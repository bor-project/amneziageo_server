using System.ComponentModel;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Routing.Firewall;

/// <summary>
/// What holding the ports open left.
/// </summary>
/// <param name="Engine">What the panel held the ports open with.</param>
/// <param name="IsDone">Whether the host took the plan.</param>
/// <param name="Message">What the host answered when it refused.</param>
public sealed record FirewallSync(string Engine, bool IsDone, string Message)
{
    /// <summary>
    /// Returns the answer of a host that took the plan.
    /// </summary>
    public static FirewallSync Done(string engine) => new(engine, true, string.Empty);
}

/// <summary>
/// Whether the panel may change the firewall of the host, and what keeps it from doing so.
/// </summary>
/// <param name="Engine">What the panel holds the ports open with.</param>
/// <param name="IsAble">Whether the panel may change the firewall.</param>
/// <param name="Reason">What keeps the panel from it: no-rights or host-ufw, empty when nothing does.</param>
/// <param name="Message">What the host answered when it refused.</param>
public sealed record FirewallReach(string Engine, bool IsAble, string Reason, string Message)
{
    /// <summary>
    /// The host does not let the panel change its firewall.
    /// </summary>
    public const string NoRights = "no-rights";

    /// <summary>
    /// The host runs a ufw the panel does not reach, and what that ufw drops the table of the panel does not let in.
    /// </summary>
    public const string HostUfw = "host-ufw";

    /// <summary>
    /// Returns the answer of a host whose firewall the panel may change.
    /// </summary>
    public static FirewallReach Able(string engine) => new(engine, true, string.Empty, string.Empty);
}

/// <summary>
/// Holds the ports of the panel open in the firewall of the host.
/// </summary>
public sealed class FirewallHost
{
    /// <summary>
    /// What the ports are opened with where the host has ufw.
    /// </summary>
    public const string Ufw = "ufw";

    /// <summary>
    /// What the ports are opened with where the host has no ufw.
    /// </summary>
    public const string Table = "nft";

    private const string Nft = "nft";

    private const string UserInput = "ufw-user-input";

    private const string Unreached =
        "the host runs ufw, which the panel does not reach from here, and what ufw drops the table of the panel does not let in";

    private static readonly string[] Places = ["/usr/sbin/ufw", "/sbin/ufw", "/usr/bin/ufw", "/bin/ufw"];

    private readonly IHostCommands _commands;

    private readonly IHostNetwork _network;

    private readonly string _tool;

    /// <summary>
    /// ctor
    /// </summary>
    public FirewallHost(IHostCommands commands, IHostNetwork network, string? tool = null)
    {
        _commands = commands;
        _network = network;
        _tool = tool ?? Array.Find(Places, File.Exists) ?? string.Empty;
    }

    /// <summary>
    /// Tells what the panel holds the ports open with.
    /// </summary>
    public string Engine => _tool.Length > 0 ? Ufw : Table;

    /// <summary>
    /// Opens what the plan names and closes what the panel opened before it.
    /// </summary>
    public async Task<FirewallSync> ApplyAsync(FirewallPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        try
        {
            return _tool.Length > 0
                ? await UfwAsync(plan, ct).ConfigureAwait(false)
                : await TableAsync(plan, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
            when (ex is HostNetworkException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return new FirewallSync(Engine, false, ex.Message);
        }
    }

    /// <summary>
    /// Hands ports the panel holds open no longer over to the host, right before the plan that drops them is applied:
    /// ufw takes the rule of the panel for each one again under a comment without its mark, so the port stays open as
    /// a rule of the host that the panel never takes out. The table of the open ports keeps nothing.
    /// </summary>
    public async Task<FirewallSync> KeepAsync(IReadOnlyList<FirewallPort> ports, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ports);

        if (_tool.Length == 0 || ports.Count == 0)
        {
            return FirewallSync.Done(Engine);
        }

        try
        {
            var shown = await RunAsync(["show", "added"], ct).ConfigureAwait(false);
            if (!shown.IsOk)
            {
                return new FirewallSync(Ufw, false, shown.Complaint);
            }

            foreach (var rule in UfwRules.Kept(ports, UfwRules.Read(shown.Output)))
            {
                var kept = await RunAsync(UfwRules.Add(rule), ct).ConfigureAwait(false);
                if (!kept.IsOk)
                {
                    return new FirewallSync(Ufw, false, kept.Complaint);
                }
            }

            return FirewallSync.Done(Ufw);
        }
        catch (Exception ex)
            when (ex is HostNetworkException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return new FirewallSync(Ufw, false, ex.Message);
        }
    }

    /// <summary>
    /// Tells whether the panel may change the firewall of the host: ufw shows the rules it was given only to a caller
    /// that may change them; where the host has no ufw of its own here, the chains of a ufw outside the reach of the
    /// panel are looked for, and the table of the open ports has to pass a dry run.
    /// </summary>
    public async Task<FirewallReach> ReachAsync(CancellationToken ct)
    {
        try
        {
            if (_tool.Length > 0)
            {
                var shown = await RunAsync(["show", "added"], ct).ConfigureAwait(false);

                return shown.IsOk
                    ? FirewallReach.Able(Ufw)
                    : new FirewallReach(Ufw, false, FirewallReach.NoRights, shown.Complaint);
            }

            var chain = await _commands.RunAsync(Nft, ["list", "chain", "ip", "filter", UserInput], null, ct)
                .ConfigureAwait(false);
            if (chain.IsOk)
            {
                return new FirewallReach(Table, false, FirewallReach.HostUfw, Unreached);
            }

            await _network.CheckFirewallAsync(OpenRuleset.Text(FirewallPlan.None), ct).ConfigureAwait(false);

            return FirewallReach.Able(Table);
        }
        catch (Exception ex)
            when (ex is HostNetworkException or IOException or UnauthorizedAccessException
                or InvalidOperationException or Win32Exception)
        {
            return new FirewallReach(Engine, false, FirewallReach.NoRights, ex.Message);
        }
    }

    /// <summary>
    /// Tells whether the firewall of the host lets a port in from outside: open, closed or unknown.
    /// </summary>
    public async Task<string> StateAsync(string protocol, int port, CancellationToken ct) =>
        (await StatesAsync([new FirewallPort(protocol, port, string.Empty)], ct).ConfigureAwait(false))[0];

    /// <summary>
    /// Tells for each port whether the firewall of the host lets it in from outside, reading the firewall once.
    /// </summary>
    public async Task<IReadOnlyList<string>> StatesAsync(IReadOnlyList<FirewallPort> ports, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ports);

        if (ports.Count == 0)
        {
            return [];
        }

        if (_tool.Length > 0)
        {
            var status = await RunAsync(["status", "verbose"], ct).ConfigureAwait(false);

            return [.. ports.Select(one => status.IsOk ? PortState.OfUfw(status.Output, one.Protocol, one.Port) : PortState.Unknown)];
        }

        var input = await _commands.RunAsync(Nft, ["list", "chain", "ip", "filter", "INPUT"], null, ct).ConfigureAwait(false);
        var rules = await _commands.RunAsync(Nft, ["list", "chain", "ip", "filter", UserInput], null, ct)
            .ConfigureAwait(false);

        return
        [
            .. ports.Select(one => input.IsOk && rules.IsOk
                ? PortState.OfChains(input.Output, rules.Output, one.Protocol, one.Port)
                : PortState.Unknown),
        ];
    }

    private async Task<FirewallSync> UfwAsync(FirewallPlan plan, CancellationToken ct)
    {
        var shown = await RunAsync(["show", "added"], ct).ConfigureAwait(false);
        if (!shown.IsOk)
        {
            return new FirewallSync(Ufw, false, shown.Complaint);
        }

        var (put, take) = UfwRules.Difference(
            UfwRules.Wanted(plan),
            UfwRules.Read(shown.Output),
            UfwRules.Others(shown.Output));
        foreach (var rule in put)
        {
            var added = await RunAsync(UfwRules.Add(rule), ct).ConfigureAwait(false);
            if (!added.IsOk)
            {
                return new FirewallSync(Ufw, false, added.Complaint);
            }
        }

        foreach (var rule in take)
        {
            var dropped = await RunAsync(UfwRules.Drop(rule), ct).ConfigureAwait(false);
            if (!dropped.IsOk)
            {
                return new FirewallSync(Ufw, false, dropped.Complaint);
            }
        }

        return FirewallSync.Done(Ufw);
    }

    private async Task<FirewallSync> TableAsync(FirewallPlan plan, CancellationToken ct)
    {
        await _network.FirewallAsync(OpenRuleset.Text(plan), ct).ConfigureAwait(false);

        return FirewallSync.Done(Table);
    }

    private async Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct) =>
        await _commands.RunAsync(_tool, arguments, null, ct).ConfigureAwait(false);
}
