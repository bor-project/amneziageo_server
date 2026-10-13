using System.Buffers.Binary;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// Reads the name a client asks for in the first message of TLS.
/// </summary>
public static class TlsHello
{
    /// <summary>
    /// The most bytes of a first message the reader looks through.
    /// </summary>
    public const int Most = 16384;

    private const byte Handshake = 0x16;

    private const byte ClientHello = 1;

    private const int RecordHead = 5;

    private const int MessageHead = 4;

    private const ushort ServerName = 0;

    /// <summary>
    /// Reads the name out of what a client sent first over TCP: one or more records of TLS.
    /// </summary>
    public static SeenName Read(ReadOnlySpan<byte> stream)
    {
        if (stream.Length == 0)
        {
            return SeenName.More;
        }

        if (stream[0] != Handshake || (stream.Length > 1 && stream[1] != 3))
        {
            return SeenName.None;
        }

        var message = new byte[Math.Min(stream.Length, Most)];
        var held = 0;
        var at = 0;
        while (stream.Length - at >= RecordHead && held < message.Length)
        {
            if (stream[at] != Handshake || stream[at + 1] != 3)
            {
                return SeenName.None;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(stream[(at + 3)..]);
            if (length == 0 || length > Most + 256)
            {
                return SeenName.None;
            }

            var there = Math.Min(Math.Min(length, stream.Length - at - RecordHead), message.Length - held);
            stream.Slice(at + RecordHead, there).CopyTo(message.AsSpan(held));
            held += there;
            at += RecordHead + length;
        }

        return held == 0 ? SeenName.More : ReadMessage(message.AsSpan(0, held));
    }

    /// <summary>
    /// Reads the name out of the first message of TLS as it goes without records, the way QUIC carries it.
    /// </summary>
    public static SeenName ReadMessage(ReadOnlySpan<byte> message)
    {
        if (message.Length < MessageHead)
        {
            return message.Length > 0 && message[0] != ClientHello ? SeenName.None : SeenName.More;
        }

        if (message[0] != ClientHello)
        {
            return SeenName.None;
        }

        var whole = MessageHead + ((message[1] << 16) | (message[2] << 8) | message[3]);
        var isWhole = message.Length >= whole || message.Length >= Most;
        var body = message[MessageHead..Math.Min(message.Length, whole)];
        var ending = isWhole ? SeenName.None : SeenName.More;

        var at = 34;
        if (!Skip(body, ref at, 1) || !Skip(body, ref at, 2) || !Skip(body, ref at, 1) || body.Length - at < 2)
        {
            return ending;
        }

        var end = Math.Min(body.Length, at + 2 + BinaryPrimitives.ReadUInt16BigEndian(body[at..]));
        at += 2;
        while (end - at >= 4)
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(body[at..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(body[(at + 2)..]);
            at += 4;
            if (type == ServerName)
            {
                return end - at < length ? ending : Name(body.Slice(at, length));
            }

            at += length;
        }

        return ending;
    }

    // Steps over a field that starts with its own length, telling whether the bytes hold it whole.
    private static bool Skip(ReadOnlySpan<byte> body, ref int at, int width)
    {
        if (body.Length - at < width)
        {
            return false;
        }

        var length = width == 1 ? body[at] : BinaryPrimitives.ReadUInt16BigEndian(body[at..]);
        at += width + length;

        return at <= body.Length;
    }

    // Returns the host name out of the list of names the extension carries.
    private static SeenName Name(ReadOnlySpan<byte> list)
    {
        var at = 2;
        while (list.Length - at >= 3)
        {
            var kind = list[at];
            var length = BinaryPrimitives.ReadUInt16BigEndian(list[(at + 1)..]);
            at += 3;
            if (list.Length - at < length)
            {
                return SeenName.None;
            }

            if (kind == 0)
            {
                return SeenName.Of(list.Slice(at, length));
            }

            at += length;
        }

        return SeenName.None;
    }
}
