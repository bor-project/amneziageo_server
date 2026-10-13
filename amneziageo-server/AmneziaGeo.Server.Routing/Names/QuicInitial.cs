using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// One piece of the handshake an Initial packet of QUIC carries.
/// </summary>
/// <param name="Offset">Where the piece stands in the handshake.</param>
/// <param name="Data">The bytes of the piece.</param>
public sealed record QuicPiece(int Offset, byte[] Data);

/// <summary>
/// The keys a client protects its Initial packets with.
/// </summary>
/// <param name="Key">The key of the packet.</param>
/// <param name="Iv">What the nonce of the packet is made from.</param>
/// <param name="Header">The key of the header.</param>
public sealed record QuicKeys(byte[] Key, byte[] Iv, byte[] Header);

/// <summary>
/// Opens the Initial packets a client starts a QUIC connection with.
/// </summary>
public static class QuicInitial
{
    /// <summary>
    /// The number of QUIC version 1.
    /// </summary>
    public const uint One = 0x00000001;

    /// <summary>
    /// The number of QUIC version 2.
    /// </summary>
    public const uint Two = 0x6b3343cf;

    private const int Tag = 16;

    private const int Sample = 16;

    private static readonly byte[] SaltOne = Convert.FromHexString("38762cf7f55934b34d179ae6a4c80cadccbb7f0a");

    private static readonly byte[] SaltTwo = Convert.FromHexString("0dede3def700a6db819381be6e269dcbf9bd2ed9");

    /// <summary>
    /// Tells whether a datagram starts with a long header, as the first packets of a connection do.
    /// </summary>
    public static bool IsLong(ReadOnlySpan<byte> datagram) => datagram.Length > 5 && (datagram[0] & 0xC0) == 0xC0;

    /// <summary>
    /// Returns the keys of the Initial packets a client sends to a connection of a version, or null for a version
    /// the reader does not know.
    /// </summary>
    public static QuicKeys? Keys(uint version, ReadOnlySpan<byte> connection)
    {
        var (salt, mark) = version switch
        {
            One => (SaltOne, "quic "),
            Two => (SaltTwo, "quicv2 "),
            _ => (null, string.Empty),
        };

        if (salt is null)
        {
            return null;
        }

        var initial = HKDF.Extract(HashAlgorithmName.SHA256, connection.ToArray(), salt);
        var client = Expand(initial, "client in", 32);

        return new QuicKeys(Expand(client, mark + "key", 16), Expand(client, mark + "iv", 12), Expand(client, mark + "hp", 16));
    }

    /// <summary>
    /// Returns the pieces of the handshake the Initial packet at the head of a datagram carries, or null when the
    /// datagram does not start with an Initial packet of a client the reader can open.
    /// </summary>
    public static IReadOnlyList<QuicPiece>? Open(ReadOnlySpan<byte> datagram)
    {
        if (!IsLong(datagram))
        {
            return null;
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(datagram[1..]);
        var kind = (datagram[0] >> 4) & 3;
        if (kind != (version == Two ? 1 : 0))
        {
            return null;
        }

        var at = 5;
        if (!Id(datagram, ref at, out var connection) || !Id(datagram, ref at, out _)
            || !Number(datagram, ref at, out var token) || datagram.Length - at < (int)token)
        {
            return null;
        }

        at += (int)token;
        if (!Number(datagram, ref at, out var length) || length < Tag + 4 || (ulong)(datagram.Length - at) < length
            || datagram.Length - at < 4 + Sample || Keys(version, connection) is not { } keys)
        {
            return null;
        }

        Span<byte> mask = stackalloc byte[16];
        using (var aes = Aes.Create())
        {
            aes.Key = keys.Header;
            aes.EncryptEcb(datagram.Slice(at + 4, Sample), mask, PaddingMode.None);
        }

        var first = (byte)(datagram[0] ^ (mask[0] & 0x0F));
        var width = (first & 3) + 1;
        var head = datagram[..(at + width)].ToArray();
        head[0] = first;
        var number = 0UL;
        for (var one = 0; one < width; one++)
        {
            head[at + one] ^= mask[1 + one];
            number = (number << 8) | head[at + one];
        }

        var nonce = keys.Iv.ToArray();
        for (var one = 0; one < 8; one++)
        {
            nonce[11 - one] ^= (byte)(number >> (8 * one));
        }

        var sealedLength = (int)length - width;
        var plain = new byte[sealedLength - Tag];
        try
        {
            using var gcm = new AesGcm(keys.Key, Tag);
            gcm.Decrypt(
                nonce,
                datagram.Slice(at + width, plain.Length),
                datagram.Slice(at + width + plain.Length, Tag),
                plain,
                head);
        }
        catch (CryptographicException)
        {
            return null;
        }

        return Pieces(plain);
    }

    // Returns the CRYPTO frames of an opened packet; a frame the reader does not know ends the reading.
    private static List<QuicPiece> Pieces(ReadOnlySpan<byte> frames)
    {
        var pieces = new List<QuicPiece>();
        var at = 0;
        while (at < frames.Length)
        {
            var kind = frames[at];
            if (kind is 0 or 1)
            {
                at++;
                continue;
            }

            if (kind != 6)
            {
                break;
            }

            at++;
            if (!Number(frames, ref at, out var offset) || !Number(frames, ref at, out var length)
                || (ulong)(frames.Length - at) < length || offset > TlsHello.Most)
            {
                break;
            }

            pieces.Add(new QuicPiece((int)offset, frames.Slice(at, (int)length).ToArray()));
            at += (int)length;
        }

        return pieces;
    }

    // Reads a connection number that starts with its own length.
    private static bool Id(ReadOnlySpan<byte> datagram, ref int at, out ReadOnlySpan<byte> id)
    {
        id = default;
        if (datagram.Length - at < 1 || datagram[at] > 20 || datagram.Length - at - 1 < datagram[at])
        {
            return false;
        }

        id = datagram.Slice(at + 1, datagram[at]);
        at += 1 + datagram[at];

        return true;
    }

    // Reads a number of one, two, four or eight bytes, the way QUIC writes its lengths.
    private static bool Number(ReadOnlySpan<byte> bytes, ref int at, out ulong value)
    {
        value = 0;
        if (bytes.Length - at < 1)
        {
            return false;
        }

        var width = 1 << (bytes[at] >> 6);
        if (bytes.Length - at < width)
        {
            return false;
        }

        value = (ulong)(bytes[at] & 0x3F);
        for (var one = 1; one < width; one++)
        {
            value = (value << 8) | bytes[at + one];
        }

        at += width;

        return true;
    }

    // Derives a secret under a label, the way TLS 1.3 does.
    private static byte[] Expand(byte[] secret, string label, int length)
    {
        var name = Encoding.ASCII.GetBytes("tls13 " + label);
        var info = new byte[4 + name.Length];
        info[0] = (byte)(length >> 8);
        info[1] = (byte)length;
        info[2] = (byte)name.Length;
        name.CopyTo(info, 3);

        return HKDF.Expand(HashAlgorithmName.SHA256, secret, length, info);
    }
}

/// <summary>
/// Puts the pieces of a handshake together and reads the name once they hold it.
/// </summary>
public sealed class QuicHello
{
    private const int Room = 8192;

    private readonly byte[] _message = new byte[Room];

    private readonly bool[] _filled = new bool[Room];

    private int _whole;

    /// <summary>
    /// Takes the pieces of one more packet and returns what the handshake tells so far.
    /// </summary>
    public SeenName Take(IReadOnlyList<QuicPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);

        foreach (var piece in pieces)
        {
            var room = Math.Min(piece.Data.Length, _message.Length - piece.Offset);
            if (room <= 0)
            {
                continue;
            }

            piece.Data.AsSpan(0, room).CopyTo(_message.AsSpan(piece.Offset));
            _filled.AsSpan(piece.Offset, room).Fill(true);
        }

        while (_whole < _filled.Length && _filled[_whole])
        {
            _whole++;
        }

        return _whole == 0 ? SeenName.More : TlsHello.ReadMessage(_message.AsSpan(0, _whole));
    }
}
