using System.Text.Json;
using System.Text.Json.Serialization;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Exchange;

/// <summary>
/// Transfers routing definitions and their ordered mappings.
/// </summary>
public static class RoutingFile
{
    public const string FormatTag = "amneziageo-routing";
    public const int CurrentVersion = 4;
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static RoutingRead Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return RoutingRead.No("routing-empty", "the file carries nothing");
        }

        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                return RoutingRead.No("routing-kind", "the file is not a routing document");
            }

            var header = JsonSerializer.Deserialize<Header>(text, Options);
            if (header?.Format != FormatTag)
            {
                return RoutingRead.No("routing-kind", "the file is not a routing file of AmneziaGeo");
            }

            if (header.Version is < 1 or > CurrentVersion)
            {
                return RoutingRead.No("routing-newer", $"unsupported routing schema {header.Version}");
            }

            var doc = header.Version == CurrentVersion
                ? JsonSerializer.Deserialize<RoutingDoc>(text, Options)
                : Upgrade(JsonSerializer.Deserialize<LegacyDoc>(text, Options)!);
            if (doc is null)
            {
                return RoutingRead.No("routing-empty", "the file carries nothing");
            }

            doc = doc with { Rules = doc.Rules ?? [], Entries = doc.Entries ?? [], Presets = doc.Presets ?? [], Sources = doc.Sources ?? [] };
            return Valid(doc) ? RoutingRead.Done(doc) : RoutingRead.No("routing-broken", "the routing document has invalid records");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return RoutingRead.No("routing-broken", "the file is not a valid routing document");
        }
    }

    private static bool Valid(RoutingDoc doc) =>
        doc.Rules.All(one => one is not null && RouteRules.CheckName(one.Name) is null
            && one.Targets is not null && one.Targets.All(value => value is not null)
            && one.Dns is not null && one.Dns.All(value => value is not null)
            && TrafficDnsMode.Known(one.DnsMode)
            && (one.Profile is null || one.Profile == RouteDefaults.InternetOnly && HasTargets(one.Targets))
            && (one.DnsMembers is null || one.DnsMembers.All(pair => !string.IsNullOrWhiteSpace(pair.Key)
                && pair.Value is not null && pair.Value.All(value => value is not null))))
        && doc.Rules.Count(one => one.Profile is not null) <= 1
        && doc.Rules.Select(one => one.Name).Distinct(StringComparer.Ordinal).Count() == doc.Rules.Count
        && doc.Entries.All(one => one is not null && RouteRules.CheckName(one.Name) is null
            && one.Action is not null && one.Outbound is not null && one.Protocol is not null
            && one.Rules is { Count: > 0 } && one.Rules.All(value => !string.IsNullOrWhiteSpace(value))
            && one.Clients is not null && one.Clients.All(value => value is not null)
            && one.Inbounds is not null && one.Inbounds.All(value => value is not null)
            && one.Ports is not null && one.Ports.All(value => value is not null)
            && one.SourcePorts is not null && one.SourcePorts.All(value => value is not null))
        && doc.Entries.Select(one => one.Name).Distinct(StringComparer.Ordinal).Count() == doc.Entries.Count
        && doc.Presets.All(one => one is not null && one.Name is not null && one.Proxy is not null
            && one.Direct is not null && one.Block is not null)
        && doc.Sources.All(one => one is not null && one.Name is not null && one.Kind is not null && one.Url is not null);

    private static bool HasTargets(IReadOnlyList<string>? targets) =>
        targets?.Any(value => value.Trim(',', ' ', '\t', '\r', '\n').Length > 0) == true;

    private static RoutingDoc Upgrade(LegacyDoc old)
    {
        var groups = old.AddressGroups ?? [];
        if (groups.Any(one => one is null || string.IsNullOrWhiteSpace(one.Name)
                || one.Targets?.Any(value => value is null) == true || one.Dns?.Any(value => value is null) == true)
            || (old.Rules ?? []).Any(one => one is null || string.IsNullOrWhiteSpace(one.Name)
                || one.Outbound is null || one.Action is null || one.Groups?.Any(value => value is null) == true
                || one.Targets?.Any(value => value is null) == true))
        {
            throw new ArgumentException("invalid legacy routing records", nameof(old));
        }

        if (groups.Any(group => !HasTargets(group.Targets)
            && (old.Rules ?? []).Any(row => row.Groups?.Contains(group.Name, StringComparer.Ordinal) == true)))
        {
            throw new ArgumentException("a referenced legacy group has no targets", nameof(old));
        }

        var definitions = groups.Select(one => new TrafficRuleBlock(one.Name, one.Targets ?? [], one.Dns ?? [],
            DnsMode: one.Dns is { Count: > 0 } ? TrafficDnsMode.Custom : TrafficDnsMode.Default)).ToList();
        var names = definitions.Select(one => one.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = new List<EntryBlock>();
        foreach (var row in (old.Rules ?? []).OrderBy(one => one.Position))
        {
            var selected = (row.Groups ?? []).Distinct(StringComparer.Ordinal)
                .OrderBy(name => groups.FirstOrDefault(one => one.Name == name)?.Dns is { Count: > 0 } ? 0 : 1).ToList();
            if (HasTargets(row.Targets) || selected.Count == 0)
            {
                var name = FreeName(row.Name, names);
                definitions.Add(new TrafficRuleBlock(name, HasTargets(row.Targets) ? row.Targets! : [], [], DnsMode: TrafficDnsMode.Default));
                selected.Add(name);
            }
            entries.Add(new EntryBlock(row.Name, row.Position, row.IsEnabled, row.Action, row.Outbound, selected, row.Clients ?? [], row.Inbounds ?? [], row.Ports ?? [], row.SourcePorts ?? [], row.Protocol ?? RouteProtocol.Any, row.LegacyId));
        }

        if (!old.Unified && old.Basic is { } basic)
        {
            Basic("block", basic.Block ?? [], RouteAction.Block, RouteBasic.BlockId, int.MinValue);
            Basic("direct", basic.Direct ?? [], RouteAction.Direct, RouteBasic.DirectId, int.MinValue + 1);
        }

        return new RoutingDoc(FormatTag, CurrentVersion, definitions, [.. entries.OrderBy(one => one.Position)], old.Presets ?? [], old.Sources ?? []);

        void Basic(string name, IReadOnlyList<string> targets, string action, long id, int position)
        {
            if (targets.Count == 0)
            {
                return;
            }

            var criterion = FreeName(name, names);
            definitions.Add(new TrafficRuleBlock(criterion, targets, [], DnsMode: TrafficDnsMode.Default));
            entries.Add(new EntryBlock(name, position, true, action, string.Empty, [criterion], [], [], [], [], RouteProtocol.Any, id));
        }
    }

    private static string FreeName(string value, HashSet<string> names)
    {
        var name = value.Length > RouteRules.MaxNameLength ? value[..RouteRules.MaxNameLength] : value;
        for (var number = 0; ; number++)
        {
            var suffix = number == 0 ? string.Empty : "-" + number;
            var candidate = name[..Math.Min(name.Length, RouteRules.MaxNameLength - suffix.Length)] + suffix;
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private sealed record Header(string Format, int Version);
    private sealed record LegacyDoc(string Format, int Version, IReadOnlyList<LegacyRow>? Rules, LegacyBasic? Basic,
        IReadOnlyList<PresetBlock>? Presets, IReadOnlyList<SourceBlock>? Sources, IReadOnlyList<LegacyGroup>? AddressGroups = null, bool Unified = false);
    private sealed record LegacyGroup(string Name, IReadOnlyList<string>? Targets, IReadOnlyList<string>? Dns = null);
    private sealed record LegacyBasic(IReadOnlyList<string>? Direct, IReadOnlyList<string>? Block);
    private sealed record LegacyRow(string Name, int Position, bool IsEnabled, string Action, string Outbound,
        IReadOnlyList<string>? Targets, IReadOnlyList<string>? Clients, IReadOnlyList<string>? Inbounds,
        IReadOnlyList<string>? Ports, IReadOnlyList<string>? SourcePorts, string? Protocol,
        IReadOnlyList<string>? Groups = null, long? LegacyId = null);
}

public sealed record RoutingRead(RoutingDoc? Doc, string Code, string Message)
{
    public bool IsOk => Doc is not null;
    public static RoutingRead Done(RoutingDoc doc) => new(doc, string.Empty, string.Empty);
    public static RoutingRead No(string code, string message) => new(null, code, message);
}

public sealed record RoutingDoc(string Format, int Version, IReadOnlyList<TrafficRuleBlock> Rules,
    IReadOnlyList<EntryBlock> Entries, IReadOnlyList<PresetBlock> Presets, IReadOnlyList<SourceBlock> Sources);

public sealed record TrafficRuleBlock(string Name, IReadOnlyList<string> Targets, IReadOnlyList<string> Dns,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DnsMembers = null,
    string DnsMode = TrafficDnsMode.Custom, string? Profile = null);

public sealed record EntryBlock(string Name, int Position, bool IsEnabled, string Action, string Outbound,
    IReadOnlyList<string> Rules, IReadOnlyList<string> Clients, IReadOnlyList<string> Inbounds,
    IReadOnlyList<string> Ports, IReadOnlyList<string> SourcePorts, string Protocol, long? LegacyId = null);

public sealed record PresetBlock(string Name, IReadOnlyList<string> Proxy, IReadOnlyList<string> Direct,
    IReadOnlyList<string> Block, bool AllUdp, bool Full, bool IsDefault, bool ReadOnly);

public sealed record SourceBlock(string Name, string Kind, string Url, int Position, bool IsEnabled);
