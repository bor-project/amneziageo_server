using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// One address range a peer is allowed to carry.
/// </summary>
public sealed record AwgAllowedIp(IPAddress Address, byte Cidr)
{
    /// <summary>
    /// Reads a range written as an address with a prefix length.
    /// </summary>
    public static AwgAllowedIp Parse(string text) =>
        TryParse(text, out var found) ? found : throw new FormatException($"'{text}' is not an address range");

    /// <summary>
    /// Reads a range written as an address with a prefix length, telling whether it held.
    /// </summary>
    public static bool TryParse(string? text, out AwgAllowedIp found)
    {
        found = default!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var body = text.Trim();
        var mark = body.LastIndexOf('/');
        var head = mark < 0 ? body : body[..mark];
        if (!IPAddress.TryParse(head, out var address))
        {
            return false;
        }

        var full = Width(address);
        if (mark < 0)
        {
            found = new AwgAllowedIp(address, full);

            return true;
        }

        if (!byte.TryParse(body[(mark + 1)..], out var cidr) || cidr > full)
        {
            return false;
        }

        found = new AwgAllowedIp(address, cidr);

        return true;
    }

    /// <summary>
    /// Tells whether the range carries addresses of the second family.
    /// </summary>
    public bool IsSix => Address.AddressFamily == AddressFamily.InterNetworkV6;

    /// <summary>
    /// Returns the range with the bits under the prefix cleared.
    /// </summary>
    public AwgAllowedIp Network()
    {
        var bytes = Address.GetAddressBytes();
        for (var at = 0; at < bytes.Length; at++)
        {
            var kept = Cidr - (at * 8);
            bytes[at] = kept switch
            {
                >= 8 => bytes[at],
                <= 0 => 0,
                _ => (byte)(bytes[at] & (0xFF << (8 - kept))),
            };
        }

        return new AwgAllowedIp(new IPAddress(bytes), Cidr);
    }

    /// <summary>
    /// Writes the range as an address with a prefix length.
    /// </summary>
    public override string ToString() => $"{Address}/{Cidr}";

    private static byte Width(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)128 : (byte)32;
}
