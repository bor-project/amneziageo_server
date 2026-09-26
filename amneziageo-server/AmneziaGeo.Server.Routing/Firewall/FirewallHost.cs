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
    /// Tells whether the firewall of the host lets a port in from outside: open, closed or unknown.
    /// </summary>
    public async Task<string> StateAsync(string protocol, int port, CancellationToken ct)
    {
        if (_tool.Length > 0)
        {
            var status = await RunAsync(["status", "verbose"], ct).ConfigureAwait(false);

            return status.IsOk ? PortState.OfUfw(status.Output, protocol, port) : PortState.Unknown;
        }

        var input = await _commands.RunAsync(Nft, ["list", "chain", "ip", "filter", "INPUT"], null, ct).ConfigureAwait(false);
        var rules = await _commands.RunAsync(Nft, ["list", "chain", "ip", "filter", "ufw-user-input"], null, ct)
            .ConfigureAwait(false);

        return input.IsOk && rules.IsOk ? PortState.OfChains(input.Output, rules.Output, protocol, port) : PortState.Unknown;
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
