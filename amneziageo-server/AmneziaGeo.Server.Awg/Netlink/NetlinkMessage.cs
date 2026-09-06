namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// One message read off a netlink socket.
/// </summary>
public sealed record NetlinkMessage(ushort Type, ushort Flags, byte Command, ReadOnlyMemory<byte> Payload);
