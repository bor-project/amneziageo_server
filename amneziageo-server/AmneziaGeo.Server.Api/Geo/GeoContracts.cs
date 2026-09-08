using AmneziaGeo.Server.Geo;

namespace AmneziaGeo.Server.Api.Geo;

/// <summary>
/// A geo source as the interface reads it.
/// </summary>
public sealed record GeoSourceResponse(
    long Id,
    string Name,
    string Kind,
    string Url,
    int Position,
    bool IsEnabled,
    DateTimeOffset? UpdatedUtc,
    string Sha256,
    int EntryCount,
    long Size,
    string LastError);

/// <summary>
/// A geo source as the interface sends it.
/// </summary>
public sealed record GeoSourceRequest(string? Name, string? Kind, string? Url, bool IsEnabled);

/// <summary>
/// Which way a source moves through the order.
/// </summary>
public sealed record GeoMoveRequest(bool Up);

/// <summary>
/// The codes the databases carry, as the interface reads them.
/// </summary>
public sealed record GeoKeysResponse(string[] Countries, string[] Categories);

/// <summary>
/// Turns geo sources between the shape the panel holds and the shape the interface reads.
/// </summary>
public static class GeoAnswers
{
    /// <summary>
    /// Describes a source as the interface reads it.
    /// </summary>
    public static GeoSourceResponse Source(GeoSource source) => new(
        source.Id,
        source.Name,
        source.Kind,
        source.Url,
        source.Position,
        source.IsEnabled,
        source.UpdatedUtc,
        source.Sha256,
        source.EntryCount,
        source.Size,
        source.LastError);

    /// <summary>
    /// Reads the source an interface sends.
    /// </summary>
    public static GeoSource Draft(GeoSourceRequest request) => new()
    {
        Name = (request.Name ?? string.Empty).Trim(),
        Kind = (request.Kind ?? string.Empty).Trim(),
        Url = (request.Url ?? string.Empty).Trim(),
        IsEnabled = request.IsEnabled,
    };
}
