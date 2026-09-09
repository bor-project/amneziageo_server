namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Puts interfaces, addresses, routes and firewall rules on the host through the ip and nft tools.
/// </summary>
public sealed class IpHostNetwork : IHostNetwork
{
    private const string NetworkClass = "/sys/class/net";
    private const string Ip = "ip";
    private const string Nft = "nft";
    private const string LinkKind = "amneziawg";

    private static readonly char[] Blanks = [' ', '\t', '\n', '\r'];

    private static readonly string[] Forwarding =
    [
        "/proc/sys/net/ipv4/ip_forward",
        "/proc/sys/net/ipv6/conf/all/forwarding",
    ];

    private readonly IHostCommands _commands;

    /// <summary>
    /// ctor
    /// </summary>
    public IpHostNetwork(IHostCommands commands)
    {
        _commands = commands;
    }

    /// <summary>
    /// Tells whether the host carries an interface.
    /// </summary>
    public bool HasLink(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Directory.Exists(Path.Combine(NetworkClass, name));
    }

    /// <summary>
    /// Adds an AmneziaWG interface.
    /// </summary>
    public Task AddLinkAsync(string name, CancellationToken ct) =>
        RunAsync([Ip, "link", "add", name, "type", LinkKind], ct);

    /// <summary>
    /// Takes an interface off the host.
    /// </summary>
    public Task RemoveLinkAsync(string name, CancellationToken ct) =>
        RunAsync([Ip, "link", "del", name], ct);

    /// <summary>
    /// Replaces the address ranges of an interface.
    /// </summary>
    public async Task AddressAsync(string name, IReadOnlyList<string> addresses, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        await RunAsync([Ip, "address", "flush", "dev", name], ct).ConfigureAwait(false);
        foreach (var address in addresses)
        {
            await RunAsync([Ip, "address", "add", address, "dev", name], ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sets the packet size of an interface and brings it up.
    /// </summary>
    public async Task UpAsync(string name, int mtu, CancellationToken ct)
    {
        if (mtu > 0)
        {
            await RunAsync([Ip, "link", "set", "dev", name, "mtu", mtu.ToString()], ct).ConfigureAwait(false);
        }

        await RunAsync([Ip, "link", "set", "dev", name, "up"], ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts the way out through an interface into a routing table.
    /// </summary>
    public async Task RouteAsync(string name, int table, CancellationToken ct)
    {
        var number = table.ToString();
        await RunAsync([Ip, "route", "replace", "default", "dev", name, "table", number], ct).ConfigureAwait(false);
        await RunAsync([Ip, "-6", "route", "replace", "default", "dev", name, "table", number], ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Clears a routing table.
    /// </summary>
    public async Task ClearRouteAsync(int table, CancellationToken ct)
    {
        var number = table.ToString();
        await Quietly([Ip, "route", "flush", "table", number], ct).ConfigureAwait(false);
        await Quietly([Ip, "-6", "route", "flush", "table", number], ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds or removes the rule that sends a marked packet into a routing table.
    /// </summary>
    public async Task RuleAsync(uint mark, int table, int priority, bool present, CancellationToken ct)
    {
        if (await HasRuleAsync(priority, mark, ct).ConfigureAwait(false) == present)
        {
            return;
        }

        var number = table.ToString();
        var value = mark.ToString();
        var order = priority.ToString();
        var verb = present ? "add" : "del";
        var words = new[] { Ip, "rule", verb, "pref", order, "fwmark", value, "lookup", number };
        if (present)
        {
            await RunAsync(words, ct).ConfigureAwait(false);
        }
        else
        {
            await Quietly(words, ct).ConfigureAwait(false);
        }

        await Quietly([Ip, "-6", "rule", verb, "pref", order, "fwmark", value, "lookup", number], ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the interface the host reaches the internet through.
    /// </summary>
    public async Task<string> UplinkAsync(CancellationToken ct)
    {
        var result = await _commands.RunAsync(Ip, ["route", "show", "default"], null, ct).ConfigureAwait(false);
        var words = result.Output.Split(Blanks, StringSplitOptions.RemoveEmptyEntries);
        for (var at = 0; at < words.Length - 1; at++)
        {
            if (words[at] == "dev")
            {
                return words[at + 1];
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Lets the host pass packets between interfaces, in both families.
    /// </summary>
    public async Task ForwardingAsync(CancellationToken ct)
    {
        foreach (var knob in Forwarding)
        {
            await File.WriteAllTextAsync(knob, "1", ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Puts a firewall ruleset on the host, replacing the one it holds.
    /// </summary>
    public Task FirewallAsync(string ruleset, CancellationToken ct) => NftAsync(ruleset, false, ct);

    /// <summary>
    /// Reads a firewall ruleset without putting it on the host.
    /// </summary>
    public Task CheckFirewallAsync(string ruleset, CancellationToken ct) => NftAsync(ruleset, true, ct);

    private async Task NftAsync(string ruleset, bool dry, CancellationToken ct)
    {
        var arguments = dry ? (string[])["-c", "-f", "-"] : ["-f", "-"];
        var result = await _commands.RunAsync(Nft, arguments, ruleset, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new HostNetworkException($"the firewall refused the ruleset: {result.Complaint}");
        }
    }

    private async Task<bool> HasRuleAsync(int priority, uint mark, CancellationToken ct)
    {
        var result = await _commands
            .RunAsync(Ip, ["rule", "show", "pref", priority.ToString()], null, ct)
            .ConfigureAwait(false);

        return result.IsOk && result.Output.Contains($"fwmark 0x{mark:x}", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunAsync(IReadOnlyList<string> words, CancellationToken ct)
    {
        var result = await _commands.RunAsync(words[0], [.. words.Skip(1)], null, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new HostNetworkException(string.Join(" ", words) + " was refused: " + result.Complaint);
        }
    }

    private async Task Quietly(IReadOnlyList<string> words, CancellationToken ct)
    {
        await _commands.RunAsync(words[0], [.. words.Skip(1)], null, ct).ConfigureAwait(false);
    }
}
