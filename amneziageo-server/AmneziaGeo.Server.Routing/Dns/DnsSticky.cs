using System.Buffers.Binary;
using System.Net;
using System.Numerics;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Selects a sticky DNS route by client address.
/// </summary>
public static class DnsSticky
{
    public static DnsWay Way(Balancer balancer, RouteWays ways, IPAddress client)
    {
        if (!balancer.IsEnabled)
        {
            return DnsWay.Closed("balancer-off", $"the balancer '{balancer.Name}' is turned off");
        }

        var members = balancer.Members.Select(ways.Outbound)
            .OfType<OutboundConfig>()
            .Where(member => member.IsEnabled && ways.IsAlive(member.Name)).ToArray();
        if (members.Length == 0)
        {
            return DnsWay.Closed("no-live-member", $"the balancer '{balancer.Name}' has no live member");
        }

        var hash = Hash(client.GetAddressBytes(), balancer.Seed);
        var index = (int)(((ulong)hash * (uint)members.Length) >> 32);
        var picked = members[index];

        return new DnsWay(picked.Mark, picked.Name, null);
    }

    public static uint Hash(ReadOnlySpan<byte> address, uint seed)
    {
        if (address.Length is not (4 or 16))
        {
            throw new ArgumentException("the address contains neither four nor sixteen bytes", nameof(address));
        }

        unchecked
        {
            var a = 0xdeadbeefu + (uint)address.Length + seed;
            var b = a;
            var c = a;
            if (address.Length == 16)
            {
                a += BinaryPrimitives.ReadUInt32LittleEndian(address);
                b += BinaryPrimitives.ReadUInt32LittleEndian(address[4..]);
                c += BinaryPrimitives.ReadUInt32LittleEndian(address[8..]);
                a = (a - c) ^ BitOperations.RotateLeft(c, 4);
                c += b;
                b = (b - a) ^ BitOperations.RotateLeft(a, 6);
                a += c;
                c = (c - b) ^ BitOperations.RotateLeft(b, 8);
                b += a;
                a = (a - c) ^ BitOperations.RotateLeft(c, 16);
                c += b;
                b = (b - a) ^ BitOperations.RotateLeft(a, 19);
                a += c;
                c = (c - b) ^ BitOperations.RotateLeft(b, 4);
                b += a;
                address = address[12..];
            }

            a += BinaryPrimitives.ReadUInt32LittleEndian(address);
            c = (c ^ b) - BitOperations.RotateLeft(b, 14);
            a = (a ^ c) - BitOperations.RotateLeft(c, 11);
            b = (b ^ a) - BitOperations.RotateLeft(a, 25);
            c = (c ^ b) - BitOperations.RotateLeft(b, 16);
            a = (a ^ c) - BitOperations.RotateLeft(c, 4);
            b = (b ^ a) - BitOperations.RotateLeft(a, 14);

            return (c ^ b) - BitOperations.RotateLeft(b, 24);
        }
    }
}
