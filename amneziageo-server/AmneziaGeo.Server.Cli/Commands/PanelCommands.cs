using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that read and change where the panel answers from.
/// </summary>
public static class PanelCommands
{
    /// <summary>
    /// Runs one panel command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "show" => await ShowAsync(context, ct).ConfigureAwait(false),
            "get" => await GetAsync(context, args, ct).ConfigureAwait(false),
            "set" => await SetAsync(context, args, ct).ConfigureAwait(false),
            "reset" => await ResetAsync(context, args, ct).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Prints the settings the panel answers under.
    /// </summary>
    public static async Task<int> ShowAsync(Context context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var held = await context.Panel.ReadAsync(ct).ConfigureAwait(false);
        Terminal.Say($"listen       {(held.Listen.Count == 0 ? "every address" : string.Join(", ", held.Listen))}");
        Terminal.Say($"names        {(held.Domains.Count == 0 ? "any" : string.Join(", ", held.Domains))}");
        Terminal.Say($"port         {held.Port.ToString(CultureInfo.InvariantCulture)}");
        Terminal.Say($"path         {held.Prefix}");
        Terminal.Say($"certificate  {(held.Certificate.Length == 0 ? PanelEdit.None : held.Certificate)}");
        if (held.CertificateKey.Length > 0)
        {
            Terminal.Say($"key          {held.CertificateKey}");
        }

        Terminal.Say($"firewall     {(held.Opened ? "the port is held open" : "the port is left as it is")}");
        Terminal.Say($"channel      {PanelEdit.Channel(held)}");
        Terminal.Say($"language     {held.Language}");

        return 0;
    }

    /// <summary>
    /// Prints the settings of the panel it is asked for, one line each, for a script to read.
    /// </summary>
    public static async Task<int> GetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        var held = await context.Panel.ReadAsync(ct).ConfigureAwait(false);
        var names = args.Positional.Skip(2).ToList();
        var values = names.Select(name => Setting(held, name)).ToList();
        if (names.Count == 0 || values.Any(value => value is null))
        {
            return Refuse("panel get port | path | listen | domains | certificate | key | opened | channel ...");
        }

        foreach (var value in values)
        {
            Terminal.Say(value!);
        }

        return 0;
    }

    /// <summary>
    /// Changes the settings the options name and keeps the rest.
    /// </summary>
    public static async Task<int> SetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        var held = await context.Panel.ReadAsync(ct).ConfigureAwait(false);
        var (draft, refusal) = Draft(held, args);
        if (draft is null)
        {
            return Refuse(refusal);
        }

        if (draft == held)
        {
            return Refuse("name what to change: --port, --path, --listen, --domains, --certificate, --opened or --channel");
        }

        return await SaveAsync(context, draft, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts the address, port, path and certificate of the panel back to their defaults.
    /// </summary>
    public static async Task<int> ResetAsync(Context context, Arguments args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (!args.Has("yes") && !Terminal.Confirm("put the address, port, path and certificate of the panel back to their defaults?"))
        {
            return 1;
        }

        var held = await context.Panel.ReadAsync(ct).ConfigureAwait(false);

        return await SaveAsync(context, PanelEdit.Reset(held), ct).ConfigureAwait(false);
    }

    // Returns one setting as a script reads it, null for a name the panel does not hold.
    private static string? Setting(PanelSettings held, string name) => name switch
    {
        "port" => held.Port.ToString(CultureInfo.InvariantCulture),
        "path" => held.Prefix,
        "listen" => string.Join(',', held.Listen),
        "domains" => string.Join(',', held.Domains),
        "certificate" => held.Certificate,
        "key" => held.CertificateKey,
        "opened" => held.Opened ? "on" : "off",
        "channel" => PanelEdit.Channel(held),
        _ => null,
    };

    // Returns the settings the options make of the held ones, or why they make none.
    private static (PanelSettings? Draft, string Refusal) Draft(PanelSettings held, Arguments args)
    {
        var draft = held;
        if (args.Value("port") is { } port)
        {
            if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return (null, $"'{port}' is not a port");
            }

            draft = draft with { Port = number };
        }

        if (args.Value("path") is { } path)
        {
            draft = draft with { Path = PanelEdit.PathOf(path) };
        }

        if (args.Value("listen") is { } listen)
        {
            draft = draft with { Listen = PanelEdit.ListOf(listen, PanelEdit.All) };
        }

        if (args.Value("domains") is { } domains)
        {
            draft = draft with { Domains = PanelEdit.ListOf(domains, PanelEdit.None) };
        }

        if (args.Value("certificate") is { } chain)
        {
            draft = string.Equals(chain, PanelEdit.None, StringComparison.OrdinalIgnoreCase)
                ? draft with { Certificate = string.Empty, CertificateKey = string.Empty }
                : draft with { Certificate = chain, CertificateKey = args.Value("key") ?? string.Empty };
        }

        if (args.Value("opened") is { } opened)
        {
            if (PanelEdit.SwitchOf(opened) is not { } open)
            {
                return (null, "--opened takes on or off");
            }

            draft = draft with { Opened = open };
        }

        if (args.Value("channel") is { } channel)
        {
            if (PanelEdit.ChannelOf(channel) is not { } prereleases)
            {
                return (null, $"--channel takes {PanelEdit.Stable} or {PanelEdit.Test}");
            }

            draft = draft with { Prereleases = prereleases };
        }

        return (draft, string.Empty);
    }

    // Saves the settings the rules of the panel allow and says where the panel answers once it starts over.
    private static async Task<int> SaveAsync(Context context, PanelSettings draft, CancellationToken ct)
    {
        if (await RefusalAsync(context, draft, ct).ConfigureAwait(false) is { } refusal)
        {
            return Refuse(refusal);
        }

        var result = await context.Panel.SaveAsync(draft, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Refuse(result.Message);
        }

        var saved = result.Record;
        var where = saved.Listen.Count == 0 ? "every address" : string.Join(", ", saved.Listen);
        Terminal.Say($"saved: the panel answers on {where}, port {saved.Port.ToString(CultureInfo.InvariantCulture)}, under {saved.Prefix} once it starts over");

        return 0;
    }

    // Returns why the panel would not answer under the settings, null when it would.
    private static async Task<string?> RefusalAsync(Context context, PanelSettings draft, CancellationToken ct)
    {
        if (PanelRules.Check(draft) is { } broken)
        {
            return broken.Message;
        }

        if (CertificateFiles.Check(draft.Certificate, draft.CertificateKey, NullLogger.Instance) is { } unread)
        {
            return unread.Message;
        }

        if (draft.Prefix.Length == 1 && await context.Configs.ServesAsync(draft.Port, ct).ConfigureAwait(false))
        {
            return $"the services of an endpoint answer on TCP port {draft.Port.ToString(CultureInfo.InvariantCulture)}, give the panel a path of its own to share the port";
        }

        var strange = draft.Listen.FirstOrDefault(address => !Carried(address));

        return strange is null ? null : $"the host carries no address {strange}";
    }

    // Tells whether an address belongs to the host.
    private static bool Carried(string address)
    {
        if (!IPAddress.TryParse(address, out var parsed))
        {
            return false;
        }

        return IPAddress.IsLoopback(parsed)
            || NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Any(item => item.Address.Equals(parsed));
    }

    private static int Refuse(string message)
    {
        Terminal.Fail(message);

        return 2;
    }

    private static int Usage() => Refuse("""
        usage:
          panel show                                   where the panel answers from
          panel get port | path | listen | domains | certificate | key | opened | channel ...
          panel set [--port <n>] [--path <path> | random | /] [--listen <addresses> | all]
                    [--domains <names> | none] [--certificate <chain> --key <key> | --certificate none]
                    [--opened on | off] [--channel stable | test]
          panel reset [--yes]                          the loopback, port 8443, a fresh path, no certificate
        """);
}
