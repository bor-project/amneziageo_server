using System.Buffers.Binary;
using System.Security.Cryptography;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace AmneziaGeo.Server.Routing.Udp;

public readonly record struct UdpPacketHeader(int Kind, uint Sender, uint Receiver);

/// <summary>
/// Reads WireGuard and AmneziaWG packet identities.
/// </summary>
public sealed class UdpPacketReader
{
    private readonly byte[] _macKey;
    private readonly byte[] _protection;
    private readonly ObfuscationSettings _settings;

    /// <summary>
    /// ctor
    /// </summary>
    public UdpPacketReader(ServerConfig config)
    {
        _settings = config.Obfuscation;
        _protection = _settings.HeaderProtectionKey.Length == 0 ? [] : Convert.FromBase64String(_settings.HeaderProtectionKey);
        var publicKey = Convert.FromBase64String(Curve25519.PublicOf(config.PrivateKey));
        _macKey = Digest("mac1----"u8.ToArray().Concat(publicKey).ToArray(), null, 32);
    }

    /// <summary>
    /// Validates a handshake addressed to this interface.
    /// </summary>
    public bool Handshake(ReadOnlySpan<byte> packet)
    {
        return Handshake(packet, true) || Handshake(packet, false);
    }

    /// <summary>
    /// Reads the receiver index of a transport packet.
    /// </summary>
    public uint? Receiver(ReadOnlySpan<byte> packet)
    {
        return Header(packet, false) is { Kind: 3 or 4 } custom ? custom.Receiver
            : Header(packet, true) is { Kind: 3 or 4 } plain ? plain.Receiver : null;
    }

    /// <summary>
    /// Reads sender and receiver indices.
    /// </summary>
    public UdpPacketHeader? Header(ReadOnlySpan<byte> packet, bool plain = false)
    {
        Span<byte> mask = stackalloc byte[16];
        mask.Clear();
        if (!plain && _protection.Length > 0)
        {
            if (packet.Length < 12)
            {
                return null;
            }

            var cipher = new ChaCha7539Engine();
            cipher.Init(false, new ParametersWithIV(new KeyParameter(_protection), packet[..12].ToArray()));
            for (var at = 0; at < mask.Length; at++)
            {
                mask[at] = cipher.ReturnByte(0);
            }
        }

        foreach (var kind in new[] { 1, 2, 3, 4 })
        {
            var padding = plain ? 0 : kind switch { 1 => _settings.S1, 2 => _settings.S2, 3 => _settings.S3, _ => _settings.S4 };
            var size = kind switch { 1 => 148, 2 => 92, 3 => 64, _ => 32 };
            if (packet.Length < padding + size || kind != 4 && !plain && !_settings.RandomTrailers && packet.Length != padding + size
                || kind != 4 && plain && packet.Length != size)
            {
                continue;
            }

            if (!plain && _protection.Length > 0 && padding < 12)
            {
                continue;
            }

            var head = packet[padding..];
            var value = BinaryPrimitives.ReadUInt32LittleEndian(head) ^ BinaryPrimitives.ReadUInt32LittleEndian(mask);
            var text = kind switch { 1 => _settings.H1, 2 => _settings.H2, 3 => _settings.H3, _ => _settings.H4 };
            var range = !plain && AwgRange.TryParse(text, out var custom) && !custom.IsZero ? custom : new AwgRange((uint)kind);
            if (value < range.Low || value > range.High)
            {
                continue;
            }

            var first = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]) ^ BinaryPrimitives.ReadUInt32LittleEndian(mask[4..]);
            return new UdpPacketHeader(kind, kind is 1 or 2 ? first : 0,
                kind == 2 ? BinaryPrimitives.ReadUInt32LittleEndian(head[8..]) ^ BinaryPrimitives.ReadUInt32LittleEndian(mask[8..]) : kind is 3 or 4 ? first : 0);
        }

        return null;
    }

    private bool Handshake(ReadOnlySpan<byte> packet, bool plain)
    {
        var header = Header(packet, plain);
        if (header is not { Kind: 1 or 2 })
        {
            return false;
        }

        var size = header.Value.Kind == 1 ? 148 : 92;
        var padding = plain ? 0 : header.Value.Kind == 1 ? _settings.S1 : _settings.S2;
        var message = Open(packet, padding, size, plain)!;
        var mac = Digest(message[..(size - 32)], _macKey, 16);
        return CryptographicOperations.FixedTimeEquals(mac, message.AsSpan(size - 32, 16));
    }

    private byte[]? Open(ReadOnlySpan<byte> packet, int padding, int size, bool plain)
    {
        if (padding < 0 || packet.Length < padding + size || !plain && _protection.Length > 0 && padding < 12)
        {
            return null;
        }

        var message = packet.Slice(padding, size).ToArray();
        if (!plain && _protection.Length > 0)
        {
            var cipher = new ChaCha7539Engine();
            cipher.Init(false, new ParametersWithIV(new KeyParameter(_protection), packet[..12].ToArray()));
            cipher.ProcessBytes(message, 0, message.Length, message, 0);
        }

        return message;
    }

    private static byte[] Digest(byte[] message, byte[]? key, int size)
    {
        var digest = new Blake2sDigest(key, size, null, null);
        digest.BlockUpdate(message, 0, message.Length);
        var result = new byte[size];
        digest.DoFinal(result, 0);
        return result;
    }
}
