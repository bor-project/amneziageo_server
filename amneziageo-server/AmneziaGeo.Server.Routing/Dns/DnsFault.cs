namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Why the resolver settings were refused.
/// </summary>
/// <param name="Code">The short name of the refusal.</param>
/// <param name="Message">What exactly is wrong.</param>
public sealed record DnsFault(string Code, string Message);
