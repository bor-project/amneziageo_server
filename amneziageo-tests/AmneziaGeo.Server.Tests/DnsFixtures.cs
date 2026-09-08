using System.Buffers.Binary;
using System.Net;
using System.Text;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// One record of a built answer.
/// </summary>
public sealed record Told(string Owner, DnsRecordType Type, uint Ttl, string Value);

/// <summary>
/// Builds the name server messages the tests read.
/// </summary>
public static class DnsBuilder
{
    /// <summary>
    /// Returns a question about a name.
    /// </summary>
    public static byte[] Question(ushort id, string name, DnsRecordType type = DnsRecordType.A)
    {
        var text = new List<byte>();
        Head(text, id, 0x0100, 1, 0);
        Ask(text, name, type);

        return [.. text];
    }

    /// <summary>
    /// Returns an answer to a question about a name.
    /// </summary>
    public static byte[] Answer(ushort id, string name, DnsRecordType type, params Told[] records)
    {
        var text = new List<byte>();
        Head(text, id, 0x8180, 1, records.Length);
        Ask(text, name, type);
        foreach (var record in records)
        {
            if (string.Equals(record.Owner, name, StringComparison.OrdinalIgnoreCase))
            {
                text.Add(0xC0);
                text.Add(0x0C);
            }
            else
            {
                Name(text, record.Owner);
            }

            Number(text, (ushort)record.Type);
            Number(text, 1);
            text.AddRange(Four(record.Ttl));
            var data = Data(record);
            Number(text, (ushort)data.Length);
            text.AddRange(data);
        }

        return [.. text];
    }

    /// <summary>
    /// Returns a message whose name points at itself.
    /// </summary>
    public static byte[] Looping()
    {
        var text = new List<byte>();
        Head(text, 7, 0x0100, 1, 0);
        text.Add(0xC0);
        text.Add(0x0C);
        Number(text, 1);
        Number(text, 1);

        return [.. text];
    }

    private static void Head(List<byte> text, ushort id, ushort flags, int questions, int answers)
    {
        Number(text, id);
        Number(text, flags);
        Number(text, (ushort)questions);
        Number(text, (ushort)answers);
        Number(text, 0);
        Number(text, 0);
    }

    private static void Ask(List<byte> text, string name, DnsRecordType type)
    {
        Name(text, name);
        Number(text, (ushort)type);
        Number(text, 1);
    }

    private static void Name(List<byte> text, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            text.Add((byte)label.Length);
            text.AddRange(Encoding.ASCII.GetBytes(label));
        }

        text.Add(0);
    }

    private static byte[] Data(Told record)
    {
        if (record.Type is DnsRecordType.A or DnsRecordType.Aaaa)
        {
            return IPAddress.Parse(record.Value).GetAddressBytes();
        }

        var text = new List<byte>();
        Name(text, record.Value);

        return [.. text];
    }

    private static void Number(List<byte> text, ushort value)
    {
        var pair = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(pair, value);
        text.AddRange(pair);
    }

    private static byte[] Four(uint value)
    {
        var four = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(four, value);

        return four;
    }
}
