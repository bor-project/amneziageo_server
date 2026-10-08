using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// One record out of an answer.
/// </summary>
/// <param name="Name">The name the record belongs to.</param>
/// <param name="Type">The kind of the record.</param>
/// <param name="Ttl">How long the record stays good, in seconds.</param>
/// <param name="Address">The address the record carries, or null.</param>
public sealed record DnsRecord(string Name, DnsRecordType Type, uint Ttl, IPAddress? Address);

/// <summary>
/// A name server message as the resolver reads it.
/// </summary>
/// <param name="Id">The number the asking side marked the message with.</param>
/// <param name="IsResponse">Whether the message answers a question.</param>
/// <param name="Code">The outcome the answering side reported.</param>
/// <param name="Question">The name that was asked about.</param>
/// <param name="Type">The kind of record that was asked for.</param>
/// <param name="Answers">The records the message carries.</param>
public sealed record DnsMessage(
    ushort Id,
    bool IsResponse,
    int Code,
    string Question,
    DnsRecordType Type,
    IReadOnlyList<DnsRecord> Answers)
{
    /// <summary>
    /// The length of the message header.
    /// </summary>
    public const int HeaderLength = 12;

    /// <summary>
    /// The longest name a message may carry.
    /// </summary>
    public const int MaxNameLength = 255;

    /// <summary>
    /// The code that says the answering side failed.
    /// </summary>
    public const int ServerFailure = 2;

    /// <summary>
    /// The largest answer a datagram carries to an asking side that named no size of its own.
    /// </summary>
    public const int DatagramLength = 512;

    /// <summary>
    /// The largest answer a datagram carries whatever size the asking side named.
    /// </summary>
    public const int MaxDatagramLength = 4096;

    private const int MaxJumps = 16;

    private const int SizeRecord = 41;

    /// <summary>
    /// The shortest time the records of the message stay good, in seconds.
    /// </summary>
    public uint Lifetime => Answers.Count == 0 ? 0 : Answers.Min(record => record.Ttl);

    /// <summary>
    /// The addresses the message answered with.
    /// </summary>
    public IEnumerable<IPAddress> Addresses => Answers.Select(record => record.Address).OfType<IPAddress>();

    /// <summary>
    /// Reads a message, returning null when the bytes are not one.
    /// </summary>
    public static DnsMessage? Read(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderLength)
        {
            return null;
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        var questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..]);
        var count = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]);
        var at = HeaderLength;
        var question = string.Empty;
        var type = DnsRecordType.Unknown;
        for (var i = 0; i < questions; i++)
        {
            if (!Name(packet, ref at, out var asked) || at + 4 > packet.Length)
            {
                return null;
            }

            if (i == 0)
            {
                question = asked;
                type = (DnsRecordType)BinaryPrimitives.ReadUInt16BigEndian(packet[at..]);
            }

            at += 4;
        }

        return new DnsMessage(
            BinaryPrimitives.ReadUInt16BigEndian(packet),
            (flags & 0x8000) != 0,
            flags & 0x000F,
            question,
            type,
            Records(packet, ref at, count));
    }

    /// <summary>
    /// Returns the message that tells the asking side the question was not answered.
    /// </summary>
    public static byte[] Refusal(ReadOnlySpan<byte> request, int code)
    {
        var reply = request.Length < HeaderLength ? new byte[HeaderLength] : request.ToArray();
        var flags = BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2));
        flags = (ushort)((flags & 0x7900) | 0x8080 | (code & 0x000F));
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), flags);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(8), 0);

        return reply;
    }

    /// <summary>
    /// Marks a message with the number the asking side used.
    /// </summary>
    public static void Stamp(Span<byte> packet, ushort id)
    {
        if (packet.Length >= HeaderLength)
        {
            BinaryPrimitives.WriteUInt16BigEndian(packet, id);
        }
    }

    /// <summary>
    /// Returns the answer when a datagram to the asking side carries it whole, and otherwise the answer cut down to
    /// its question and marked as cut, so the asking side comes again over a stream.
    /// </summary>
    public static byte[] Fit(ReadOnlySpan<byte> question, byte[] answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (answer.Length < HeaderLength || answer.Length <= Room(question) || !Asked(answer, out var end))
        {
            return answer;
        }

        var cut = answer[..end];
        cut[2] |= 0x02;
        BinaryPrimitives.WriteUInt16BigEndian(cut.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt16BigEndian(cut.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16BigEndian(cut.AsSpan(10), 0);

        return cut;
    }

    // Returns how many bytes a datagram to the asking side may carry: the size its question names, within the bounds.
    private static int Room(ReadOnlySpan<byte> question)
    {
        if (question.Length < HeaderLength || !Asked(question, out var at))
        {
            return DatagramLength;
        }

        var records = BinaryPrimitives.ReadUInt16BigEndian(question[6..]) + BinaryPrimitives.ReadUInt16BigEndian(question[8..])
            + BinaryPrimitives.ReadUInt16BigEndian(question[10..]);
        for (var i = 0; i < records; i++)
        {
            if (!Name(question, ref at, out _) || at + 10 > question.Length)
            {
                return DatagramLength;
            }

            if (BinaryPrimitives.ReadUInt16BigEndian(question[at..]) == SizeRecord)
            {
                return Math.Clamp((int)BinaryPrimitives.ReadUInt16BigEndian(question[(at + 2)..]), DatagramLength, MaxDatagramLength);
            }

            at += 10 + BinaryPrimitives.ReadUInt16BigEndian(question[(at + 8)..]);
        }

        return DatagramLength;
    }

    // Finds where the questions of a message end.
    private static bool Asked(ReadOnlySpan<byte> packet, out int end)
    {
        end = HeaderLength;
        var questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..]);
        for (var i = 0; i < questions; i++)
        {
            if (!Name(packet, ref end, out _) || end + 4 > packet.Length)
            {
                return false;
            }

            end += 4;
        }

        return true;
    }

    private static IReadOnlyList<DnsRecord> Records(ReadOnlySpan<byte> packet, ref int at, int count)
    {
        var found = new List<DnsRecord>(count);
        for (var i = 0; i < count; i++)
        {
            if (!Name(packet, ref at, out var owner) || at + 10 > packet.Length)
            {
                break;
            }

            var type = (DnsRecordType)BinaryPrimitives.ReadUInt16BigEndian(packet[at..]);
            var ttl = BinaryPrimitives.ReadUInt32BigEndian(packet[(at + 4)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(packet[(at + 8)..]);
            at += 10;
            if (at + length > packet.Length)
            {
                break;
            }

            found.Add(new DnsRecord(owner, type, ttl, Address(type, packet.Slice(at, length))));
            at += length;
        }

        return found;
    }

    private static IPAddress? Address(DnsRecordType type, ReadOnlySpan<byte> data) =>
        (type, data.Length) switch
        {
            (DnsRecordType.A, 4) => new IPAddress(data),
            (DnsRecordType.Aaaa, 16) => new IPAddress(data),
            _ => null,
        };

    private static bool Name(ReadOnlySpan<byte> packet, ref int at, out string name)
    {
        var text = new StringBuilder();
        var cursor = at;
        var jumps = 0;
        var followed = false;
        name = string.Empty;
        while (true)
        {
            if (cursor >= packet.Length || text.Length > MaxNameLength)
            {
                return false;
            }

            var length = packet[cursor];
            if (length == 0)
            {
                if (!followed)
                {
                    at = cursor + 1;
                }

                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= packet.Length || ++jumps > MaxJumps)
                {
                    return false;
                }

                var target = ((length & 0x3F) << 8) | packet[cursor + 1];
                if (!followed)
                {
                    at = cursor + 2;
                    followed = true;
                }

                cursor = target;
                continue;
            }

            if ((length & 0xC0) != 0 || cursor + 1 + length > packet.Length)
            {
                return false;
            }

            if (text.Length > 0)
            {
                text.Append('.');
            }

            text.Append(Encoding.ASCII.GetString(packet.Slice(cursor + 1, length)));
            cursor += 1 + length;
        }

        name = text.ToString();

        return true;
    }
}
