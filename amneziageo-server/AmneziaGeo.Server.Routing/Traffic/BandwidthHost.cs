using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Routing.Traffic;

/// <summary>
/// Applies client bandwidth ceilings to tunnel interfaces.
/// </summary>
public sealed class ClientBandwidthHost
{
    private const string Root = "a804:";
    private const string Ingress = "ffff:";
    private readonly IHostCommands _commands;
    private readonly ILogger<ClientBandwidthHost> _logger;
    private readonly SemaphoreSlim _serial = new(1, 1);

    /// <summary>
    /// ctor
    /// </summary>
    public ClientBandwidthHost(IHostCommands commands, ILogger<ClientBandwidthHost>? logger = null)
    {
        _commands = commands;
        _logger = logger ?? NullLogger<ClientBandwidthHost>.Instance;
    }

    /// <summary>
    /// Applies the ceilings of the clients of an interface.
    /// </summary>
    public async Task<BandwidthSync> ApplyAsync(
        string interfaceName,
        IReadOnlyList<ClientBandwidth> clients,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clients);
        await _serial.WaitAsync(ct).ConfigureAwait(false);
        var undo = new List<Func<Task>>();
        try
        {
            var plan = BandwidthPlan.Create(interfaceName, clients);
            var link = await LinkAsync(plan.Interface, ct).ConfigureAwait(false);
            var ifb = await LinkAsync(plan.Ifb, ct).ConfigureAwait(false);
            var owned = ifb is { } found && Text(found, "ifalias").StartsWith(plan.Owner, StringComparison.Ordinal)
                && Text(Child(found, "linkinfo"), "info_kind") == "ifb";
            if (!plan.IsLimited && !owned)
            {
                return new BandwidthSync(true, string.Empty);
            }

            if (ifb is not null && !owned)
            {
                throw new InvalidOperationException($"the host carries a foreign interface called '{plan.Ifb}'");
            }

            if (link is null)
            {
                if (plan.IsLimited)
                {
                    throw new InvalidOperationException($"the host carries no interface called '{plan.Interface}'");
                }

                await TargetAsync(plan.Ifb, true, false, ct).ConfigureAwait(false);
                await DeleteIfbAsync(plan, ct).ConfigureAwait(false);

                return new BandwidthSync(true, string.Empty);
            }

            var alias = owned ? Text(ifb!.Value, "ifalias") : string.Empty;
            var ownsIngress = owned && alias.StartsWith(plan.Owner + "u:", StringComparison.Ordinal);
            var finalAlias = plan.Owner + (plan.Upload.IsLimited ? "u:" : "d:") + plan.Fingerprint;
            var pendingAlias = plan.Owner + (ownsIngress || plan.Upload.IsLimited ? "u:" : "d:") + "pending";
            var down = await TargetAsync(plan.Interface, owned, plan.IsLimited, ct).ConfigureAwait(false);
            var ingress = down.Qdiscs.Where(one => Text(one, "kind") is "ingress" or "clsact").ToArray();
            if (ingress.Any(one => !ownsIngress || !IsIngress(one)) && plan.Upload.IsLimited)
            {
                throw new InvalidOperationException($"the interface '{plan.Interface}' carries a foreign ingress qdisc");
            }

            var redirects = ingress.Any(IsIngress) && ownsIngress
                ? await FiltersAsync(plan.Interface, Ingress, ct).ConfigureAwait(false)
                : [];
            CheckFilters(redirects, plan.Interface);
            var up = owned
                ? await TargetAsync(plan.Ifb, true, plan.Upload.IsLimited, ct).ConfigureAwait(false)
                : Target.Empty;
            if (alias == finalAlias
                && Current(down, plan.Download, false)
                && Current(up, plan.Upload, true)
                && ((ownsIngress && ingress.Any(IsIngress)) == plan.Upload.IsLimited)
                && FiltersMatch(redirects, plan.Upload.Filters, true, plan.Ifb)
                && (!plan.Upload.IsLimited || (Number(ifb!.Value, "mtu") == Number(link.Value, "mtu") && IsUp(ifb.Value))))
            {
                return new BandwidthSync(true, string.Empty);
            }

            if (!owned)
            {
                await RunAsync("ip",
                    ["link", "add", "name", plan.Ifb, "alias", pendingAlias, "type", "ifb"], ct)
                    .ConfigureAwait(false);
                var created = await LinkAsync(plan.Ifb, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("the host did not create the bandwidth interface");
                var index = Number(created, "ifindex");
                if (index <= 0 || Text(Child(created, "linkinfo"), "info_kind") != "ifb")
                {
                    throw new InvalidOperationException("the bandwidth interface returned an invalid identity");
                }

                undo.Add(() => DeleteNewIfbAsync(plan, index));
                await RunAsync("ip", ["link", "set", "dev", plan.Ifb, "alias", pendingAlias], ct)
                    .ConfigureAwait(false);
            }
            else
            {
                await RunAsync("ip", ["link", "set", "dev", plan.Ifb, "alias", pendingAlias], ct)
                    .ConfigureAwait(false);
            }

            if (plan.Upload.IsLimited)
            {
                await RunAsync("ip",
                    ["link", "set", "dev", plan.Ifb, "mtu", Number(link.Value, "mtu").ToString(CultureInfo.InvariantCulture), "up"], ct)
                    .ConfigureAwait(false);
            }
            else if (ingress.Any(IsIngress) && ownsIngress)
            {
                await DeleteQdiscAsync(plan, plan.Interface, true, ct).ConfigureAwait(false);
            }

            await ConfigureAsync(plan, plan.Interface, down, plan.Download, false, alias != finalAlias, undo, ct).ConfigureAwait(false);
            await ConfigureAsync(plan, plan.Ifb, up, plan.Upload, true, alias != finalAlias, undo, ct).ConfigureAwait(false);
            if (plan.Upload.IsLimited)
            {
                if (!ownsIngress || !ingress.Any(IsIngress))
                {
                    await RunAsync("tc", ["qdisc", "add", "dev", plan.Interface, "handle", Ingress, "ingress"], ct)
                        .ConfigureAwait(false);
                    undo.Add(() => DeleteQdiscAsync(plan, plan.Interface, true, CancellationToken.None));
                }

                await ConfigureFiltersAsync(plan, plan.Interface, Ingress, redirects, plan.Upload.Filters, true, plan.Ifb, undo, ct)
                    .ConfigureAwait(false);
            }

            if (plan.IsLimited)
            {
                await RunAsync("ip", ["link", "set", "dev", plan.Ifb, "alias", finalAlias], ct)
                    .ConfigureAwait(false);
            }
            else
            {
                await DeleteIfbAsync(plan, ct).ConfigureAwait(false);
            }

            return new BandwidthSync(true, string.Empty);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "the bandwidth update of {Interface} was canceled", interfaceName);
            await RollbackAsync(undo).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException
            or UnauthorizedAccessException or HostNetworkException or Win32Exception or JsonException)
        {
            _logger.LogWarning(ex, "the bandwidth ceilings of {Interface} were not applied", interfaceName);
            var rollback = await RollbackAsync(undo).ConfigureAwait(false);

            return new BandwidthSync(false, ex.Message + rollback);
        }
        finally
        {
            _serial.Release();
        }
    }

    private async Task ConfigureAsync(
        BandwidthPlan plan,
        string name,
        Target state,
        BandwidthDirection direction,
        bool upload,
        bool forceRates,
        List<Func<Task>> undo,
        CancellationToken ct)
    {
        if (!direction.IsLimited)
        {
            if (state.HasRoot)
            {
                await DeleteQdiscAsync(plan, name, false, ct).ConfigureAwait(false);
            }

            return;
        }

        if (!state.HasRoot)
        {
            await RunAsync("tc", ["qdisc", "add", "dev", name, "root", "handle", Root, "htb", "default", "0", "direct_qlen", "1024"], ct)
                .ConfigureAwait(false);
            undo.Add(() => DeleteQdiscAsync(plan, name, false, CancellationToken.None));
        }
        foreach (var one in direction.Classes)
        {
            if (forceRates || !state.Classes.Any(existing => ClassMatches(existing, one)))
            {
                var rate = (one.BytesPerSecond * 8).ToString(CultureInfo.InvariantCulture) + "bit";
                var burst = Math.Max(1600, Math.Min(uint.MaxValue, one.BytesPerSecond / 100)).ToString(CultureInfo.InvariantCulture);
                await RunAsync("tc",
                    ["class", "replace", "dev", name, "parent", Root, "classid", one.Handle,
                        "htb", "rate", rate, "ceil", rate, "burst", burst, "cburst", burst, "quantum", "1514"], ct)
                    .ConfigureAwait(false);
                var previous = state.Classes.Where(existing => Text(existing, "handle") == one.Handle).ToArray();
                if (previous.Length == 0)
                {
                    undo.Add(() => UndoAsync(plan, name,
                        ["class", "del", "dev", name, "classid", one.Handle]));
                }
                else
                {
                    undo.Add(() => RestoreClassAsync(plan, name, previous[0], false));
                }
            }

            if (!state.Qdiscs.Any(existing => Text(existing, "parent") == one.Handle))
            {
                await RunAsync("tc",
                    ["qdisc", "add", "dev", name, "parent", one.Handle, "handle", $"{one.Id:x}:",
                        "fq_codel", "flows", "128", "limit", "1024", "memory_limit", "1048576"], ct)
                    .ConfigureAwait(false);
                undo.Add(() => UndoAsync(plan, name,
                    ["qdisc", "del", "dev", name, "parent", one.Handle, "handle", $"{one.Id:x}:"]));
            }
        }

        await ConfigureFiltersAsync(plan, name, Root, state.Filters, direction.Filters, upload, null, undo, ct).ConfigureAwait(false);
        foreach (var stale in state.Classes.Where(one => !direction.Classes.Any(wanted => wanted.Handle == Text(one, "handle"))))
        {
            await RunAsync("tc", ["class", "del", "dev", name, "classid", Text(stale, "handle")], ct)
                .ConfigureAwait(false);
            undo.Add(() => RestoreClassAsync(plan, name, stale, true));
        }
    }

    private async Task RestoreClassAsync(BandwidthPlan plan, string name, JsonElement previous, bool leaf)
    {
        var handle = Text(previous, "handle");
        var bytes = Number(Child(previous, "options"), "rate");
        var rate = (bytes * 8).ToString(CultureInfo.InvariantCulture) + "bit";
        var ceil = (Number(Child(previous, "options"), "ceil") * 8).ToString(CultureInfo.InvariantCulture) + "bit";
        var burst = Math.Max(1600, Math.Min(uint.MaxValue, bytes / 100)).ToString(CultureInfo.InvariantCulture);
        await UndoAsync(plan, name,
            ["class", "replace", "dev", name, "parent", Root, "classid", handle,
                "htb", "rate", rate, "ceil", ceil, "burst", burst, "cburst", burst, "quantum", "1514"])
            .ConfigureAwait(false);
        if (leaf)
        {
            await UndoAsync(plan, name,
                ["qdisc", "add", "dev", name, "parent", handle, "handle", handle[(handle.IndexOf(':') + 1)..] + ":",
                    "fq_codel", "flows", "128", "limit", "1024", "memory_limit", "1048576"]).ConfigureAwait(false);
        }
    }

    private async Task ConfigureFiltersAsync(
        BandwidthPlan plan,
        string name,
        string parent,
        IReadOnlyList<JsonElement> existing,
        IReadOnlyList<BandwidthFilter> wanted,
        bool upload,
        string? redirect,
        List<Func<Task>> undo,
        CancellationToken ct)
    {
        var remaining = existing.ToList();
        foreach (var stale in existing.Where(one => !wanted.Any(filter => SameFilter(one, filter) && PrefixMatches(one, filter, upload))))
        {
            await RunAsync("tc", FilterDelete(name, parent, stale), ct).ConfigureAwait(false);
            undo.Add(() => UndoAsync(plan, name, FilterRestore(name, parent, stale, upload, redirect)));
            remaining.Remove(stale);
        }

        foreach (var filter in wanted)
        {
            if (remaining.Any(one => FilterMatches(one, filter, upload, redirect)))
            {
                continue;
            }

            var arguments = new List<string>
            {
                "filter", "replace", "dev", name, "parent", parent,
                "protocol", filter.Protocol, "pref", filter.Preference, "handle", filter.Tag,
                "flower", "skip_hw", upload ? "src_ip" : "dst_ip", filter.Prefix,
            };
            if (redirect is null)
            {
                arguments.AddRange(["classid", filter.Class]);
            }
            else
            {
                arguments.AddRange(["action", "mirred", "egress", "redirect", "dev", redirect]);
            }

            await RunAsync("tc", arguments, ct).ConfigureAwait(false);
            if (!remaining.Any(one => SameFilter(one, filter)))
            {
                undo.Add(() => UndoAsync(plan, name,
                    ["filter", "del", "dev", name, "parent", parent, "protocol", filter.Protocol,
                        "pref", filter.Preference, "handle", filter.Tag, "flower"]));
            }
        }
    }

    private static IReadOnlyList<string> FilterDelete(string name, string parent, JsonElement filter) =>
        ["filter", "del", "dev", name, "parent", parent, "protocol", Text(filter, "protocol"),
            "pref", Number(filter, "pref").ToString(CultureInfo.InvariantCulture),
            "handle", FilterHandle(filter), "flower"];

    private static IReadOnlyList<string> FilterRestore(
        string name,
        string parent,
        JsonElement filter,
        bool upload,
        string? redirect)
    {
        var options = Child(filter, "options");
        var arguments = new List<string>
        {
            "filter", "replace", "dev", name, "parent", parent, "protocol", Text(filter, "protocol"),
            "pref", Number(filter, "pref").ToString(CultureInfo.InvariantCulture), "handle", FilterHandle(filter),
            "flower", "skip_hw", upload ? "src_ip" : "dst_ip", Text(Child(options, "keys"), upload ? "src_ip" : "dst_ip"),
        };
        arguments.AddRange(redirect is null
            ? ["classid", Text(options, "classid")]
            : ["action", "mirred", "egress", "redirect", "dev", redirect]);

        return arguments;
    }

    private async Task<Target> TargetAsync(string name, bool owned, bool needed, CancellationToken ct)
    {
        var qdiscs = await JsonAsync("tc", ["-j", "qdisc", "show", "dev", name], ct).ConfigureAwait(false);
        var roots = qdiscs.Where(one => Flag(one, "root")).ToArray();
        var ours = roots.Any(IsRoot) && owned;
        if (roots.Any(one => !IsRoot(one) && Text(one, "handle") != "0:")
            || (roots.Any(IsRoot) && !owned))
        {
            if (needed || owned)
            {
                throw new InvalidOperationException($"the interface '{name}' carries a foreign root qdisc");
            }
        }

        if (ours && (Number(Child(roots.Single(IsRoot), "options"), "default") != 0
            || Number(Child(roots.Single(IsRoot), "options"), "direct_qlen") != 1024))
        {
            throw new InvalidOperationException($"the bandwidth root settings of '{name}' changed");
        }

        if (owned && qdiscs.Any(one => !Flag(one, "root") && Text(one, "kind") is not "ingress" and not "clsact"
            && (Text(one, "kind") != "fq_codel" || !Text(one, "parent").StartsWith(Root, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException($"the interface '{name}' carries a foreign child qdisc");
        }

        var classes = ours
            ? await ClassesAsync(name, qdiscs, ct).ConfigureAwait(false)
            : [];
        if (classes.Any(one => Text(one, "kind") != "htb" || !Text(one, "handle").StartsWith(Root, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"the interface '{name}' carries foreign traffic classes");
        }

        var filters = ours ? await FiltersAsync(name, Root, ct).ConfigureAwait(false) : [];
        CheckFilters(filters, name);

        return new Target(qdiscs, classes, filters, ours);
    }

    private async Task<JsonElement[]> ClassesAsync(string name, IReadOnlyList<JsonElement> qdiscs, CancellationToken ct)
    {
        var result = await _commands.RunAsync("tc", ["-j", "class", "show", "dev", name], null, ct)
            .ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new HostNetworkException($"the traffic classes could not be read: {result.Complaint}");
        }

        var leaves = qdiscs.Where(one => Text(one, "kind") == "fq_codel"
            && Text(one, "parent").StartsWith(Root, StringComparison.Ordinal))
            .Select(one => Text(one, "handle")).ToHashSet(StringComparer.Ordinal);

        return BandwidthClasses.Read(result.Output, leaves);
    }

    private async Task<JsonElement[]> FiltersAsync(string name, string parent, CancellationToken ct)
    {
        var rows = await JsonAsync("tc", ["-j", "filter", "show", "dev", name, "parent", parent], ct).ConfigureAwait(false);

        return rows.Where(one => one.TryGetProperty("options", out _)).ToArray();
    }

    private static void CheckFilters(IReadOnlyList<JsonElement> filters, string name)
    {
        if (filters.Any(one => Text(one, "kind") != "flower"
            || Text(one, "protocol") is not "ip" and not "ipv6"
            || Number(one, "pref") != (Text(one, "protocol") == "ip" ? 100 : 101)
            || Number(one, "chain") != 0
            || FilterHandle(one).Length == 0))
        {
            throw new InvalidOperationException($"the interface '{name}' carries foreign traffic filters");
        }
    }

    private static bool Current(Target state, BandwidthDirection wanted, bool upload) =>
        state.HasRoot == wanted.IsLimited
        && state.Classes.Count == wanted.Classes.Count
        && wanted.Classes.All(one => state.Classes.Any(existing => ClassMatches(existing, one)))
        && wanted.Classes.All(one => state.Qdiscs.Any(existing =>
            Text(existing, "kind") == "fq_codel" && Text(existing, "parent") == one.Handle))
        && FiltersMatch(state.Filters, wanted.Filters, upload, null)
        && (!state.HasRoot || Number(Child(state.Qdiscs.Single(IsRoot), "options"), "default") == 0);

    private static bool ClassMatches(JsonElement existing, BandwidthClass wanted) =>
        Text(existing, "handle") == wanted.Handle
        && Math.Abs(Number(Child(existing, "options"), "rate") - wanted.BytesPerSecond)
            <= (Flag(existing, "legacy") ? Math.Max(1, wanted.BytesPerSecond / 100_000) : 0)
        && Math.Abs(Number(Child(existing, "options"), "ceil") - wanted.BytesPerSecond)
            <= (Flag(existing, "legacy") ? Math.Max(1, wanted.BytesPerSecond / 100_000) : 0);

    private static bool FiltersMatch(
        IReadOnlyList<JsonElement> existing,
        IReadOnlyList<BandwidthFilter> wanted,
        bool upload,
        string? redirect) =>
        existing.Count == wanted.Count && wanted.All(filter => existing.Any(one => FilterMatches(one, filter, upload, redirect)));

    private static bool FilterMatches(JsonElement existing, BandwidthFilter wanted, bool upload, string? redirect)
    {
        var options = Child(existing, "options");
        if (!SameFilter(existing, wanted) || !PrefixMatches(existing, wanted, upload))
        {
            return false;
        }

        if (redirect is null)
        {
            return Text(options, "classid") == wanted.Class
                && (!options.TryGetProperty("actions", out var classActions) || classActions.GetArrayLength() == 0);
        }

        return options.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array
            && actions.GetArrayLength() == 1
            && Text(actions[0], "kind") == "mirred"
            && Text(actions[0], "mirred_action") == "redirect"
            && Text(actions[0], "direction") == "egress"
            && Text(actions[0], "to_dev") == redirect;
    }

    private static bool PrefixMatches(JsonElement existing, BandwidthFilter wanted, bool upload) =>
        AwgAllowedIp.TryParse(Text(Child(Child(existing, "options"), "keys"), upload ? "src_ip" : "dst_ip"), out var prefix)
        && prefix.Network().ToString() == wanted.Prefix;

    private static bool SameFilter(JsonElement existing, BandwidthFilter wanted) =>
        Text(existing, "protocol") == wanted.Protocol
        && Number(existing, "pref").ToString(CultureInfo.InvariantCulture) == wanted.Preference
        && FilterHandle(existing) == wanted.Tag;

    private async Task DeleteQdiscAsync(BandwidthPlan plan, string name, bool ingress, CancellationToken ct)
    {
        var marker = await LinkAsync(plan.Ifb, ct).ConfigureAwait(false);
        if (marker is null || !Text(marker.Value, "ifalias").StartsWith(plan.Owner, StringComparison.Ordinal)
            || Text(Child(marker.Value, "linkinfo"), "info_kind") != "ifb")
        {
            throw new InvalidOperationException("the bandwidth ownership marker is missing");
        }

        var rows = await JsonAsync("tc", ["-j", "qdisc", "show", "dev", name], ct).ConfigureAwait(false);
        if (!rows.Any(ingress ? IsIngress : IsRoot))
        {
            return;
        }

        await RunAsync("tc",
            ingress
                ? ["qdisc", "del", "dev", name, "handle", Ingress, "ingress"]
                : ["qdisc", "del", "dev", name, "root", "handle", Root], ct).ConfigureAwait(false);
    }

    private async Task DeleteNewIfbAsync(BandwidthPlan plan, long index)
    {
        var link = await LinkAsync(plan.Ifb, CancellationToken.None).ConfigureAwait(false);
        if (link is null)
        {
            return;
        }

        var alias = Text(link.Value, "ifalias");
        if (Number(link.Value, "ifindex") != index || Text(Child(link.Value, "linkinfo"), "info_kind") != "ifb"
            || (alias.Length > 0 && !alias.StartsWith(plan.Owner, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("the new bandwidth interface changed during rollback");
        }

        await RunAsync("ip", ["link", "del", "dev", plan.Ifb], CancellationToken.None).ConfigureAwait(false);
    }

    private async Task DeleteIfbAsync(BandwidthPlan plan, CancellationToken ct)
    {
        var link = await LinkAsync(plan.Ifb, ct).ConfigureAwait(false);
        if (link is null)
        {
            return;
        }

        if (!Text(link.Value, "ifalias").StartsWith(plan.Owner, StringComparison.Ordinal)
            || Text(Child(link.Value, "linkinfo"), "info_kind") != "ifb")
        {
            throw new InvalidOperationException("the bandwidth ownership marker changed");
        }

        await RunAsync("ip", ["link", "del", "dev", plan.Ifb], ct).ConfigureAwait(false);
    }

    private async Task UndoAsync(BandwidthPlan plan, string name, IReadOnlyList<string> arguments)
    {
        var marker = await LinkAsync(plan.Ifb, CancellationToken.None).ConfigureAwait(false);
        if (marker is null || !Text(marker.Value, "ifalias").StartsWith(plan.Owner, StringComparison.Ordinal)
            || Text(Child(marker.Value, "linkinfo"), "info_kind") != "ifb")
        {
            throw new InvalidOperationException("the bandwidth ownership marker changed during rollback");
        }

        var qdiscs = await JsonAsync("tc", ["-j", "qdisc", "show", "dev", name], CancellationToken.None)
            .ConfigureAwait(false);
        if (!qdiscs.Any(IsRoot) && !qdiscs.Any(IsIngress))
        {
            return;
        }

        await RunAsync("tc", arguments, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<string> RollbackAsync(IReadOnlyList<Func<Task>> undo)
    {
        var errors = new List<string>();
        foreach (var action in undo.Reverse())
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
                or HostNetworkException or Win32Exception or JsonException)
            {
                _logger.LogWarning(ex, "a new bandwidth resource was not removed");
                errors.Add(ex.Message);
            }
        }

        return errors.Count == 0 ? string.Empty : "; rollback: " + string.Join("; ", errors);
    }

    private async Task<JsonElement?> LinkAsync(string name, CancellationToken ct)
    {
        var result = await _commands.RunAsync("ip", ["-j", "-d", "link", "show", "dev", name], null, ct)
            .ConfigureAwait(false);
        if (!result.IsOk)
        {
            if (result.Complaint.Contains("does not exist", StringComparison.Ordinal)
                || result.Complaint.Contains("Cannot find device", StringComparison.Ordinal))
            {
                return null;
            }

            throw new HostNetworkException($"the interface '{name}' could not be read: {result.Complaint}");
        }

        var rows = Parse(result.Output);
        if (rows.Length != 1 || Text(rows[0], "ifname") != name || Number(rows[0], "mtu") <= 0)
        {
            throw new InvalidOperationException($"the interface '{name}' returned an invalid description");
        }

        return rows[0];
    }

    private async Task<JsonElement[]> JsonAsync(string file, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var result = await _commands.RunAsync(file, arguments, null, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new HostNetworkException($"'{file}' refused the bandwidth query: {result.Complaint}");
        }

        return Parse(result.Output);
    }

    private async Task RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var result = await _commands.RunAsync(file, arguments, null, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            throw new HostNetworkException($"'{file}' refused the bandwidth change: {result.Complaint}");
        }
    }

    private static JsonElement[] Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("a bandwidth query returned no array");
        }

        return document.RootElement.EnumerateArray().Select(one => one.Clone()).ToArray();
    }

    private static bool IsRoot(JsonElement value) =>
        Flag(value, "root") && Text(value, "kind") == "htb" && Text(value, "handle") == Root;

    private static bool IsIngress(JsonElement value) =>
        Text(value, "kind") == "ingress" && Text(value, "handle") == Ingress;

    private static JsonElement Child(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) ? child : default;

    private static string Text(JsonElement value, string key) =>
        Child(value, key) is { ValueKind: JsonValueKind.String } text ? text.GetString() ?? string.Empty : string.Empty;

    private static long Number(JsonElement value, string key)
    {
        var item = Child(value, key);
        if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number))
        {
            return number;
        }

        var text = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
        return text.StartsWith("0x", StringComparison.Ordinal)
            ? long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : 0
            : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits) ? digits : 0;
    }

    private static string FilterHandle(JsonElement value)
    {
        var options = Child(value, "options");
        var number = Number(options, "handle");
        return number is > 0 and <= uint.MaxValue ? $"0x{number:x}" : string.Empty;
    }

    private static bool IsUp(JsonElement link) =>
        link.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array
        && flags.EnumerateArray().Any(one => one.ValueKind == JsonValueKind.String && one.GetString() == "UP");

    private static bool Flag(JsonElement value, string key) => Child(value, key).ValueKind == JsonValueKind.True;

    private sealed record Target(
        IReadOnlyList<JsonElement> Qdiscs,
        IReadOnlyList<JsonElement> Classes,
        IReadOnlyList<JsonElement> Filters,
        bool HasRoot)
    {
        public static readonly Target Empty = new([], [], [], false);
    }
}
