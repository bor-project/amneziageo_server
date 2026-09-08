namespace AmneziaGeo.Server.Geo;

/// <summary>
/// Names the setting a geo source was refused over.
/// </summary>
/// <param name="Code">The code the panel turns into a phrase.</param>
/// <param name="Message">The reason in words.</param>
public sealed record GeoFault(string Code, string Message);
