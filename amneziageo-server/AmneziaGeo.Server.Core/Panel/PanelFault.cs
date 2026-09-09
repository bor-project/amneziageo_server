namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Why the settings of the panel were refused.
/// </summary>
/// <param name="Code">The short name of the refusal.</param>
/// <param name="Message">What exactly is wrong.</param>
public sealed record PanelFault(string Code, string Message);
