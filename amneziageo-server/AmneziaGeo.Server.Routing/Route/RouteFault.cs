namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Why a routing rule is unusable.
/// </summary>
/// <param name="Code">The name of the refusal the panel translates.</param>
/// <param name="Message">The reason in plain words.</param>
public sealed record RouteFault(string Code, string Message);
