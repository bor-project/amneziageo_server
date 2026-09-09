namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// Why the settings of the proxy were refused.
/// </summary>
/// <param name="Code">The short name of the refusal.</param>
/// <param name="Message">What exactly is wrong.</param>
public sealed record ProxyFault(string Code, string Message);
