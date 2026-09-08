namespace AmneziaGeo.Server.Routing.Balance;

/// <summary>
/// Why a balancer is unusable.
/// </summary>
/// <param name="Code">The name of the refusal the panel translates.</param>
/// <param name="Message">The reason in plain words.</param>
public sealed record BalanceFault(string Code, string Message);
