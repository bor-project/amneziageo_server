using System.Net;
using System.Net.Http.Json;
using System.Text;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Outbounds;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Outbound;
using Microsoft.EntityFrameworkCore;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ImportNameTests
{
    private const string Sample = """
        [Interface]
        PrivateKey = OMMbTfBGZmzVOenkLmL2hFRuMbAG9pOZzTZ4L1H+wF0=
        Address = 10.84.50.2/32, fd00::5/128
        DNS = 1.1.1.1
        [Peer]
        PublicKey = eOWG0GXBLrpZOZ+FCwCJTvZ5rBEKHZ8NPvxNM3nJPl4=
        Endpoint = bor.sytes.net:51821
        """;

    [Theory]
    [InlineData("Awg1")]
    [InlineData("1awg")]
    [InlineData("bor.sytes.net")]
    [InlineData("192.168.1.137")]
    [InlineData("Москва")]
    [InlineData("серверыx")]
    [InlineData("_abc")]
    [InlineData("-abc")]
    [InlineData("srv+one")]
    [InlineData("srv@one")]
    [InlineData("a=b")]
    [InlineData("123456789012345")]
    public void ValidInterfaceNamesAreAcceptedByBothModels(string name)
    {
        Assert.Null(ConfigRules.CheckName(name));
        Assert.Null(OutboundRules.CheckName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("one two")]
    [InlineData("one\ttwo")]
    [InlineData("a\0b")]
    [InlineData("a\u0085b")]
    [InlineData("a\u00a0b")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a,b")]
    [InlineData("a\"b")]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a%d")]
    [InlineData("1234567890123456")]
    [InlineData("серверыа")]
    public void UnsupportedInterfaceNamesAreRefusedByBothModels(string name)
    {
        Assert.Equal("bad-interface-name", ConfigRules.CheckName(name)!.Code);
        Assert.Equal("bad-interface-name", OutboundRules.CheckName(name)!.Code);
    }

    [Theory]
    [InlineData("bor.sytes.net", "bor.sytes.net")]
    [InlineData("192.168.1.137", "192.168.1.137")]
    [InlineData("10.8.0.1/24", "10.8.0.1")]
    [InlineData("[2001:db8::1]", "2001-db8-1")]
    [InlineData("Москва", "Москва")]
    [InlineData("", "out0")]
    public void SuggestedNamesComeFromAddresses(string source, string expected)
    {
        Assert.Equal(expected, InterfaceNames.Suggest(source, [], "out0"));
    }

    [Theory]
    [InlineData("very-long-vpn.example.net")]
    [InlineData("2001:db8:abcd:1234:5678:9abc:def0:1234")]
    [InlineData("длинныйсервер.рф")]
    public void LongNamesKeepTheByteLimitAndProduceAvailableAlternatives(string host)
    {
        var first = InterfaceNames.Suggest(host, [], "out0");
        Assert.Null(ConfigRules.CheckName(first));
        Assert.InRange(Encoding.UTF8.GetByteCount(first), 1, 15);
        Assert.Equal(first, InterfaceNames.Suggest(host, [], "out0"));
        var second = InterfaceNames.Suggest(host, [first], "out0");
        Assert.NotEqual(first, second);
        Assert.Null(ConfigRules.CheckName(second));
        Assert.InRange(Encoding.UTF8.GetByteCount(second), 1, 15);
    }

    [Fact]
    public async Task ImportApiSuggestsAnAvailableNameAndPreservesManualNames()
    {
        using var bench = new Bench();
        Assert.True((await bench.Configs.AddAsync(ConfigDefaults.Fresh("bor.sytes.net") with { IsEnabled = false }, default).ConfigureAwait(false)).IsOk);
        var first = OutboundImport.Read(Sample, "bor.sytes.net-2").Outbound!;
        Assert.True((await bench.Outbounds.AddAsync(first with { IsEnabled = false }, default).ConfigureAwait(false)).IsOk);
        Assert.True((await bench.Balancers.AddAsync(new Balancer { Name = "bor.sytes.net-3", Members = [first.Name] }, default).ConfigureAwait(false)).IsOk);
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, configure: web => web.MapOutbounds()).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        for (var count = 0; count < 2; count++)
        {
            using var imported = await api.PostAsJsonAsync("/api/outbounds/import", new { name = "", config = Sample }).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
            Assert.Equal("bor.sytes.net-4", (await imported.Content.ReadFromJsonAsync<OutboundResponse>().ConfigureAwait(false))!.Name);
        }
        using var named = await api.PostAsJsonAsync("/api/outbounds/import", new { name = "Москва", config = Sample }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal("Москва", (await named.Content.ReadFromJsonAsync<OutboundResponse>().ConfigureAwait(false))!.Name);
        using var existing = await api.PostAsJsonAsync("/api/outbounds/import", new { name = first.Name, config = Sample }).ConfigureAwait(false);
        Assert.Equal(first.Name, (await existing.Content.ReadFromJsonAsync<OutboundResponse>().ConfigureAwait(false))!.Name);
        using var server = await api.PostAsJsonAsync("/api/configs/import", new { name = "", text = Sample }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, server.StatusCode);
        Assert.Equal("10.84.50.2", (await server.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!.Name);
    }

    [Theory]
    [InlineData("bor.sytes.net")]
    [InlineData("192.168.1.137")]
    [InlineData("[2001:db8::1]")]
    [InlineData("very-long-vpn.example.net")]
    [InlineData("Москва.рф")]
    public async Task ImportedAddressesProduceUsableNamesThroughTheApi(string host)
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, configure: web => web.MapOutbounds()).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await api.PostAsJsonAsync("/api/outbounds/import", new { name = "", config = Sample.Replace("bor.sytes.net", host, StringComparison.Ordinal) }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var found = (await response.Content.ReadFromJsonAsync<OutboundResponse>().ConfigureAwait(false))!;
        Assert.Null(ConfigRules.CheckName(found.Name));
        Assert.Equal(InterfaceNames.Suggest(found.Host, [], "out0"), found.Name);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("foo:bar")]
    [InlineData("серверыа")]
    public async Task ImportApiRefusesUnsupportedManualNames(string name)
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, configure: web => web.MapOutbounds()).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var outbound = await api.PostAsJsonAsync("/api/outbounds/import", new { name, config = Sample }).ConfigureAwait(false);
        using var config = await api.PostAsJsonAsync("/api/configs/import", new { name, text = Sample }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, outbound.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, config.StatusCode);
        Assert.Contains("bad-interface-name", await outbound.Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);
        Assert.Contains("bad-interface-name", await config.Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\u0085node\u0085", "node")]
    [InlineData("\uFEFFnode", "\uFEFFnode")]
    public async Task ImportedManualNamesUseDotNetWhitespaceNormalization(string name, string expected)
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, configure: web => web.MapOutbounds()).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var imported = await api.PostAsJsonAsync("/api/outbounds/import", new { name, config = Sample }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal(expected, (await imported.Content.ReadFromJsonAsync<OutboundResponse>().ConfigureAwait(false))!.Name);
    }

    [Fact]
    public async Task MissingAddressesHaveAnExplicitImportOutcome()
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, configure: web => web.MapOutbounds()).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var outbound = await api.PostAsJsonAsync("/api/outbounds/import", new { name = "", config = Sample.Replace("Endpoint = bor.sytes.net:51821", "", StringComparison.Ordinal) }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, outbound.StatusCode);
        using var config = await api.PostAsJsonAsync("/api/configs/import", new { name = "", text = Sample.Replace("Address = 10.84.50.2/32, fd00::5/128", "", StringComparison.Ordinal) }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        Assert.Equal("awg0", (await config.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!.Name);
    }

    [Fact]
    public async Task InterfacesAndOutboundsCannotClaimEachOthersNames()
    {
        using var bench = new Bench();
        var config = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("Москва") with { IsEnabled = false }, default).ConfigureAwait(false)).Record!;
        var outbound = OutboundImport.Read(Sample, "Москва").Outbound! with { IsEnabled = false };
        Assert.Equal(OutboundOutcome.NameTaken, (await bench.Outbounds.AddAsync(outbound, default).ConfigureAwait(false)).Outcome);
        var saved = (await bench.Outbounds.AddAsync(outbound with { Name = "bor.sytes.net" }, default).ConfigureAwait(false)).Record!;
        Assert.Equal(ConfigOutcome.NameTaken, (await bench.Configs.AddAsync(ConfigDefaults.Fresh(saved.Name) with { IsEnabled = false }, default).ConfigureAwait(false)).Outcome);
        Assert.Equal(ConfigOutcome.NameTaken, (await bench.Configs.ChangeAsync(config.Id, config with { Name = saved.Name }, default).ConfigureAwait(false)).Outcome);
        Assert.Equal(OutboundOutcome.NameTaken, (await bench.Outbounds.ChangeAsync(saved.Id, saved with { Name = config.Name }, default).ConfigureAwait(false)).Outcome);
        Assert.Equal("Москва", (await bench.Configs.FindAsync(config.Id, default).ConfigureAwait(false))!.Name);
    }

    [Fact]
    public async Task ConcurrentApiCreatesCannotClaimTheSameInterfaceName()
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, configure: web => web.MapOutbounds()).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        for (var number = 0; number < 8; number++)
        {
            var name = "race" + number;
            var endpoint = ConfigDefaults.Fresh(name) with { IsEnabled = false, Host = "vpn.example" };
            var left = api.PostAsJsonAsync("/api/configs", new ConfigRequest(name, endpoint.Host, endpoint.ListenPort,
                [.. endpoint.Address], [.. endpoint.Dns], [.. endpoint.AllowedIps], endpoint.Mtu, endpoint.Keepalive, false,
                endpoint.Nat, false, [.. endpoint.Blocked], endpoint.PrivateKey, endpoint.PresharedKey,
                ConfigAnswers.Obfuscation(endpoint.Obfuscation, true), WebSocket: false));
            var right = api.PostAsJsonAsync("/api/outbounds", new { name, kind = "local", isEnabled = false });
            var responses = await Task.WhenAll(left, right).ConfigureAwait(false);
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
                Assert.Contains("name-taken", await responses.Single(response => response.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
    }

    [Fact]
    public async Task ExistingNamesRemainUnchangedAndLegacyCollisionsCanBeEdited()
    {
        using var bench = new Bench();
        var config = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { IsEnabled = false }, default).ConfigureAwait(false)).Record!;
        var outbound = (await bench.Outbounds.AddAsync(OutboundImport.Read(Sample, "oldout").Outbound! with { IsEnabled = false }, default).ConfigureAwait(false)).Record!;
        await bench.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Outbounds SET Name = {config.Name} WHERE Id = {outbound.Id}").ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();
        var changed = await bench.Configs.ChangeAsync(config.Id, config with { Mtu = 1300 }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk);
        Assert.Equal(config.Name, changed.Record!.Name);
        var held = (await bench.Outbounds.FindAsync(outbound.Id, default).ConfigureAwait(false))!;
        var edited = await bench.Outbounds.ChangeAsync(held.Id, held with { Mtu = 1280 }, default).ConfigureAwait(false);
        Assert.True(edited.IsOk);
        Assert.Equal(config.Name, edited.Record!.Name);
    }
}
