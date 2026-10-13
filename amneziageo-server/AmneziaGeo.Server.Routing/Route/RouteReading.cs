namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// What the firewall hands over to the panel so that it reads the names the connections go to.
/// </summary>
/// <param name="Queue">The queue the first packets of QUIC wait in, or null when the panel takes none.</param>
/// <param name="Port">The port the panel takes TCP connections on, or null when it takes none.</param>
/// <param name="Six">Whether the panel takes the TCP connections of IPv6 too.</param>
/// <param name="Door">The port of the host the connections handed over come in through, or null when they come in as they are.</param>
public sealed record RouteReading(ushort? Queue, int? Port, bool Six = true, int? Door = null);
