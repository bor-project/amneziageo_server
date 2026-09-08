namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// Names the setting an outbound was refused over.
/// </summary>
/// <param name="Code">The code the panel turns into a phrase.</param>
/// <param name="Message">The reason in words.</param>
public sealed record OutboundFault(string Code, string Message);
