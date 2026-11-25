using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Routing.Traffic;

/// <summary>
/// The bandwidth ceilings of one client.
/// </summary>
public sealed record ClientBandwidth(
    long ClientId,
    IReadOnlyList<string> Addresses,
    long UploadBitsPerSecond,
    long DownloadBitsPerSecond);

/// <summary>
/// The result of applying bandwidth ceilings.
/// </summary>
public sealed record BandwidthSync(bool IsDone, string Message);

internal sealed record BandwidthClass(int Id, long BytesPerSecond)
{
    public string Handle => $"a804:{Id:x}";
}

internal sealed record BandwidthFilter(int Handle, string Protocol, string Prefix, int ClassId)
{
    public string Preference => Protocol == "ip" ? "100" : "101";

    public string Tag => $"0x{Handle:x}";

    public string Class => $"a804:{ClassId:x}";
}

internal sealed record BandwidthDirection(
    IReadOnlyList<BandwidthClass> Classes,
    IReadOnlyList<BandwidthFilter> Filters)
{
    public bool IsLimited => Classes.Count > 0;
}

internal sealed record BandwidthPlan(
    string Interface,
    string Ifb,
    string Owner,
    string Fingerprint,
    BandwidthDirection Upload,
    BandwidthDirection Download)
{
    public bool IsLimited => Upload.IsLimited || Download.IsLimited;

    public static BandwidthPlan Create(string name, IReadOnlyList<ClientBandwidth> clients)
    {
        if (ConfigRules.CheckName(name) is not null)
        {
            throw new ArgumentException("the interface name is invalid", nameof(name));
        }

        if (clients.Count > 32767)
        {
            throw new ArgumentException("an interface carries more than 32767 bandwidth clients", nameof(clients));
        }

        var ids = new HashSet<long>();
        var addresses = new List<(long Client, AwgAllowedIp Range)>();
        foreach (var client in clients.OrderBy(one => one.ClientId))
        {
            if (client.ClientId <= 0 || !ids.Add(client.ClientId))
            {
                throw new ArgumentException("the bandwidth clients carry an invalid or repeated identity", nameof(clients));
            }

            if (client.UploadBitsPerSecond < 0 || client.DownloadBitsPerSecond < 0
                || client.UploadBitsPerSecond is > 0 and < 8 || client.DownloadBitsPerSecond is > 0 and < 8)
            {
                throw new ArgumentException("a bandwidth ceiling must be zero or at least 8 bits per second", nameof(clients));
            }

            if (client.Addresses is null || client.Addresses.Count == 0)
            {
                throw new ArgumentException("a bandwidth client carries no addresses", nameof(clients));
            }

            foreach (var text in client.Addresses)
            {
                if (!AwgAllowedIp.TryParse(text, out var range)
                    || range.Address.IsIPv4MappedToIPv6
                    || (range.IsSix && range.Address.ScopeId != 0))
                {
                    throw new ArgumentException("a bandwidth client carries an invalid address", nameof(clients));
                }

                range = range.Network();
                if (addresses.Any(one => one.Client != client.ClientId && Overlap(one.Range, range)))
                {
                    throw new ArgumentException("the addresses of bandwidth clients overlap", nameof(clients));
                }

                if (!addresses.Any(one => one.Client == client.ClientId && one.Range == range))
                {
                    addresses.Add((client.ClientId, range));
                }
            }
        }

        var upload = Direction(clients, addresses, true);
        var download = Direction(clients, addresses, false);
        var owner = $"amneziageo:bandwidth:v1:{name}:";
        var identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12];
        var body = string.Join("\n", clients.OrderBy(one => one.ClientId).Select(one =>
            $"{one.ClientId.ToString(CultureInfo.InvariantCulture)}:{one.UploadBitsPerSecond.ToString(CultureInfo.InvariantCulture)}:"
            + $"{one.DownloadBitsPerSecond.ToString(CultureInfo.InvariantCulture)}:"
            + string.Join(",", addresses.Where(address => address.Client == one.ClientId)
                .Select(address => address.Range.ToString()).Order(StringComparer.Ordinal))));
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        return new BandwidthPlan(name, "agb" + identity, owner, fingerprint, upload, download);
    }

    private static BandwidthDirection Direction(
        IReadOnlyList<ClientBandwidth> clients,
        IReadOnlyList<(long Client, AwgAllowedIp Range)> addresses,
        bool upload)
    {
        var classes = new List<BandwidthClass>();
        var filters = new List<BandwidthFilter>();
        var at = 0;
        foreach (var client in clients.OrderBy(one => one.ClientId))
        {
            at++;
            var bits = upload ? client.UploadBitsPerSecond : client.DownloadBitsPerSecond;
            if (bits == 0)
            {
                continue;
            }

            classes.Add(new BandwidthClass(at, bits / 8));
            foreach (var range in addresses.Where(one => one.Client == client.ClientId)
                .Select(one => one.Range).OrderBy(one => one.ToString(), StringComparer.Ordinal))
            {
                filters.Add(new BandwidthFilter(filters.Count + 1, range.IsSix ? "ipv6" : "ip", range.ToString(), at));
            }
        }

        return new BandwidthDirection(classes, filters);
    }

    private static bool Overlap(AwgAllowedIp left, AwgAllowedIp right)
    {
        if (left.Address.AddressFamily != right.Address.AddressFamily)
        {
            return false;
        }

        var prefix = Math.Min(left.Cidr, right.Cidr);
        return new AwgAllowedIp(left.Address, prefix).Network().Address
            .Equals(new AwgAllowedIp(right.Address, prefix).Network().Address);
    }
}
