using System.Buffers.Binary;
using System.Net;
using System.Numerics;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// Picks the mark a connection the panel carries itself leaves with, the way the firewall picks it.
/// </summary>
public sealed class NameMarks
{
    private const uint Start = 0xDEADBEEF;

    private long _turn;

    /// <summary>
    /// Returns the mark of the outbound a client leaves through by a way out, 0 when the way out carries none.
    /// </summary>
    public uint Pick(RouteExit exit, IPAddress client)
    {
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(client);

        if (!exit.IsSpread)
        {
            return exit.Mark;
        }

        var count = (uint)exit.Marks.Count;
        var at = exit.Strategy == BalanceStrategy.Sticky
            ? Scale(Hash(client.GetAddressBytes(), exit.Seed), count)
            : (uint)((ulong)Interlocked.Increment(ref _turn) % count);

        return exit.Marks[(int)at];
    }

    /// <summary>
    /// Returns the hash the kernel gives bytes under a seed.
    /// </summary>
    public static uint Hash(ReadOnlySpan<byte> key, uint seed)
    {
        var a = Start + (uint)key.Length + seed;
        var b = a;
        var c = a;
        while (key.Length > 12)
        {
            a += BinaryPrimitives.ReadUInt32LittleEndian(key);
            b += BinaryPrimitives.ReadUInt32LittleEndian(key[4..]);
            c += BinaryPrimitives.ReadUInt32LittleEndian(key[8..]);
            a -= c;
            a ^= BitOperations.RotateLeft(c, 4);
            c += b;
            b -= a;
            b ^= BitOperations.RotateLeft(a, 6);
            a += c;
            c -= b;
            c ^= BitOperations.RotateLeft(b, 8);
            b += a;
            a -= c;
            a ^= BitOperations.RotateLeft(c, 16);
            c += b;
            b -= a;
            b ^= BitOperations.RotateLeft(a, 19);
            a += c;
            c -= b;
            c ^= BitOperations.RotateLeft(b, 4);
            b += a;
            key = key[12..];
        }

        if (key.Length == 0)
        {
            return c;
        }

        Span<byte> last = stackalloc byte[12];
        key.CopyTo(last);
        a += BinaryPrimitives.ReadUInt32LittleEndian(last);
        b += BinaryPrimitives.ReadUInt32LittleEndian(last[4..]);
        c += BinaryPrimitives.ReadUInt32LittleEndian(last[8..]);
        c ^= b;
        c -= BitOperations.RotateLeft(b, 14);
        a ^= c;
        a -= BitOperations.RotateLeft(c, 11);
        b ^= a;
        b -= BitOperations.RotateLeft(a, 25);
        c ^= b;
        c -= BitOperations.RotateLeft(b, 16);
        a ^= c;
        a -= BitOperations.RotateLeft(c, 4);
        b ^= a;
        b -= BitOperations.RotateLeft(a, 14);
        c ^= b;
        c -= BitOperations.RotateLeft(b, 24);

        return c;
    }

    // Brings a hash into a count of choices the way the kernel does.
    private static uint Scale(uint hash, uint count) => (uint)(((ulong)hash * count) >> 32);
}
