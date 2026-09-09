using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// A host that writes down what it was asked to do instead of doing it.
/// </summary>
public sealed class Ledger : IHostNetwork
{
    /// <summary>
    /// What the host was asked to do, in order.
    /// </summary>
    public List<string> Steps { get; } = [];

    /// <summary>
    /// The interfaces the host carries.
    /// </summary>
    public HashSet<string> Links { get; } = [];

    /// <summary>
    /// The interface the host reaches the internet through.
    /// </summary>
    public string Uplink { get; set; } = "eth0";

    /// <summary>
    /// The ruleset the host last took.
    /// </summary>
    public string Ruleset { get; private set; } = string.Empty;

    /// <summary>
    /// What the host refuses, by the first word of the step.
    /// </summary>
    public string Refuses { get; set; } = string.Empty;

    /// <summary>
    /// Tells whether the host carries an interface.
    /// </summary>
    public bool HasLink(string name) => Links.Contains(name);

    /// <summary>
    /// Adds an AmneziaWG interface.
    /// </summary>
    public Task AddLinkAsync(string name, CancellationToken ct)
    {
        Links.Add(name);

        return Step($"add {name}");
    }

    /// <summary>
    /// Takes an interface off the host.
    /// </summary>
    public Task RemoveLinkAsync(string name, CancellationToken ct)
    {
        Links.Remove(name);

        return Step($"remove {name}");
    }

    /// <summary>
    /// Replaces the address ranges of an interface.
    /// </summary>
    public Task AddressAsync(string name, IReadOnlyList<string> addresses, CancellationToken ct) =>
        Step($"address {name} {string.Join(" ", addresses)}");

    /// <summary>
    /// Sets the packet size of an interface and brings it up.
    /// </summary>
    public Task UpAsync(string name, int mtu, CancellationToken ct) => Step($"up {name} {mtu}");

    /// <summary>
    /// Puts the way out through an interface into a routing table.
    /// </summary>
    public Task RouteAsync(string name, int table, CancellationToken ct) => Step($"route {name} {table}");

    /// <summary>
    /// Lets the host pass packets between interfaces, in both families.
    /// </summary>
    public Task ForwardingAsync(CancellationToken ct) => Step("forwarding");

    /// <summary>
    /// Clears a routing table.
    /// </summary>
    public Task ClearRouteAsync(int table, CancellationToken ct) => Step($"clear {table}");

    /// <summary>
    /// Adds or removes the rule that sends a marked packet into a routing table.
    /// </summary>
    public Task RuleAsync(uint mark, int table, int priority, bool present, CancellationToken ct) =>
        Step($"rule {mark} {table} {priority} {present}");

    /// <summary>
    /// Returns the interface the host reaches the internet through.
    /// </summary>
    public Task<string> UplinkAsync(CancellationToken ct) => Task.FromResult(Uplink);

    /// <summary>
    /// Puts a firewall ruleset on the host, replacing the one it holds.
    /// </summary>
    public Task FirewallAsync(string ruleset, CancellationToken ct)
    {
        Ruleset = ruleset;

        return Step("firewall");
    }

    /// <summary>
    /// Reads a firewall ruleset without putting it on the host.
    /// </summary>
    public Task CheckFirewallAsync(string ruleset, CancellationToken ct) => Step("check");

    /// <summary>
    /// Tells whether the host was asked to do a step.
    /// </summary>
    public bool Did(string step) => Steps.Any(one => one.StartsWith(step, StringComparison.Ordinal));

    private Task Step(string step)
    {
        Steps.Add(step);

        return Refuses.Length > 0 && step.StartsWith(Refuses, StringComparison.Ordinal)
            ? throw new HostNetworkException($"the host refused '{step}'")
            : Task.CompletedTask;
    }
}

/// <summary>
/// Interfaces held in memory instead of by the kernel.
/// </summary>
public sealed class Kernel : IAwgDevices
{
    private readonly Dictionary<string, AwgDevice> _devices = [];

    /// <summary>
    /// The changes the kernel was given, in order.
    /// </summary>
    public List<AwgUpdate> Updates { get; } = [];

    /// <summary>
    /// Puts an interface in place as though the kernel carried it.
    /// </summary>
    public void Hold(AwgDevice device) => _devices[device.Name] = device;

    /// <summary>
    /// Returns the names of the interfaces the host carries.
    /// </summary>
    public IReadOnlyList<string> Names() => [.. _devices.Keys];

    /// <summary>
    /// Reads one interface, returning null when the host does not carry it.
    /// </summary>
    public AwgDevice? Find(string name) => _devices.GetValueOrDefault(name);

    /// <summary>
    /// Reads every interface the host carries.
    /// </summary>
    public IReadOnlyList<AwgDevice> List() => [.. _devices.Values];

    /// <summary>
    /// Puts a change on an interface.
    /// </summary>
    public void Apply(AwgUpdate update) => Updates.Add(update);
}

/// <summary>
/// Host tools that answer from a script and write down what they were called with.
/// </summary>
public sealed class Tools : IHostCommands
{
    /// <summary>
    /// The calls the tools took, as command lines.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// What the standard input of the last call carried.
    /// </summary>
    public string Input { get; private set; } = string.Empty;

    /// <summary>
    /// What a call whose line starts with the key answers with.
    /// </summary>
    public Dictionary<string, CommandResult> Answers { get; } = [];

    /// <summary>
    /// Runs a tool with the arguments and the standard input it takes.
    /// </summary>
    public Task<CommandResult> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? input,
        CancellationToken ct)
    {
        var line = $"{file} {string.Join(" ", arguments)}".Trim();
        Calls.Add(line);
        Input = input ?? string.Empty;

        foreach (var answer in Answers)
        {
            if (line.StartsWith(answer.Key, StringComparison.Ordinal))
            {
                return Task.FromResult(answer.Value);
            }
        }

        return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
    }

    /// <summary>
    /// Tells whether a tool was called with a line.
    /// </summary>
    public bool Called(string line) => Calls.Any(one => one.StartsWith(line, StringComparison.Ordinal));
}
