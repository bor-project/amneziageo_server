namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Names the setting an endpoint was refused over.
/// </summary>
/// <param name="Code">The code the panel turns into a phrase.</param>
/// <param name="Message">The reason in words.</param>
public sealed record ConfigFault(string Code, string Message);
