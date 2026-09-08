namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// The reason a client was refused.
/// </summary>
/// <param name="Code">The word the refusal is known by.</param>
/// <param name="Message">The refusal in words.</param>
public sealed record ClientFault(string Code, string Message);
