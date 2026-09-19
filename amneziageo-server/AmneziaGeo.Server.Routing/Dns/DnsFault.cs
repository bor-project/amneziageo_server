namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// What is wrong with the resolver settings or with the way out of the resolver.
/// </summary>
/// <param name="Code">The short name of the refusal.</param>
/// <param name="Message">What exactly is wrong.</param>
public sealed record DnsFault(string Code, string Message);
