using System.Net;
using System.Net.Http.Json;
using System.Text;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Tests;

public class CpsTests
{
    [Fact]
    public void FreshInterfacesHaveDistinctQuicSignatures()
    {
        var first = ConfigDefaults.Fresh("awg1");
        var second = ConfigDefaults.Fresh("awg2");

        Assert.NotNull(first.Obfuscation.I1);
        Assert.NotEqual(first.Obfuscation.I1, second.Obfuscation.I1);
        Assert.True(CpsPacket.IsValid(first.Obfuscation.I1));
        Assert.True(first.Obfuscation.I1.Length < 1024);
        Assert.Equal(1, first.Obfuscation.Jc);
        Assert.All(new[] { first.Obfuscation.I2, first.Obfuscation.I3, first.Obfuscation.I4, first.Obfuscation.I5 }, Assert.Null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<b 0x00Aaff><r 22><rc 8><rd 5><t><c>")]
    [InlineData("<r 0><r +12>")]
    [InlineData(" <b 0x01> <t> ")]
    [InlineData("\"<b 0x01><r 12>\"")]
    [InlineData("'<b 0x01><r 12>'")]
    public void SupportedExistingTagsRemainValid(string? value)
    {
        Assert.True(CpsPacket.IsValid(value));
    }

    [Theory]
    [InlineData("<b 0x0>")]
    [InlineData("<b 0xgg>")]
    [InlineData("<b 01>")]
    [InlineData("<r -1>")]
    [InlineData("<r 2147483648>")]
    [InlineData("<r 65508>")]
    [InlineData("<r 65507><t>")]
    [InlineData("<rc many>")]
    [InlineData("<rd 10")]
    [InlineData("<t 1>")]
    [InlineData("<unknown 1>")]
    [InlineData("before<r 12>")]
    public void InvalidOrExcessiveDescriptionsAreRefused(string value)
    {
        var shape = ConfigDefaults.Obfuscation() with { I1 = value };

        Assert.Equal("bad-special", ConfigRules.CheckObfuscation(shape)!.Code);
    }

    [Fact]
    public void DescriptionTagAndPacketLimitsAreIndependent()
    {
        Assert.True(CpsPacket.IsValid("<r 65507>"));
        Assert.True(CpsPacket.IsValid(string.Concat(Enumerable.Repeat("<t>", 341))));
        Assert.True(CpsPacket.IsValid(string.Concat(Enumerable.Repeat("<r 1>", CpsPacket.MaxTags))));
        Assert.False(CpsPacket.IsValid(string.Concat(Enumerable.Repeat("<r 1>", CpsPacket.MaxTags + 1))));
        Assert.False(CpsPacket.IsValid(new string(' ', ConfigRules.MaxSpecialLength) + "<t>"));
    }

    [Fact]
    public async Task AllFiveFullSignaturesRoundTripThroughStorageAndImport()
    {
        using var bench = new Bench();
        var signatures = Enumerable.Range(1, 5).Select(value => "<b 0x" + new string((char)('a' + value), 2400) + ">").ToArray();
        var fresh = ConfigDefaults.Fresh("awg1");
        var shape = fresh.Obfuscation with
        {
            I1 = signatures[0], I2 = signatures[1], I3 = signatures[2], I4 = signatures[3], I5 = signatures[4],
        };
        var draft = fresh with { Obfuscation = shape };

        Assert.Null(ConfigRules.Check(draft));
        var added = await bench.Configs.AddAsync(draft, default).ConfigureAwait(false);
        Assert.True(added.IsOk);
        var stored = await bench.Configs.FindAsync(added.Record!.Id, default).ConfigureAwait(false);
        Assert.Equal(shape, stored!.Obfuscation);

        var text = "[Interface]\nPrivateKey = " + fresh.PrivateKey + "\nAddress = 10.8.0.1/24\n" +
            string.Join("\n", signatures.Select((signature, index) => "I" + (index + 1) + " = " + signature));
        var imported = ConfigImport.Read(text, "awg2");
        Assert.Null(imported.Fault);
        Assert.Equal(signatures, new[] { imported.Config!.Obfuscation.I1, imported.Config.Obfuscation.I2,
            imported.Config.Obfuscation.I3, imported.Config.Obfuscation.I4, imported.Config.Obfuscation.I5 });
    }

    [Fact]
    public void AnImportWithoutASignatureKeepsItEmpty()
    {
        var fresh = ConfigDefaults.Fresh("awg1");
        var imported = ConfigImport.Read("[Interface]\nPrivateKey = " + fresh.PrivateKey + "\nAddress = 10.8.0.1/24\n", "awg2");

        Assert.Null(imported.Fault);
        Assert.Null(imported.Config!.Obfuscation.I1);
        Assert.True(ConfigRules.IsPlain(imported.Config.Obfuscation));
    }

    [Fact]
    public async Task TheApiKeepsDefaultAndFullSignaturesInClientExports()
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var fresh = (await api.GetFromJsonAsync<ConfigResponse>("/api/configs/draft?name=awg828").ConfigureAwait(false))!;
        var shape = ConfigAnswers.Settings(fresh.Obfuscation);
        Assert.NotNull(shape.I1);
        var config = ConfigDefaults.Fresh("awg828") with { Host = "vpn.example", IsEnabled = false, Obfuscation = shape };
        var body = Request(config);
        using var created = await api.PostAsJsonAsync("/api/configs", body).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = (await created.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!;
        var pair = Curve25519.Create();
        using var made = await api.PostAsJsonAsync("/api/clients", new ClientRequest(saved.Id, "cps-client",
            pair.PrivateKey, pair.PublicKey, "", ["10.8.0.2/32"], false, "")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var client = (await made.Content.ReadFromJsonAsync<ClientResponse>().ConfigureAwait(false))!;
        var exported = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!;
        Assert.Contains("I1 = " + shape.I1, exported.Text, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(exported.Text), 1, 2953);
        Assert.InRange(Encoding.UTF8.GetByteCount(exported.Link), 1, 2953);

        var full = "<b 0x" + new string('a', 2400) + ">";
        var changed = shape with { I1 = full, I2 = full, I3 = full, I4 = full, I5 = full };
        using var updated = await api.PutAsJsonAsync($"/api/configs/{saved.Id}", body with
        {
            Obfuscation = ConfigAnswers.Obfuscation(changed, true),
        }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var read = (await api.GetFromJsonAsync<ConfigResponse>($"/api/configs/{saved.Id}").ConfigureAwait(false))!;
        Assert.Equal(changed, ConfigAnswers.Settings(read.Obfuscation));
        var fullExport = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!;
        foreach (var index in Enumerable.Range(1, 5))
        {
            Assert.Contains("I" + index + " = " + full, fullExport.Text, StringComparison.Ordinal);
        }
    }

    private static ConfigRequest Request(ServerConfig config) => new(config.Name, config.Host, config.ListenPort,
        [.. config.Address], [.. config.Dns], [.. config.AllowedIps], config.Mtu, config.Keepalive, false,
        config.Nat, false, [.. config.Blocked], config.PrivateKey, config.PresharedKey,
        ConfigAnswers.Obfuscation(config.Obfuscation, true), config.OfflineAfter, WebSocket: false,
        ServicesPort: config.ListenPort, WebSocketPath: config.WebSocketPath);
}
