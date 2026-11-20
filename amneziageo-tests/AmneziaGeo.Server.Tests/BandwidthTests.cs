using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Tests;

public sealed class BandwidthTests
{
    [Fact]
    public async Task BothAddressFamiliesShareOneCeilingInEachDirection()
    {
        var tools = new Tools();
        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0",
            [Client(42, 1_000_000, 2_000_000), Client(43, 0, 0, "10.0.0.3/32")], CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Equal(125_000, tools.Classes[tools.Ifb].Single()["options"]!["rate"]!.GetValue<long>());
        Assert.Equal(250_000, tools.Classes["awg0"].Single()["options"]!["rate"]!.GetValue<long>());
        Assert.Equal(2, tools.Filters[("awg0", "a804:")].Count);
        Assert.Equal(2, tools.Filters[(tools.Ifb, "a804:")].Count);
        Assert.All(tools.Filters[("awg0", "a804:")], one => Assert.Equal("a804:1", one["options"]!["classid"]!.GetValue<string>()));
        Assert.Equal(2, tools.Filters[("awg0", "ffff:")].Count);
        Assert.DoesNotContain(tools.Mutations, one => one.Contains("mark", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADownloadOnlyCeilingLeavesUploadUnshaped()
    {
        var tools = new Tools();
        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 0, 2_000_000)], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.DoesNotContain(tools.Qdiscs["awg0"], one => one["kind"]!.GetValue<string>() == "ingress");
        Assert.Equal("noqueue", tools.Qdiscs[tools.Ifb].Single()["kind"]!.GetValue<string>());
        Assert.Empty(tools.Classes.GetValueOrDefault(tools.Ifb, []));
    }

    [Fact]
    public async Task ARestartAdoptsTheExistingQueuesWithoutResettingThem()
    {
        var tools = new Tools();
        var clients = new[] { Client(42, 1_000_000, 2_000_000) };
        Assert.True((await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Mutations.Clear();

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Empty(tools.Mutations);
    }

    [Fact]
    public async Task ARestartRepairsAMissingClassifier()
    {
        var tools = new Tools();
        var clients = new[] { Client(42, 0, 2_000_000) };
        Assert.True((await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Filters[("awg0", "a804:")].Clear();
        tools.Mutations.Clear();

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Equal(2, tools.Filters[("awg0", "a804:")].Count);
        Assert.DoesNotContain(tools.Mutations, one => one.StartsWith("tc qdisc add", StringComparison.Ordinal));
        Assert.DoesNotContain(tools.Mutations, one => one.StartsWith("tc class replace", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fq_codel", "8001:")]
    [InlineData("htb", "a804:")]
    public async Task AnUnownedRootIsNeverReplaced(string kind, string handle)
    {
        var tools = new Tools();
        tools.Qdiscs["awg0"] = [Root(kind, handle)];

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 0, 1_000_000)], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.Empty(tools.Mutations);
        Assert.Equal(kind, tools.Qdiscs["awg0"].Single()["kind"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("ingress", "ffff:")]
    [InlineData("clsact", "ffff:")]
    public async Task AForeignIngressIsNeverReplaced(string kind, string handle)
    {
        var tools = new Tools();
        tools.Qdiscs["awg0"].Add(new JsonObject { ["kind"] = kind, ["handle"] = handle, ["parent"] = "ffff:fff1" });

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 1_000_000, 0)], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.Empty(tools.Mutations);
    }

    [Fact]
    public async Task ADownloadMarkerDoesNotClaimAForeignIngress()
    {
        var tools = new Tools();
        tools.Qdiscs["awg0"].Add(new JsonObject { ["kind"] = "ingress", ["handle"] = "ffff:", ["parent"] = "ffff:fff1" });
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 0, 1_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Mutations.Clear();

        var result = await host.ApplyAsync("awg0", [Client(42, 1_000_000, 1_000_000)], CancellationToken.None).ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.Empty(tools.Mutations);
        Assert.True((await host.ApplyAsync("awg0", [], CancellationToken.None).ConfigureAwait(false)).IsDone);
        Assert.Contains(tools.Qdiscs["awg0"], one => one["kind"]!.GetValue<string>() == "ingress");
    }

    [Fact]
    public async Task ACollidingInterfaceNameNeedsTheOwnershipAliasAndIfbKind()
    {
        foreach (var kind in new[] { "ifb", "dummy" })
        {
            var tools = new Tools();
            var plan = BandwidthPlan.Create("awg0", [Client(42, 1_000_000, 1_000_000)]);
            tools.Links[plan.Ifb] = Link(plan.Ifb, kind, kind == "ifb" ? "somebody else" : plan.Owner + "pending");
            tools.Qdiscs[plan.Ifb] = [Root("noqueue", "0:")];

            var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 1_000_000, 1_000_000)], CancellationToken.None)
                .ConfigureAwait(false);

            Assert.False(result.IsDone);
            Assert.Empty(tools.Mutations);
        }
    }

    [Fact]
    public async Task UnlimitedRemovesOnlyTheResourcesThatTheModuleOwns()
    {
        var tools = new Tools();
        tools.Links["other"] = Link("other", "dummy", "somebody else");
        tools.Qdiscs["other"] = [Root("cake", "8001:")];
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 1_000_000, 2_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        var ifb = tools.Ifb;

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [], CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.False(tools.Links.ContainsKey(ifb));
        Assert.Equal("noqueue", tools.Qdiscs["awg0"].Single()["kind"]!.GetValue<string>());
        Assert.Equal("cake", tools.Qdiscs["other"].Single()["kind"]!.GetValue<string>());
        tools.Mutations.Clear();
        Assert.True((await host.ApplyAsync("awg0", [], CancellationToken.None).ConfigureAwait(false)).IsDone);
        Assert.Empty(tools.Mutations);
    }

    [Fact]
    public async Task UnlimitedLeavesForeignResourcesAlone()
    {
        var tools = new Tools();
        tools.Qdiscs["awg0"] = [Root("cake", "8001:")];

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [], CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Empty(tools.Mutations);
    }

    [Fact]
    public async Task CleanupRefusesAReplacementRootEvenWhenTheMarkerSurvives()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 0, 2_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Qdiscs["awg0"] = [Root("cake", "8001:")];
        tools.Mutations.Clear();

        var result = await host.ApplyAsync("awg0", [], CancellationToken.None).ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.Empty(tools.Mutations);
        Assert.True(tools.Links.ContainsKey(tools.Ifb));
    }

    [Fact]
    public async Task AFailedFirstApplyRollsBackItsNewInterfacesAndQueues()
    {
        var tools = new Tools { Fail = call => call.StartsWith("tc filter replace dev awg0 parent ffff:", StringComparison.Ordinal) };

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 1_000_000, 2_000_000)], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.DoesNotContain("rollback:", result.Message, StringComparison.Ordinal);
        Assert.Single(tools.Links);
        Assert.Equal("noqueue", tools.Qdiscs["awg0"].Single()["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task AFailedUpdateRemovesNewClassesAndKeepsTheExistingCeiling()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 1_000_000, 2_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Fail = call => call.StartsWith("tc qdisc add dev awg0 parent a804:2", StringComparison.Ordinal);

        var result = await host.ApplyAsync("awg0",
            [Client(42, 1_000_000, 2_000_000), Client(43, 1_000_000, 2_000_000, "10.0.0.3/32")], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.DoesNotContain("rollback:", result.Message, StringComparison.Ordinal);
        Assert.Single(tools.Classes["awg0"]);
        Assert.Single(tools.Classes[tools.Ifb]);
        Assert.EndsWith(":pending", tools.Links[tools.Ifb]["ifalias"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationRollsBackWithAnIndependentToken()
    {
        var tools = new Tools();
        using var source = new CancellationTokenSource();
        tools.BeforeChange = call =>
        {
            if (call.StartsWith("tc filter replace dev awg0 parent ffff:", StringComparison.Ordinal))
            {
                source.Cancel();
                source.Token.ThrowIfCancellationRequested();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 1_000_000, 2_000_000)], source.Token))
            .ConfigureAwait(false);

        Assert.Single(tools.Links);
        Assert.Equal("noqueue", tools.Qdiscs["awg0"].Single()["kind"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(7)]
    public async Task InvalidRatesDoNotReachTheHost(long rate)
    {
        var tools = new Tools();

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, rate, 0)], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.Empty(tools.Calls);
    }

    [Fact]
    public async Task OverlappingClientsAndInvalidAddressesDoNotReachTheHost()
    {
        var bad = new ClientBandwidth[][]
        {
            [Client(42, 1_000_000, 0, "10.0.0.0/24"), Client(43, 1_000_000, 0, "10.0.0.2/32")],
            [Client(42, 1_000_000, 0, "not-an-address")],
            [Client(42, 1_000_000, 0), Client(42, 1_000_000, 0)],
        };
        foreach (var clients in bad)
        {
            var tools = new Tools();
            var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false);
            Assert.False(result.IsDone);
            Assert.Empty(tools.Calls);
        }
    }

    [Fact]
    public async Task AReadFailureDoesNotBecomePermissionToCreateAnInterface()
    {
        var tools = new Tools { RefuseIfbRead = true };

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 1_000_000, 0)], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.Empty(tools.Mutations);
    }

    [Fact]
    public async Task ChangingAnAddressRemovesItsOldClassifierWithoutReplacingTheRoot()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 0, 2_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Mutations.Clear();

        var result = await host.ApplyAsync("awg0", [Client(42, 0, 1_000_000, "10.0.0.5/32")], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Single(tools.Filters[("awg0", "a804:")]);
        Assert.Equal("10.0.0.5/32", tools.Filters[("awg0", "a804:")].Single()["options"]!["keys"]!["dst_ip"]!.GetValue<string>());
        Assert.DoesNotContain(tools.Mutations, one => one.StartsWith("tc qdisc del", StringComparison.Ordinal));
        Assert.DoesNotContain(tools.Mutations, one => one.StartsWith("tc qdisc add", StringComparison.Ordinal));
        Assert.Equal(125_000, tools.Classes["awg0"].Single()["options"]!["rate"]!.GetValue<long>());
    }

    [Fact]
    public async Task RemovingIpv4KeepsTheIpv6ClassifierUsable()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 0, 2_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);

        var result = await host.ApplyAsync("awg0", [Client(42, 0, 2_000_000, "fd00::2/128")], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Equal("ipv6", tools.Filters[("awg0", "a804:")].Single()["protocol"]!.GetValue<string>());
    }

    [Fact]
    public async Task APrefixMaskChangeDoesNotReplaceAnExistingFlowerMask()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 0, 2_000_000, "10.0.0.2/32")], CancellationToken.None).ConfigureAwait(false)).IsDone);

        var result = await host.ApplyAsync("awg0", [Client(42, 0, 2_000_000, "10.0.0.0/24")], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Equal("10.0.0.0/24", tools.Filters[("awg0", "a804:")].Single()["options"]!["keys"]!["dst_ip"]!.GetValue<string>());
    }

    [Fact]
    public async Task AFailedAddressChangeRestoresRemovedClassifiers()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 0, 2_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Fail = call => call.StartsWith("tc filter replace", StringComparison.Ordinal) && call.Contains("10.0.1.0/24", StringComparison.Ordinal);

        var result = await host.ApplyAsync("awg0", [Client(42, 0, 2_000_000, "10.0.1.0/24")], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.DoesNotContain("rollback:", result.Message, StringComparison.Ordinal);
        Assert.Equal(2, tools.Filters[("awg0", "a804:")].Count);
        Assert.Contains(tools.Filters[("awg0", "a804:")], one => one["options"]!["keys"]!["dst_ip"]!.GetValue<string>() == "10.0.0.2/32");
    }

    [Fact]
    public async Task ARestartRepairsIfbLinkStateAndMtuWithoutResettingTheQueues()
    {
        var tools = new Tools();
        var clients = new[] { Client(42, 1_000_000, 2_000_000) };
        Assert.True((await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Links[tools.Ifb]["flags"] = new JsonArray();
        tools.Links["awg0"]["mtu"] = 1300;
        tools.Mutations.Clear();

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Equal(1300, tools.Links[tools.Ifb]["mtu"]!.GetValue<int>());
        Assert.Contains(tools.Links[tools.Ifb]["flags"]!.AsArray(), one => one!.GetValue<string>() == "UP");
        Assert.DoesNotContain(tools.Mutations, one => one.StartsWith("tc qdisc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AChangedHexadecimalDefaultClassIsRefusedBeforeMutation()
    {
        var tools = new Tools();
        var clients = new[] { Client(42, 0, 2_000_000) };
        Assert.True((await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.Qdiscs["awg0"].Single(one => one["root"]?.GetValue<bool>() == true)["options"]!["default"] = "0x1";
        tools.Mutations.Clear();

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.Empty(tools.Mutations);
    }

    [Fact]
    public async Task CleanupRemovesAnOwnedIfbAfterTheTunnelDisappears()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 1_000_000, 2_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        var ifb = tools.Ifb;
        tools.Links.Remove("awg0");
        tools.Qdiscs.Remove("awg0");
        tools.Calls.Clear();

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [], CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.False(tools.Links.ContainsKey(ifb));
        Assert.DoesNotContain(tools.Calls, one => one.StartsWith("tc -j qdisc show dev awg0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OrphanCleanupPreservesAnIfbWithAReplacementRoot()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 1_000_000, 2_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        var ifb = tools.Ifb;
        tools.Links.Remove("awg0");
        tools.Qdiscs[ifb] = [Root("cake", "8001:")];
        tools.Mutations.Clear();

        var result = await host.ApplyAsync("awg0", [], CancellationToken.None).ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.True(tools.Links.ContainsKey(ifb));
        Assert.Empty(tools.Mutations);
    }

    [Fact]
    public async Task AKernelDefaultQdiscWithNoHandleCanBeReplaced()
    {
        var tools = new Tools();
        tools.Qdiscs["awg0"] = [Root("fq_codel", "0:")];

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 0, 2_000_000)], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Equal("a804:", tools.Qdiscs["awg0"].Single(one => one["root"]?.GetValue<bool>() == true)["handle"]!.GetValue<string>());
    }

    [Fact]
    public async Task LegacyTcClassTextAdoptsTheExistingCeiling()
    {
        var tools = new Tools();
        var clients = new[] { Client(42, 0, 4_000_000) };
        Assert.True((await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.ClassOutput["awg0"] = "class htb a804:1 root leaf 1: prio 0 rate 4Mbit ceil 4Mbit burst 5000b cburst 5000b\n"
            + "class fq_codel 1:2d parent 1: \nclass fq_codel 1:3d parent 1: \n";
        tools.Mutations.Clear();

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", clients, CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Empty(tools.Mutations);
    }

    [Fact]
    public async Task LegacyRoundingDoesNotHideARequestedRateChange()
    {
        var tools = new Tools();
        var host = new ClientBandwidthHost(tools);
        Assert.True((await host.ApplyAsync("awg0", [Client(42, 0, 4_000_000)], CancellationToken.None).ConfigureAwait(false)).IsDone);
        tools.ClassOutput["awg0"] = "class htb a804:1 root leaf 1: prio 0 rate 4Mbit ceil 4Mbit burst 5000b cburst 5000b\n";
        tools.Mutations.Clear();

        var result = await host.ApplyAsync("awg0", [Client(42, 0, 4_000_008)], CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsDone, result.Message);
        Assert.Contains(tools.Mutations, one => one.Contains("rate 4000008bit", StringComparison.Ordinal));
    }

    [Fact]
    public void JsonClassDumpsIgnoreOnlyTheFlowClassesOfOwnedLeaves()
    {
        var text = """
            [{"kind":"htb","handle":"a804:1","options":{"rate":500000,"ceil":500000}},
             {"kind":"fq_codel","handle":"1:2d","parent":"1:"},
             {"kind":"fq_codel","handle":"2:2d","parent":"2:"}]
            """;

        var read = BandwidthClasses.Read(text, new HashSet<string> { "1:" });

        Assert.Equal(2, read.Length);
        Assert.Equal("a804:1", read[0].GetProperty("handle").GetString());
        Assert.Equal("2:2d", read[1].GetProperty("handle").GetString());
    }

    [Fact]
    public void LegacyClassRatesUseTheUnitsOfTc()
    {
        var read = BandwidthClasses.Read(
            "class htb a804:1 root leaf 1: prio 0 rate 1.6Mbit ceil 2Gbit burst 1600b cburst 1600b",
            new HashSet<string>());

        Assert.Equal(200000, read.Single().GetProperty("options").GetProperty("rate").GetInt64());
        Assert.Equal(250000000, read.Single().GetProperty("options").GetProperty("ceil").GetInt64());
    }

    [Fact]
    public async Task AFailedExplicitOwnershipMarkerRemovesTheNewUnmarkedIfb()
    {
        var tools = new Tools
        {
            Fail = call => call.StartsWith("ip link set dev agb", StringComparison.Ordinal)
                && call.Contains(" alias ", StringComparison.Ordinal),
        };

        var result = await new ClientBandwidthHost(tools).ApplyAsync("awg0", [Client(42, 1_000_000, 2_000_000)], CancellationToken.None)
            .ConfigureAwait(false);

        Assert.False(result.IsDone);
        Assert.DoesNotContain("rollback:", result.Message, StringComparison.Ordinal);
        Assert.Single(tools.Links);
        Assert.Equal("noqueue", tools.Qdiscs["awg0"].Single()["kind"]!.GetValue<string>());
    }

    private static ClientBandwidth Client(long id, long upload, long download, params string[] addresses) =>
        new(id, addresses.Length == 0 ? ["10.0.0.2/32", "fd00::2/128"] : addresses, upload, download);

    private static JsonObject Link(string name, string kind, string alias = "") =>
        new() { ["ifindex"] = name == "awg0" ? 1 : 2, ["ifname"] = name, ["mtu"] = 1420, ["flags"] = new JsonArray("UP"), ["ifalias"] = alias, ["linkinfo"] = new JsonObject { ["info_kind"] = kind } };

    private static JsonObject Root(string kind, string handle) =>
        new() { ["kind"] = kind, ["handle"] = handle, ["root"] = true, ["options"] = new JsonObject { ["default"] = "0x0", ["direct_qlen"] = 1024 } };

    private sealed class Tools : IHostCommands
    {
        public Dictionary<string, JsonObject> Links { get; } = new(StringComparer.Ordinal) { ["awg0"] = Link("awg0", "wireguard") };
        public Dictionary<string, List<JsonObject>> Qdiscs { get; } = new(StringComparer.Ordinal) { ["awg0"] = [Root("noqueue", "0:")] };
        public Dictionary<string, List<JsonObject>> Classes { get; } = new(StringComparer.Ordinal);
        public Dictionary<(string Device, string Parent), List<JsonObject>> Filters { get; } = [];
        public Dictionary<string, string> ClassOutput { get; } = new(StringComparer.Ordinal);
        public List<string> Calls { get; } = [];
        public List<string> Mutations { get; } = [];
        public Func<string, bool>? Fail { get; set; }
        public Action<string>? BeforeChange { get; set; }
        public bool RefuseIfbRead { get; init; }
        public string Ifb => Links.Keys.Single(one => one.StartsWith("agb", StringComparison.Ordinal));

        public Task<CommandResult> RunAsync(string file, IReadOnlyList<string> arguments, string? input, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var call = file + " " + string.Join(" ", arguments);
            Calls.Add(call);
            var args = arguments.ToArray();
            if (file == "ip" && args[0] == "-j")
            {
                var name = Value(args, "dev");
                if (RefuseIfbRead && name.StartsWith("agb", StringComparison.Ordinal))
                {
                    return Answer(1, "", "Operation not permitted");
                }

                return Links.TryGetValue(name, out var link)
                    ? Answer(0, JsonSerializer.Serialize(new[] { link }))
                    : Answer(1, "", $"Device \"{name}\" does not exist.");
            }

            if (file == "tc" && args[0] == "-j")
            {
                var name = Value(args, "dev");
                if (args[1] == "class" && ClassOutput.TryGetValue(name, out var text))
                {
                    return Answer(0, text);
                }
                var rows = args[1] switch
                {
                    "qdisc" => Qdiscs.GetValueOrDefault(name, []),
                    "class" => Classes.GetValueOrDefault(name, []),
                    "filter" => Filters.GetValueOrDefault((name, Value(args, "parent")), []),
                    _ => throw new InvalidOperationException(call),
                };

                return Answer(0, JsonSerializer.Serialize(rows));
            }

            Mutations.Add(call);
            BeforeChange?.Invoke(call);
            if (Fail?.Invoke(call) == true)
            {
                return Answer(1, "", "injected failure");
            }

            if (file == "ip")
            {
                ChangeLink(args);
            }
            else if (file == "tc")
            {
                ChangeTraffic(args);
            }
            else
            {
                throw new InvalidOperationException(call);
            }

            return Answer(0, "");
        }

        private void ChangeLink(string[] args)
        {
            var name = Value(args, args[1] == "add" ? "name" : "dev");
            switch (args[1])
            {
                case "add":
                    Links.Add(name, Link(name, Value(args, "type")));
                    Qdiscs[name] = [Root("noqueue", "0:")];
                    break;
                case "set":
                    if (args.Contains("alias", StringComparer.Ordinal))
                    {
                        Links[name]["ifalias"] = Value(args, "alias");
                    }
                    if (args.Contains("mtu", StringComparer.Ordinal))
                    {
                        Links[name]["mtu"] = int.Parse(Value(args, "mtu"), CultureInfo.InvariantCulture);
                    }
                    if (args.Contains("up", StringComparer.Ordinal))
                    {
                        Links[name]["flags"] = new JsonArray("UP");
                    }
                    break;
                case "del":
                    Links.Remove(name);
                    Qdiscs.Remove(name);
                    Classes.Remove(name);
                    foreach (var key in Filters.Keys.Where(one => one.Device == name).ToArray())
                    {
                        Filters.Remove(key);
                    }
                    break;
                default:
                    throw new InvalidOperationException(string.Join(" ", args));
            }
        }

        private void ChangeTraffic(string[] args)
        {
            var name = Value(args, "dev");
            var parent = args.Contains("parent", StringComparer.Ordinal) ? Value(args, "parent") : "";
            if (args[0] == "qdisc")
            {
                var ingress = args.Contains("ingress", StringComparer.Ordinal);
                var root = args.Contains("root", StringComparer.Ordinal);
                var handle = Value(args, "handle");
                if (args[1] == "del")
                {
                    Qdiscs[name].RemoveAll(one => root
                        ? one["root"]?.GetValue<bool>() == true || one["parent"]?.GetValue<string>().StartsWith("a804:", StringComparison.Ordinal) == true
                        : ingress ? one["kind"]!.GetValue<string>() == "ingress" : one["handle"]!.GetValue<string>() == handle);
                    if (root)
                    {
                        Qdiscs[name].Add(Root("noqueue", "0:"));
                        Classes.Remove(name);
                    }
                    Filters.Remove((name, ingress ? "ffff:" : root ? "a804:" : parent));
                    return;
                }

                if (root)
                {
                    Qdiscs[name].RemoveAll(one => one["root"]?.GetValue<bool>() == true);
                    Qdiscs[name].Add(Root("htb", handle));
                }
                else
                {
                    Qdiscs[name].Add(new JsonObject
                    {
                        ["kind"] = ingress ? "ingress" : "fq_codel", ["handle"] = handle,
                        ["parent"] = ingress ? "ffff:fff1" : parent,
                    });
                }
                return;
            }

            if (args[0] == "class")
            {
                var handle = Value(args, "classid");
                var classes = Classes.GetValueOrDefault(name, []);
                Classes[name] = classes;
                classes.RemoveAll(one => one["handle"]!.GetValue<string>() == handle);
                if (args[1] == "del")
                {
                    Qdiscs[name].RemoveAll(one => one["parent"]?.GetValue<string>() == handle);
                }
                else
                {
                    var bits = long.Parse(Value(args, "rate")[..^3], CultureInfo.InvariantCulture);
                    classes.Add(new JsonObject
                    {
                        ["kind"] = "htb", ["handle"] = handle, ["parent"] = parent,
                        ["options"] = new JsonObject { ["rate"] = bits / 8, ["ceil"] = bits / 8 },
                    });
                }
                return;
            }

            if (args[0] == "filter")
            {
                var protocol = Value(args, "protocol");
                var handle = Value(args, "handle");
                var pref = int.Parse(Value(args, "pref"), CultureInfo.InvariantCulture);
                var filters = Filters.GetValueOrDefault((name, parent), []);
                Filters[(name, parent)] = filters;
                var identity = int.Parse(handle.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (args[1] != "del")
                {
                    var key = args.Contains("src_ip", StringComparer.Ordinal) ? "src_ip" : "dst_ip";
                    var prefix = Value(args, key);
                    var old = filters.SingleOrDefault(one => one["protocol"]!.GetValue<string>() == protocol
                        && one["pref"]!.GetValue<int>() == pref && one["options"]!["handle"]!.GetValue<int>() == identity);
                    if (old is not null && old["options"]!["keys"]![key]!.GetValue<string>().Split('/')[1] != prefix.Split('/')[1])
                    {
                        throw new InvalidOperationException("the flower classifier refused a mask change");
                    }
                    if (filters.Any(one => one["protocol"]!.GetValue<string>() == protocol
                        && one["options"]!["keys"]![key]?.GetValue<string>() == prefix
                        && one["options"]!["handle"]!.GetValue<int>() != identity))
                    {
                        throw new InvalidOperationException("the flower classifier refused a duplicate key");
                    }
                }
                filters.RemoveAll(one => one["protocol"]!.GetValue<string>() == protocol
                    && one["pref"]!.GetValue<int>() == pref && one["options"]!["handle"]!.GetValue<int>() == identity);
                if (args[1] == "del")
                {
                    return;
                }

                var source = args.Contains("src_ip", StringComparer.Ordinal);
                var options = new JsonObject
                {
                    ["handle"] = int.Parse(handle.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    ["keys"] = new JsonObject { [source ? "src_ip" : "dst_ip"] = Value(args, source ? "src_ip" : "dst_ip") },
                };
                if (args.Contains("classid", StringComparer.Ordinal))
                {
                    options["classid"] = Value(args, "classid");
                }
                else
                {
                    options["actions"] = new JsonArray(new JsonObject
                    {
                        ["kind"] = "mirred", ["mirred_action"] = "redirect", ["direction"] = "egress", ["to_dev"] = args[^1],
                    });
                }

                filters.Add(new JsonObject { ["kind"] = "flower", ["protocol"] = protocol, ["pref"] = pref, ["chain"] = 0, ["options"] = options });
                return;
            }

            throw new InvalidOperationException(string.Join(" ", args));
        }

        private static string Value(string[] args, string key) => args[Array.IndexOf(args, key) + 1];

        private static Task<CommandResult> Answer(int code, string output, string error = "") =>
            Task.FromResult(new CommandResult(code, output, error));
    }
}
