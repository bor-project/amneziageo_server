using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Creates a QUIC Initial signature.
/// </summary>
public static class QuicSignature
{
    private const int PacketLength = 1200;
    private const string ServerName = "ya.ru";

    /// <summary>
    /// Returns a signature with its own connection identifiers and ClientHello.
    /// </summary>
    public static string Create()
    {
        var destination = RandomNumberGenerator.GetBytes(8);
        var source = RandomNumberGenerator.GetBytes(8);
        var hello = ClientHello(source);
        var crypto = Join([6, 0], Variable(hello.Length), hello);
        var prefix = Join([0xc1, 0, 0, 0, 1, 8], destination, [8], source, [0]);
        var payloadLength = PacketLength - prefix.Length - 2 - 2 - 16;
        var header = Join(prefix, Variable(payloadLength + 18), [0, 0]);
        var plaintext = new byte[payloadLength];
        crypto.CopyTo(plaintext, 0);
        var salt = Convert.FromHexString("38762cf7f55934b34d179ae6a4c80cadccbb7f0a");
        var initial = HMACSHA256.HashData(salt, destination);
        var secret = Expand(initial, "client in", 32);
        var key = Expand(secret, "quic key", 16);
        var nonce = Expand(secret, "quic iv", 12);
        var protection = Expand(secret, "quic hp", 16);
        var ciphertext = new byte[payloadLength];
        var tag = new byte[16];
        using var cipher = new AesGcm(key, tag.Length);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, header);
        var packet = Join(header, ciphertext, tag);
        using var aes = Aes.Create();
        aes.Key = protection;
        var numberOffset = header.Length - 2;
        var mask = aes.EncryptEcb(packet.AsSpan(numberOffset + 4, 16), PaddingMode.None);
        packet[0] ^= (byte)(mask[0] & 15);
        packet[numberOffset] ^= mask[1];
        packet[numberOffset + 1] ^= mask[2];
        var kept = header.Length + crypto.Length;
        return "<b 0x" + Convert.ToHexStringLower(packet.AsSpan(0, kept)) + "><r " + (PacketLength - kept) + ">";
    }

    private static byte[] ClientHello(byte[] source)
    {
        using var exchange = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = exchange.ExportParameters(false).Q;
        var publicKey = Join([4], point.X!, point.Y!);
        var name = Encoding.ASCII.GetBytes(ServerName);
        var parameters = Join(Parameter(1, Variable(30000)), Parameter(3, Variable(1472)),
            Parameter(4, Variable(1048576)), Parameter(8, Variable(16)), Parameter(9, Variable(16)),
            Parameter(14, Variable(2)), Parameter(15, source));
        var extensions = Join(
            Extension(0, Vector(Join([0], Vector(name)))),
            Extension(10, Vector([0, 23])),
            Extension(13, Vector([4, 3, 8, 4, 8, 5, 8, 6])),
            Extension(16, Vector([2, (byte)'h', (byte)'3'])),
            Extension(43, [2, 3, 4]),
            Extension(51, Vector(Join([0, 23], Vector(publicKey)))),
            Extension(57, parameters));
        var body = Join([3, 3], RandomNumberGenerator.GetBytes(32), [0],
            Vector([0x13, 1, 0x13, 2, 0x13, 3]), [1, 0], Vector(extensions));
        return Join([1, (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length], body);
    }

    private static byte[] Expand(byte[] secret, string label, int length)
    {
        var name = Encoding.ASCII.GetBytes("tls13 " + label);
        return HKDF.Expand(HashAlgorithmName.SHA256, secret, length,
            Join(Short(length), [(byte)name.Length], name, [0]));
    }

    private static byte[] Parameter(int kind, byte[] value) => Join(Variable(kind), Variable(value.Length), value);

    private static byte[] Extension(int kind, byte[] value) => Join(Short(kind), Vector(value));

    private static byte[] Vector(byte[] value) => Join(Short(value.Length), value);

    private static byte[] Short(int value) => [(byte)(value >> 8), (byte)value];

    private static byte[] Variable(int value) => value switch
    {
        < 64 => [(byte)value],
        < 16384 => [(byte)(0x40 | (value >> 8)), (byte)value],
        _ => [(byte)(0x80 | (value >> 24)), (byte)(value >> 16), (byte)(value >> 8), (byte)value],
    };

    private static byte[] Join(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
