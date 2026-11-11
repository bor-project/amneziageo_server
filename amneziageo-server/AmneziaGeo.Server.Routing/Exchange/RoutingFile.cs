using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmneziaGeo.Server.Routing.Exchange;

/// <summary>
/// The file the routing of a server travels in: the rules, the basic lists, the routing presets and the addresses
/// of the geo sources. The channels and their keys stay behind, every server grows its own.
/// </summary>
public static class RoutingFile
{
    /// <summary>
    /// The marker stored in the file so an import can refuse unrelated JSON.
    /// </summary>
    public const string FormatTag = "amneziageo-routing";

    /// <summary>
    /// The schema version this build writes and can read.
    /// </summary>
    public const int CurrentVersion = 3;

    /// <summary>
    /// The JSON settings the file is written and read by.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads a routing file, refusing one that carries nothing, is of another kind, or is newer than this build reads.
    /// </summary>
    public static RoutingRead Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return RoutingRead.No("routing-empty", "the file carries nothing");
        }

        RoutingDoc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<RoutingDoc>(text, Options);
        }
        catch (JsonException)
        {
            return RoutingRead.No("routing-broken", "the file is not JSON a routing file reads");
        }

        if (doc is null || string.IsNullOrEmpty(doc.Format))
        {
            return RoutingRead.No("routing-empty", "the file carries nothing");
        }

        doc = new RoutingDoc(
            doc.Format,
            doc.Version,
            doc.Rules ?? [],
            doc.Basic ?? new BasicBlock([], []),
            doc.Presets ?? [],
            doc.Sources ?? [],
            doc.AddressGroups ?? [],
            doc.Unified);

        if (!string.Equals(doc.Format, FormatTag, StringComparison.Ordinal))
        {
            return RoutingRead.No("routing-kind", "the file is not a routing file of AmneziaGeo");
        }

        if (doc.Version > CurrentVersion)
        {
            return RoutingRead.No("routing-newer", $"the file is of schema {doc.Version}, this build reads up to {CurrentVersion}");
        }

        return RoutingRead.Done(doc);
    }
}

/// <summary>
/// What a read of a routing file left: the file, or why it was refused.
/// </summary>
public sealed record RoutingRead(RoutingDoc? Doc, string Code, string Message)
{
    /// <summary>
    /// Tells whether the read took the file in.
    /// </summary>
    public bool IsOk => Doc is not null;

    /// <summary>
    /// Answers a read that took the file in.
    /// </summary>
    public static RoutingRead Done(RoutingDoc doc) => new(doc, string.Empty, string.Empty);

    /// <summary>
    /// Answers a read that refused the file.
    /// </summary>
    public static RoutingRead No(string code, string message) => new(null, code, message);
}

/// <summary>
/// The routing of one server as the file carries it.
/// </summary>
/// <param name="Format">The marker of the kind of the file.</param>
/// <param name="Version">The schema version the file was written by.</param>
/// <param name="Rules">The rules of the server, in the order they act.</param>
/// <param name="Basic">The basic lists the host sends straight out or drops by.</param>
/// <param name="Presets">The routing lists the panel hands to the clients.</param>
/// <param name="Sources">The geo sources by their addresses, without the files themselves.</param>
public sealed record RoutingDoc(
    string Format,
    int Version,
    IReadOnlyList<RuleBlock> Rules,
    BasicBlock Basic,
    IReadOnlyList<PresetBlock> Presets,
    IReadOnlyList<SourceBlock> Sources,
    IReadOnlyList<AddressGroupBlock>? AddressGroups = null,
    bool Unified = false);

/// <summary>
/// Transfers an address group.
/// </summary>
public sealed record AddressGroupBlock(string Name, IReadOnlyList<string> Targets, IReadOnlyList<string>? Dns = null);

/// <summary>
/// One routing rule as the file carries it.
/// </summary>
public sealed record RuleBlock(
    string Name,
    int Position,
    bool IsEnabled,
    string Action,
    string Outbound,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Clients,
    IReadOnlyList<string> Inbounds,
    IReadOnlyList<string> Ports,
    IReadOnlyList<string> SourcePorts,
    string Protocol,
    IReadOnlyList<string>? Groups = null,
    long? LegacyId = null);

/// <summary>
/// The basic lists as the file carries them.
/// </summary>
public sealed record BasicBlock(IReadOnlyList<string> Direct, IReadOnlyList<string> Block);

/// <summary>
/// One routing preset as the file carries it.
/// </summary>
public sealed record PresetBlock(
    string Name,
    IReadOnlyList<string> Proxy,
    IReadOnlyList<string> Direct,
    IReadOnlyList<string> Block,
    bool AllUdp,
    bool Full,
    bool IsDefault,
    bool ReadOnly);

/// <summary>
/// One geo source as the file carries it, by its address.
/// </summary>
public sealed record SourceBlock(string Name, string Kind, string Url, int Position, bool IsEnabled);
