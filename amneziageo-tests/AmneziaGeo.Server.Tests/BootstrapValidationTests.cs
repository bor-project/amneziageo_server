using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

[Collection(PanelCommandTests.Utility)]
public sealed class BootstrapValidationTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ag-bootstrap-validation-");
    private readonly string? _key = Environment.GetEnvironmentVariable(Context.KeyVariable);

    /// <summary>
    /// ctor
    /// </summary>
    public BootstrapValidationTests()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, Path.Combine(_folder.FullName, "signing.pem"));
    }

    private string Database => Path.Combine(_folder.FullName, "server.db");

    private BootstrapSettings Setup() => new()
    {
        User = "compose-admin",
        PasswordFile = Path.Combine(_folder.FullName, "password"),
        Panel = new PanelSettings { Listen = ["127.0.0.1"], Port = 19443, Path = "panel" },
    };

    private async Task Check(BootstrapSettings setup, IBootstrapHost? host = null)
    {
        using var checks = new BootstrapChecks(host);
        await checks.CheckAsync(setup, setup.Panel!, Database, CancellationToken.None).ConfigureAwait(false);
    }

    [Theory]
    [InlineData("{}", "$.User")]
    [InlineData("{\"user\":\"\",\"passwordFile\":\"/secret\"}", "$.user")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/secret\",\"User\":\"other\"}", "$.User")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/secret\",\"panel\":{\"prefix\":\"bad\"}}", "$.panel.prefix")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/secret\",\"endpoints\":[{\"settings\":{}}]}", "$.endpoints[0].settings.Name")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/secret\",\"endpoints\":[null]}", "$.endpoints[0]")]
    [InlineData("[]", "$")]
    public void InvalidFieldsAreIdentified(string json, string field)
    {
        Assert.Equal(field, Assert.Throws<BootstrapFault>(() => BootstrapInput.Read(json)).Field);
    }

    [Theory]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/secret\",\"panel\":{\"port\":\"wrong\"}}")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/secret\",\"dns\":{\"filesMode\":123}}")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/secret\",\"validation\":null}")]
    public void InvalidTypedValuesAreRejected(string json)
    {
        Assert.Throws<JsonException>(() => BootstrapInput.Read(json));
    }

    [Theory]
    [InlineData("\"panel\":{\"port\":NUMBER}", "$.panel.port")]
    [InlineData("\"dns\":{\"port\":NUMBER}", "$.dns.port")]
    [InlineData("\"subscriptions\":{\"port\":NUMBER}", "$.subscriptions.port")]
    [InlineData("\"clientUpdates\":{\"port\":NUMBER}", "$.clientUpdates.port")]
    [InlineData("\"endpoints\":[{\"settings\":{\"name\":\"awg0\",\"listenPort\":NUMBER}}]", "$.endpoints[0].settings.listenPort")]
    [InlineData("\"endpoints\":[{\"settings\":{\"name\":\"awg0\",\"mtu\":NUMBER}}]", "$.endpoints[0].settings.mtu")]
    [InlineData("\"endpoints\":[{\"settings\":{\"name\":\"awg0\",\"keepalive\":NUMBER}}]", "$.endpoints[0].settings.keepalive")]
    [InlineData("\"endpoints\":[{\"settings\":{\"name\":\"awg0\",\"obfuscation\":{\"s1\":NUMBER}}}]", "$.endpoints[0].settings.obfuscation.s1")]
    public void NumericFieldsRejectQuotedNumbers(string fields, string field)
    {
        var prefix = "{\"user\":\"admin\",\"passwordFile\":\"/secret\",";
        Assert.NotNull(BootstrapInput.Read(prefix + fields.Replace("NUMBER", "123") + "}"));
        var error = Assert.Throws<JsonException>(() => BootstrapInput.Read(prefix + fields.Replace("NUMBER", "\"123\"") + "}"));
        Assert.Equal(field, error.Path);
    }

    [Fact]
    public async Task AQuotedPortIsRejectedBeforeAnySettingsAreSaved()
    {
        var setup = Setup();
        File.WriteAllText(setup.PasswordFile, "Bootstrap-only-123456789!");
        var path = Path.Combine(_folder.FullName, "bootstrap.json");
        var json = BootstrapTestJson.Write(setup);
        Assert.Contains("\"port\":19443", json);
        File.WriteAllText(path, json.Replace("\"port\":19443", "\"port\":\"19443\""));
        using var context = Context.Open(Database);
        var args = new Arguments(["bootstrap", "--file", path, "--check"]);
        Assert.Equal(78, await BootstrapCommand.RunAsync(context, args, CancellationToken.None, new BootstrapTestHost()).ConfigureAwait(false));
        Assert.Null(PanelStore.Held(Database));
        Assert.False(await context.Accounts.HasAdminAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Empty(await context.Configs.ListAsync(CancellationToken.None).ConfigureAwait(false));
        File.WriteAllText(path, json);
        Assert.Equal(0, await BootstrapCommand.RunAsync(context, args, CancellationToken.None, new BootstrapTestHost()).ConfigureAwait(false));
        Assert.Null(PanelStore.Held(Database));
        Assert.False(await context.Accounts.HasAdminAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task AForeignDnsAddressIsRejected()
    {
        var setup = Setup();
        var error = await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup with { Panel = setup.Panel! with { Domains = ["panel.test"] } }, new BootstrapTestHost { Answers = [IPAddress.Parse("203.0.113.11")] })).ConfigureAwait(false);
        Assert.Equal("panel.domains", error.Field);
    }

    [Fact]
    public async Task AForeignIpv6RecordIsRejectedEvenWhenIpv4Matches()
    {
        var setup = Setup();
        var host = new BootstrapTestHost { Answers = [IPAddress.Loopback, IPAddress.Parse("2001:db8::bad")] };
        Assert.Equal("panel.domains", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup with { Panel = setup.Panel! with { Domains = ["panel.test"] } }, host)).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task DeclaredNatAddressesMustMatchAllDnsAnswers()
    {
        var setup = Setup();
        setup = setup with
        {
            Panel = setup.Panel! with { Domains = ["panel.test"] },
            Validation = new BootstrapValidation { ExpectedAddresses = new Dictionary<string, IReadOnlyList<string>> { ["panel.test"] = ["203.0.113.11"] } },
        };
        await Check(setup, new BootstrapTestHost { Answers = [IPAddress.Parse("203.0.113.11")] }).ConfigureAwait(false);
        await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost { Answers = [IPAddress.Parse("203.0.113.12")] })).ConfigureAwait(false);
    }

    [Fact]
    public async Task UnusedExpectedAddressEntriesAreRejected()
    {
        var setup = Setup() with { Validation = new BootstrapValidation { ExpectedAddresses = new Dictionary<string, IReadOnlyList<string>> { ["typo.test"] = ["203.0.113.11"] } } };
        Assert.Equal("validation.expectedAddresses", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost())).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task AnUnavailableDnsNameIdentifiesTheHostField()
    {
        var setup = Setup();
        Assert.Equal("panel.domains", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup with { Panel = setup.Panel! with { Domains = ["missing.test"] } }, new BootstrapTestHost { ResolveError = true })).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task AnAddressNotAssignedToTheHostIsRejected()
    {
        var setup = Setup();
        Assert.Equal("panel.port", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup with { Panel = setup.Panel! with { Listen = ["203.0.113.99"] } }, new BootstrapTestHost())).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task APortOwnedByAnotherProcessIsRejected()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var setup = Setup();
        Assert.Equal("panel.port", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup with { Panel = setup.Panel! with { Port = ((IPEndPoint)listener.LocalEndpoint).Port } })).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task HttpServicesShareOneProbeWhenPathsDiffer()
    {
        var setup = TlsSetup();
        var host = new BootstrapTestHost();
        setup = setup with { Subscriptions = new SubscriptionSettings { IsEnabled = true, Separate = true, Listen = [], Port = setup.Panel!.Port, Path = "subscriptions" } };
        await Check(setup, host).ConfigureAwait(false);
        Assert.Equal(host.Reservations.Count, host.Reservations.Distinct().Count());
    }

    [Fact]
    public async Task OverlappingHttpPathsAreRejected()
    {
        var setup = TlsSetup();
        setup = setup with { ClientUpdates = new ClientUpdateSettings { IsEnabled = true, Path = "panel/sub" } };
        Assert.Equal("clientUpdates.path", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost())).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task DnsAndHttpCannotShareATcpListener()
    {
        var setup = Setup();
        setup = setup with { Dns = new DnsSettings { IsEnabled = true, Listen = ["127.0.0.1"], Port = setup.Panel!.Port } };
        Assert.Equal("panel.port", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost())).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task AnUnresponsiveUpstreamIdentifiesItsIndex()
    {
        var setup = Setup() with { Dns = new DnsSettings { IsEnabled = true, Listen = ["127.0.0.1"], Port = 1053 } };
        Assert.Equal("dns.systemServers[0]", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost { DnsReply = false })).ConfigureAwait(false)).Field);
    }

    [Theory]
    [InlineData("upstreams", "[\"1.1.1.1\"]")]
    [InlineData("selection", "\"global\"")]
    [InlineData("outbound", "\"old-way\"")]
    public void RemovedDnsFieldsAreRejectedWithTheirPath(string name, string value)
    {
        using var body = JsonDocument.Parse(value);
        var json = JsonSerializer.Serialize(new { user = "admin", passwordFile = "/secret", dns = new Dictionary<string, JsonElement> { [name] = body.RootElement } });
        Assert.Equal("$.dns." + name, Assert.Throws<BootstrapFault>(() => BootstrapInput.Read(json)).Field);
    }

    [Fact]
    public async Task InitialDnsChecksTheEffectiveSystemServers()
    {
        var setup = Setup() with { Dns = new DnsSettings { IsEnabled = true, Listen = ["127.0.0.1"], Port = 1053 } };
        var host = new BootstrapTestHost { Servers = ["192.0.2.53", "2001:db8::53"] };
        await Check(setup, host).ConfigureAwait(false);
        Assert.Equal(host.Servers, host.CheckedDns);
        Assert.Equal(setup.Dns.Listen, host.ExcludedDns);
        Assert.Equal(setup.Dns.Port, host.ResolverPort);
    }

    [Fact]
    public async Task MissingSystemDnsIsRejected()
    {
        var setup = Setup() with { Dns = new DnsSettings { IsEnabled = true, Listen = ["127.0.0.1"], Port = 1053 } };
        Assert.Equal("dns.systemServers", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost { Servers = [] })).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task AConfiguredRemoteUpdateServiceMustPassItsCheck()
    {
        var setup = Setup() with { ClientUpdates = new ClientUpdateSettings { IsEnabled = true, Source = "address", Address = "https://updates.test", Token = "test-only-token" } };
        Assert.Equal("clientUpdates.address", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost { UpdatesReply = false })).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task APrivateCertificateNeedsExplicitTrustAndMatchingSan()
    {
        var (chain, key) = Certificate("panel.test");
        var setup = Setup();
        setup = setup with { Panel = setup.Panel! with { Domains = ["panel.test"], Certificate = chain, CertificateKey = key } };
        Assert.Equal("panel.certificate", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost())).ConfigureAwait(false)).Field);
        setup = setup with { Validation = new BootstrapValidation { TrustedCertificates = [chain] } };
        await Check(setup, new BootstrapTestHost()).ConfigureAwait(false);
        var mismatch = setup with { Panel = setup.Panel! with { Domains = ["different.test"] } };
        Assert.Equal("panel.certificate", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(mismatch, new BootstrapTestHost())).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task AnExpiredTrustedCertificateIsRejected()
    {
        var (chain, key) = Certificate("panel.test", expired: true);
        var setup = Setup();
        setup = setup with { Panel = setup.Panel! with { Certificate = chain, CertificateKey = key }, Validation = new BootstrapValidation { TrustedCertificates = [chain] } };
        Assert.Equal("panel.certificate", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost())).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task AListenerFailureRollsBackTheAdministratorAndSettings()
    {
        File.WriteAllText(Setup().PasswordFile, "Bootstrap-only-123456789!");
        var setup = Setup();
        var path = Path.Combine(_folder.FullName, "bootstrap.json");
        File.WriteAllText(path, BootstrapTestJson.Write(setup));
        using var context = Context.Open(Database);
        var code = await BootstrapCommand.RunAsync(context, new Arguments(["bootstrap", "--file", path]), CancellationToken.None, new BootstrapTestHost { ReserveError = true }).ConfigureAwait(false);
        Assert.Equal(78, code);
        Assert.Null(PanelStore.Held(Database));
        Assert.False(await context.Accounts.HasAdminAsync(CancellationToken.None).ConfigureAwait(false));
    }

    private (string Chain, string Key) Certificate(string name, bool expired = false)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(name);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 3));
        var chain = Path.Combine(_folder.FullName, Guid.NewGuid() + ".pem");
        var key = chain + ".key";
        File.WriteAllText(chain, cert.ExportCertificatePem());
        File.WriteAllText(key, rsa.ExportPkcs8PrivateKeyPem());

        return (chain, key);
    }

    [Fact]
    public async Task DryRunRollsBackAValidInitialConfiguration()
    {
        var setup = Setup();
        File.WriteAllText(setup.PasswordFile, "Bootstrap-only-123456789!");
        var path = Path.Combine(_folder.FullName, "bootstrap.json");
        File.WriteAllText(path, BootstrapTestJson.Write(setup));
        using var context = Context.Open(Database);
        Assert.Equal(0, await BootstrapCommand.RunAsync(context, new Arguments(["bootstrap", "--file", path, "--check"]), CancellationToken.None, new BootstrapTestHost()).ConfigureAwait(false));
        Assert.Null(PanelStore.Held(Database));
        Assert.False(await context.Accounts.HasAdminAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task MissingTlsForEnabledSubscriptionsIsRejected()
    {
        var setup = Setup() with { Subscriptions = new SubscriptionSettings { IsEnabled = true, Separate = true } };
        Assert.Equal("subscriptions.certificate", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost())).ConfigureAwait(false)).Field);
    }

    [Fact]
    public async Task InvalidRuntimeValuesFailBeforeOpeningTheDatabase()
    {
        var old = Environment.GetEnvironmentVariable("Proxy__Enabled");
        try
        {
            Environment.SetEnvironmentVariable("Proxy__Enabled", "not-a-boolean");
            Assert.Equal(78, await BootstrapCommand.RunAsync(new Arguments(["bootstrap", "--db", Database, "--file", "/missing"]), CancellationToken.None).ConfigureAwait(false));
            Assert.False(File.Exists(Database));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Proxy__Enabled", old);
        }
    }

    [Fact]
    public async Task AConfiguredPasswordMinimumIsApplied()
    {
        var old = Environment.GetEnvironmentVariable("Auth__MinimumPasswordLength");
        try
        {
            Environment.SetEnvironmentVariable("Auth__MinimumPasswordLength", "20");
            var setup = Setup();
            File.WriteAllText(setup.PasswordFile, "only-ten!!");
            var path = Path.Combine(_folder.FullName, "bootstrap.json");
            File.WriteAllText(path, BootstrapTestJson.Write(setup));
            using var context = Context.Open(Database);
            Assert.Equal(20, context.Options.MinimumPasswordLength);
            Assert.Equal(78, await BootstrapCommand.RunAsync(context, new Arguments(["bootstrap", "--file", path]), CancellationToken.None, new BootstrapTestHost()).ConfigureAwait(false));
            Assert.Null(PanelStore.Held(Database));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__MinimumPasswordLength", old);
        }
    }

    [Fact]
    public async Task AListenCertificateCannotUseAnotherPrivateKey()
    {
        var (chain, _) = Certificate("panel.test");
        var (_, wrong) = Certificate("panel.test");
        var setup = Setup();
        setup = setup with { Panel = setup.Panel! with { Certificate = chain, CertificateKey = wrong }, Validation = new BootstrapValidation { TrustedCertificates = [chain] } };
        Assert.Equal("panel.certificate", (await Assert.ThrowsAsync<BootstrapFault>(() => Check(setup, new BootstrapTestHost())).ConfigureAwait(false)).Field);
    }

    private BootstrapSettings TlsSetup()
    {
        var (chain, key) = Certificate("panel.test");
        var setup = Setup();

        return setup with { Panel = setup.Panel! with { Certificate = chain, CertificateKey = key }, Validation = new BootstrapValidation { TrustedCertificates = [chain] } };
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, _key);
        _folder.Delete(recursive: true);
    }
}

internal sealed class BootstrapTestHost : IBootstrapHost
{
    public IReadOnlyList<IPAddress> Addresses => [IPAddress.Loopback, IPAddress.IPv6Loopback];
    public IPAddress[] Answers { get; init; } = [IPAddress.Loopback];
    public bool ResolveError { get; init; }
    public bool ReserveError { get; init; }
    public bool DnsReply { get; init; } = true;
    public IReadOnlyList<string> Servers { get; init; } = ["192.0.2.53"];
    public IReadOnlyList<string> ExcludedDns { get; private set; } = [];
    public int ResolverPort { get; private set; }
    public List<string> CheckedDns { get; } = [];

    public bool UpdatesReply { get; init; } = true;
    public List<(IPAddress, int, ProtocolType)> Reservations { get; } = [];

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => ResolveError
        ? Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)) : Task.FromResult(Answers);

    public IDisposable Reserve(IPAddress address, int port, ProtocolType protocol)
    {
        if (ReserveError)
        {
            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        }

        Reservations.Add((address, port, protocol));

        return new MemoryStream();
    }

    public string? VpnFault() => null;
    public IReadOnlyList<string> NameServers(IEnumerable<string> excludedAddresses, int resolverPort)
    {
        ExcludedDns = excludedAddresses.ToArray();
        ResolverPort = resolverPort;
        return Servers;
    }

    public Task<bool> DnsAsync(string upstream, CancellationToken ct)
    {
        CheckedDns.Add(upstream);
        return Task.FromResult(DnsReply);
    }
    public Task<bool> UpdatesAsync(Uri address, string token, CancellationToken ct) => Task.FromResult(UpdatesReply);
}

internal static class BootstrapTestJson
{
    public static string Write(BootstrapSettings setup)
    {
        var resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(p => p.Set is null).ToArray())
            {
                info.Properties.Remove(property);
            }
        });

        return JsonSerializer.Serialize(setup, new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver, Converters = { new JsonStringEnumConverter() } });
    }
}
