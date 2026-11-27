using System.Data;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// Initializes a fresh container from a private configuration file.
/// </summary>
public static class BootstrapCommand
{
    /// <summary>
    /// Opens the initial installation context.
    /// </summary>
    public static async Task<int> RunAsync(Arguments args, CancellationToken ct)
    {
        using var logs = LoggerFactory.Create(builder => builder.AddProvider(new BootstrapLogging(args.Has("verbose"))).SetMinimumLevel(LogLevel.Debug));
        var logger = logs.CreateLogger("bootstrap");
        try
        {
            CheckEnvironment();
            using var context = Context.Open(args.Value("db") ?? Environment.GetEnvironmentVariable("Database__Path"));

            return await RunAsync(context, args, ct, logger: logger).ConfigureAwait(false);
        }
        catch (BootstrapFault ex)
        {
            logger.LogDebug(ex, "installation check failed at {Field}", ex.Field);
            Terminal.Fail("bootstrap: " + ex.Field + ": " + ex.Reason);

            return 78;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or CryptographicException)
        {
            logger.LogError(ex, "installation storage could not be opened");
            Terminal.Fail("bootstrap: storage: database/signing-key paths cannot be opened; check paths and permissions");

            return 78;
        }
    }

    /// <summary>
    /// Applies the initial settings once.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct, IBootstrapHost? host = null, ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        try
        {
            var transaction = await context.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
            await using var lifetime = transaction.ConfigureAwait(false);
            if (await context.Accounts.HasAdminAsync(ct).ConfigureAwait(false))
            {
                if (args.Has("check"))
                {
                    throw new BootstrapFault("database", "initial setup already exists; use the panel for changes");
                }

                Terminal.Say("bootstrap: existing administrator and settings retained");

                return 0;
            }

            if (await context.Database.Users.AnyAsync(ct).ConfigureAwait(false)
                || await context.Database.Configs.AnyAsync(ct).ConfigureAwait(false)
                || await context.Database.Panel.AnyAsync(ct).ConfigureAwait(false))
            {
                throw new BootstrapFault("database", "not a fresh installation; use the administrative utility");
            }

            var file = args.Value("file") ?? throw new BootstrapFault("file", "name the bootstrap JSON file");
            var setup = BootstrapInput.Read(await ReadAsync(file, "file", ct).ConfigureAwait(false));
            var password = (await ReadAsync(setup.PasswordFile, "passwordFile", ct).ConfigureAwait(false)).TrimEnd('\r', '\n');
            if (password.Contains('\r') || password.Contains('\n') || AccountRules.CheckPassword(password, context.Options.MinimumPasswordLength) is not null)
            {
                throw new BootstrapFault("passwordFile", "one nonempty line of at least " + context.Options.MinimumPasswordLength + " characters is required");
            }

            var panel = setup.Panel is { } given
                ? given with { Path = given.Path.Length == 0 ? PanelDefaults.FreshPath() : PanelEdit.PathOf(given.Path) }
                : await context.PanelSettingsAsync(ct).ConfigureAwait(false);
            Names(panel.Domains, "panel.domains");
            if (PanelRules.Check(panel) is { } panelFault)
            {
                throw Fault("panel", panelFault.Code, panelFault.Message);
            }

            var saved = await context.Panel.SaveAsync(panel, ct).ConfigureAwait(false);
            if (!saved.IsOk)
            {
                throw Fault("panel", saved.Code, saved.Message);
            }

            if (setup.Subscriptions is { } subscriptions)
            {
                Names(subscriptions.Domains, "subscriptions.domains");
                if (SubscriptionRules.Check(subscriptions, panel) is { } fault)
                {
                    throw Fault("subscriptions", fault.Code, fault.Message);
                }

                await context.Subscriptions.SaveAsync(subscriptions, ct).ConfigureAwait(false);
            }

            if (setup.ClientUpdates is { } updates)
            {
                if (ClientUpdateRules.Check(updates, panel) is { } fault)
                {
                    throw Fault("clientUpdates", fault.Code, fault.Message);
                }

                await new ClientUpdateStore(context.Database).SaveAsync(updates, ct).ConfigureAwait(false);
            }

            if (setup.Dns is { } dns)
            {
                var result = await context.Dns.SaveAsync(dns, ct).ConfigureAwait(false);
                if (!result.IsOk)
                {
                    throw Fault("dns", result.Code, result.Message);
                }
            }

            var prepared = new List<BootstrapEndpoint>();
            for (var index = 0; index < setup.Endpoints.Count; index++)
            {
                var endpoint = setup.Endpoints[index];
                var at = "endpoints[" + index + "].settings";
                if (ConfigRules.CheckReach(endpoint.Settings) is { } reach)
                {
                    throw new BootstrapFault(at + ".host", reach.Message);
                }

                var result = await context.Configs.AddAsync(Endpoint(endpoint, at), ct).ConfigureAwait(false);
                if (!result.IsOk)
                {
                    throw Fault(at, result.Code, result.Message);
                }

                prepared.Add(endpoint with { Settings = result.Record! });
                for (var one = 0; one < endpoint.Clients.Count; one++)
                {
                    var client = endpoint.Clients[one];
                    var clientAt = "endpoints[" + index + "].clients[" + one + "]";
                    if (client.PrivateKey.Length > 0 && Curve25519.IsKey(client.PrivateKey) && client.PublicKey.Length > 0 && Curve25519.PublicOf(client.PrivateKey) != client.PublicKey)
                    {
                        throw new BootstrapFault(clientAt + ".publicKey", "public key does not match the private key");
                    }

                    var keyed = ClientDefaults.Keyed(client with
                    {
                        Id = 0,
                        ConfigId = result.Record!.Id,
                        SubscriptionId = client.SubscriptionId.Length > 0 ? client.SubscriptionId : ClientDefaults.SubscriptionId(),
                    });
                    var added = await context.Clients.AddAsync(keyed, ct).ConfigureAwait(false);
                    if (!added.IsOk)
                    {
                        throw Fault(clientAt, added.Code, "invalid client settings (" + added.Code + ")");
                    }
                }
            }

            if (setup.Dns is { IsEnabled: true })
            {
                await DnsRoutingAsync(context, ct).ConfigureAwait(false);
            }

            using var checks = new BootstrapChecks(host);
            await checks.CheckAsync(setup with { Endpoints = prepared }, panel, context.Path, ct).ConfigureAwait(false);
            if (setup.RoutingDefaults)
            {
                await context.RoutingDefaults.RestoreAllAsync(ct).ConfigureAwait(false);
            }

            var account = await context.Accounts.AddAsync(setup.User, setup.User, Roles.Admin, password, setup.ChangePassword, ct).ConfigureAwait(false);
            if (!account.IsOk)
            {
                throw new BootstrapFault("user", "administrator could not be created (" + account.Outcome + ")");
            }

            if (args.Has("check"))
            {
                Terminal.Say("bootstrap: validation passed; initial settings were not committed");

                return 0;
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            Terminal.Say("bootstrap: administrator and initial settings created");

            return 0;
        }
        catch (BootstrapFault ex)
        {
            log.LogDebug(ex, "installation check failed at {Field}", ex.Field);
            Terminal.Fail("bootstrap: " + ex.Field + ": " + ex.Reason);

            return 78;
        }
        catch (JsonException ex)
        {
            log.LogDebug(ex, "installation JSON did not pass validation");
            Terminal.Fail("bootstrap: " + (ex.Path ?? "$") + ": invalid value/type or malformed JSON at line " + (ex.LineNumber + 1));

            return 78;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException or SocketException or OperationCanceledException or HttpRequestException)
        {
            log.LogDebug(ex, "installation resource check failed");
            Terminal.Fail("bootstrap: resources: a file, listener, DNS or remote service check failed; check access and availability");

            return 78;
        }
    }

    private static async Task DnsRoutingAsync(Context context, CancellationToken ct)
    {
        var rules = await context.Rules.TableAsync(ct).ConfigureAwait(false);
        var entry = rules.Count == 1 ? rules[0] : null;
        var criterion = entry is { Criteria.Count: 1 } ? entry.Criteria[0] : null;
        if (entry is not { IsEnabled: true, Action: RouteAction.Direct, Outbound.Length: 0 }
            || entry.Clients.Count != 0 || entry.Inbounds.Count != 0 || entry.Ports.Count != 0 || entry.SourcePorts.Count != 0
            || entry.Protocol != RouteProtocol.Any
            || criterion is not { DefaultKey: RouteDefaults.FallbackKey or "resolver-default", DnsMode: TrafficDnsMode.Default }
            || criterion.Targets.Count != 0 || criterion.Dns.Count != 0 || criterion.DnsMembers.Count != 0)
        {
            throw new BootstrapFault("dns.routing", "initial DNS requires the fresh system-default direct routing entry; restore the initial routing policy or configure DNS after installation");
        }
    }

    private static ServerConfig Endpoint(BootstrapEndpoint endpoint, string field)
    {
        var given = endpoint.Settings;
        if (given.PublicKey.Length > 0 && (given.PrivateKey.Length == 0 || (Curve25519.IsKey(given.PrivateKey) && Curve25519.PublicOf(given.PrivateKey) != given.PublicKey)))
        {
            throw new BootstrapFault(field + ".privateKey", "an explicit public key requires its matching private key");
        }

        var pair = given.PrivateKey.Length == 0 ? Curve25519.Create() : new KeyPair(given.PrivateKey, given.PublicKey);
        var shape = !endpoint.Amnezia ? ConfigDefaults.Plain()
            : ConfigRules.IsPlain(given.Obfuscation) ? ConfigDefaults.Obfuscation() : given.Obfuscation;

        return given with { Id = 0, PrivateKey = pair.PrivateKey,
            PublicKey = Curve25519.IsKey(pair.PrivateKey) ? Curve25519.PublicOf(pair.PrivateKey) : pair.PublicKey, Obfuscation = shape };
    }

    private static void Names(IReadOnlyList<string> names, string field)
    {
        for (var index = 0; index < names.Count; index++)
        {
            if (!BootstrapChecks.HostName(names[index]))
            {
                throw new BootstrapFault(field + "[" + index + "]", "expected a nonempty DNS name or address");
            }
        }
    }

    private static async Task<string> ReadAsync(string path, string field, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BootstrapFault(field, "file is missing or unreadable", ex);
        }
    }

    private static BootstrapFault Fault(string section, string code, string reason)
    {
        var field = code switch
        {
            "bad-port" => "port", "bad-domain" => "domains", "bad-listen" => "listen", "bad-path" => "path",
            "bad-certificate" => "certificate", "bad-host" => "host", "bad-mtu" => "mtu", "bad-key" => "privateKey",
            "bad-updates-path" => "path", "bad-services-port" => "servicesPort", _ => code,
        };

        return new BootstrapFault(section + "." + field, reason);
    }

    private static void CheckEnvironment()
    {
        if (Environment.GetEnvironmentVariable("Auth__HostLogin") is { } login
            && (!Enum.TryParse<HostLogin>(login, true, out var mode) || !Enum.IsDefined(mode)))
        {
            throw new BootstrapFault("runtime.Auth__HostLogin", "expected Strict, Register or Off");
        }

        foreach (var name in new[] { "AMNEZIAGEO_DB", "AMNEZIAGEO_SIGNING_KEY", "Database__Path", "Endpoints__Directory", "Proxy__Directory", "Geo__Path" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } path && !Path.IsPathFullyQualified(path))
            {
                throw new BootstrapFault("runtime." + name, "an absolute nonempty path is required");
            }
        }

        foreach (var name in new[] { "Proxy__Enabled", "Proxy__Isolated", "Web__Inline", "Auth__HostUsers" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value && !bool.TryParse(value, out _))
            {
                throw new BootstrapFault("runtime." + name, "expected true or false");
            }
        }

        foreach (var name in new[] { "AMNEZIAGEO_MIN_PASSWORD", "Auth__MinimumPasswordLength", "Web__Requests" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value && (!int.TryParse(value, out var number) || number < (name == "Web__Requests" ? 0 : AccountRules.MinPasswordLength)))
            {
                throw new BootstrapFault("runtime." + name, "expected a nonnegative number within the supported range");
            }
        }
    }
}

/// <summary>
/// Holds the settings of a fresh container.
/// </summary>
public sealed record BootstrapSettings
{
    public required string User { get; init; }
    public required string PasswordFile { get; init; }
    public bool ChangePassword { get; init; } = true;
    public PanelSettings? Panel { get; init; }
    public SubscriptionSettings? Subscriptions { get; init; }
    public ClientUpdateSettings? ClientUpdates { get; init; }
    public DnsSettings? Dns { get; init; }
    public bool RoutingDefaults { get; init; }
    public IReadOnlyList<BootstrapEndpoint> Endpoints { get; init; } = [];
    public BootstrapValidation Validation { get; init; } = new();
}

/// <summary>
/// Holds an initial interface and its clients.
/// </summary>
public sealed record BootstrapEndpoint
{
    public required ServerConfig Settings { get; init; }
    public bool Amnezia { get; init; } = true;
    public IReadOnlyList<TunnelClient> Clients { get; init; } = [];
}
