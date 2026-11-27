using System.Text.Json;
using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Tests;

[Collection(PanelCommandTests.Utility)]
public sealed class BootstrapTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-bootstrap-");

    private readonly string? _endpoints = Environment.GetEnvironmentVariable("Endpoints__Directory");

    private readonly string? _key = Environment.GetEnvironmentVariable(Context.KeyVariable);

    /// <summary>
    /// ctor
    /// </summary>
    public BootstrapTests()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, Path.Combine(_folder.FullName, "signing.pem"));
        Environment.SetEnvironmentVariable("Endpoints__Directory", Path.Combine(_folder.FullName, "endpoints"));
    }

    private string Database => Path.Combine(_folder.FullName, "server.db");

    private BootstrapSettings Setup()
    {
        var password = Path.Combine(_folder.FullName, "password");
        File.WriteAllText(password, "Bootstrap-Only-123456789!\n");

        return new BootstrapSettings
        {
            User = "compose-admin",
            PasswordFile = password,
            ChangePassword = false,
            Panel = new PanelSettings { Listen = ["127.0.0.1"], Port = 19443, Path = "compose", Language = "ru" },
        };
    }

    private async Task<int> Run(BootstrapSettings setup)
    {
        var file = Path.Combine(_folder.FullName, "bootstrap.json");
        await File.WriteAllTextAsync(file, BootstrapTestJson.Write(setup)).ConfigureAwait(false);

        return await RunFile(file).ConfigureAwait(false);
    }

    private async Task<int> RunFile(string file)
    {
        using var context = Context.Open(Database);

        return await BootstrapCommand.RunAsync(context, new Arguments(["bootstrap", "--file", file]), CancellationToken.None, new BootstrapTestHost()).ConfigureAwait(false);
    }

    [Fact]
    public async Task FreshInstallationCreatesAnAdministratorAndPanelSettings()
    {
        Assert.Equal(0, await Run(Setup()).ConfigureAwait(false));
        using var context = Context.Open(Database);
        var login = await context.Login.PasswordAsync("compose-admin", "Bootstrap-Only-123456789!", "127.0.0.1", "test", CancellationToken.None).ConfigureAwait(false);
        Assert.True(login.IsOk);
        Assert.False(login.MustChangePassword);
        Assert.Equal(19443, PanelStore.Held(Database)!.Port);
        Assert.Equal("/compose/", PanelStore.Held(Database)!.Prefix);
    }

    [Fact]
    public async Task RepeatedStartupDoesNotReadSecretsOrResetChangedSettings()
    {
        var setup = Setup();
        Assert.Equal(0, await Run(setup).ConfigureAwait(false));
        using (var context = Context.Open(Database))
        {
            var panel = await context.Panel.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            await context.Panel.SaveAsync(panel with { Port = 20443 }, CancellationToken.None).ConfigureAwait(false);
        }

        File.Delete(setup.PasswordFile);
        Assert.Equal(0, await RunFile("/missing-bootstrap.json").ConfigureAwait(false));
        Assert.Equal(20443, PanelStore.Held(Database)!.Port);
    }

    [Fact]
    public async Task InvalidInterfaceRollsBackPanelAndAdministrator()
    {
        var setup = Setup() with { Endpoints = [new BootstrapEndpoint { Settings = new ServerConfig { Name = "bad", ListenPort = -1 } }] };
        Assert.Equal(78, await Run(setup).ConfigureAwait(false));
        Assert.Null(PanelStore.Held(Database));
        using var context = Context.Open(Database);
        Assert.False(await context.Accounts.HasAdminAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Empty(await context.Configs.ListAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(0, await Run(Setup()).ConfigureAwait(false));
    }

    [Fact]
    public async Task InterfacesAndClientsReceiveKeysAndPersistAcrossStartup()
    {
        var endpoint = new BootstrapEndpoint
        {
            Settings = new ServerConfig { Name = "awg0", Host = "vpn.example.test", ListenPort = 51820, Address = ["10.84.3.1/24"], Dns = ["1.1.1.1"], AllowedIps = ["0.0.0.0/0"] },
            Clients = [new TunnelClient { Name = "phone" }],
        };
        var setup = Setup() with { Endpoints = [endpoint], RoutingDefaults = true };
        Assert.Equal(0, await Run(setup).ConfigureAwait(false));
        using var context = Context.Open(Database);
        var config = Assert.Single(await context.Configs.ListAsync(CancellationToken.None).ConfigureAwait(false));
        var client = Assert.Single(await context.Clients.ListAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(config.Id, client.ConfigId);
        Assert.NotEmpty(config.PrivateKey);
        Assert.NotEmpty(config.Obfuscation.HeaderProtectionKey);
        Assert.NotEmpty(client.PrivateKey);
        Assert.NotEmpty(client.SubscriptionId);
        Assert.Equal(0, await Run(setup).ConfigureAwait(false));
        Assert.Equal(config.PrivateKey, Assert.Single(await context.Configs.ListAsync(CancellationToken.None).ConfigureAwait(false)).PrivateKey);
    }

    [Fact]
    public async Task PlainWireGuardKeepsPlainHeaders()
    {
        var setup = Setup() with { Endpoints = [new BootstrapEndpoint { Amnezia = false, Settings = ConfigDefaults.Fresh("wg0") with { Host = "vpn.example.test" } }] };
        Assert.Equal(0, await Run(setup).ConfigureAwait(false));
        using var context = Context.Open(Database);
        Assert.True(ConfigRules.IsPlain(Assert.Single(await context.Configs.ListAsync(CancellationToken.None).ConfigureAwait(false)).Obfuscation));
    }

    [Fact]
    public async Task InitialDnsSubscriptionsAndClientReleasesUseTheProductStores()
    {
        var setup = Setup() with
        {
            Dns = new DnsSettings { IsEnabled = true, Port = 1053, Listen = ["127.0.0.1"] },
            Subscriptions = new SubscriptionSettings { IsEnabled = false, Title = "Compose subscriptions" },
            ClientUpdates = new ClientUpdateSettings { IsEnabled = false, Path = "updates" },
        };
        Assert.Equal(0, await Run(setup).ConfigureAwait(false));
        using var context = Context.Open(Database);
        Assert.Equal(1053, (await context.Dns.ReadAsync(CancellationToken.None).ConfigureAwait(false)).Port);
        Assert.Equal("Compose subscriptions", (await context.Subscriptions.ReadAsync(CancellationToken.None).ConfigureAwait(false)).Title);
    }

    [Fact]
    public async Task MissingDnsRoutingIsNotSilentlyReplacedByBundledDefaults()
    {
        var setup = Setup() with { Dns = new DnsSettings { IsEnabled = true, Port = 1053, Listen = ["127.0.0.1"] }, RoutingDefaults = true };
        using (var context = Context.Open(Database))
        {
            Assert.Single(await context.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false));
        }
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, ForeignKeys = true }.ToString()))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM RoutingEntries";
            Assert.Equal(1, await command.ExecuteNonQueryAsync().ConfigureAwait(false));
        }
        Assert.Equal(78, await Run(setup).ConfigureAwait(false));
        Assert.Null(PanelStore.Held(Database));
        using var reopened = Context.Open(Database);
        Assert.False(await reopened.Accounts.HasAdminAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Empty(await reopened.Rules.TableAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task DefaultInitialPasswordRequiresAChange()
    {
        Assert.Equal(0, await Run(Setup() with { ChangePassword = true }).ConfigureAwait(false));
        using var context = Context.Open(Database);
        var login = await context.Login.PasswordAsync("compose-admin", "Bootstrap-Only-123456789!", "127.0.0.1", "test", CancellationToken.None).ConfigureAwait(false);
        Assert.True(login.MustChangePassword);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":null}")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/missing\",\"typo\":true}")]
    [InlineData("{\"user\":\"admin\",\"passwordFile\":\"/missing\",\"endpoints\":[null]}")]
    public async Task MalformedInputDoesNotCreateAnAdministrator(string json)
    {
        var file = Path.Combine(_folder.FullName, "invalid.json");
        File.WriteAllText(file, json);
        Assert.Equal(78, await RunFile(file).ConfigureAwait(false));
        using var context = Context.Open(Database);
        Assert.False(await context.Accounts.HasAdminAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task WeakPasswordDoesNotChangePanelSettings()
    {
        var setup = Setup();
        File.WriteAllText(setup.PasswordFile, "short");
        Assert.Equal(78, await Run(setup).ConfigureAwait(false));
        Assert.Null(PanelStore.Held(Database));
    }

    [Fact]
    public async Task ExistingPanelWithoutAnAdministratorRequiresExplicitRecovery()
    {
        using (var context = Context.Open(Database))
        {
            await context.Panel.SaveAsync(new PanelSettings { Port = 28443 }, CancellationToken.None).ConfigureAwait(false);
        }

        Assert.Equal(78, await Run(Setup()).ConfigureAwait(false));
        Assert.Equal(28443, PanelStore.Held(Database)!.Port);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Context.KeyVariable, _key);
        Environment.SetEnvironmentVariable("Endpoints__Directory", _endpoints);
        _folder.Delete(recursive: true);
    }
}
