using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Routing.Carrier;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Probe;

namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// Shapes the settings of an outbound have to take.
/// </summary>
public static class OutboundRules
{
    /// <summary>
    /// The lowest mark an outbound takes.
    /// </summary>
    public const uint FirstMark = 0xA601;

    /// <summary>
    /// The highest mark an outbound takes.
    /// </summary>
    public const uint LastMark = 0xA6FF;

    /// <summary>
    /// The routing table the lowest mark looks the way out up in.
    /// </summary>
    public const int FirstTable = 42601;

    /// <summary>
    /// The routing table the host itself picks the way out of.
    /// </summary>
    public const int MainTable = 254;

    /// <summary>
    /// The place in the rule list the lowest mark takes.
    /// </summary>
    public const int FirstPriority = 10000;

    /// <summary>
    /// Returns why the settings of an outbound are unusable, or null when they hold.
    /// </summary>
    public static OutboundFault? Check(OutboundConfig outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        return CheckName(outbound.Name)
            ?? CheckKind(outbound.Kind)
            ?? CheckMark(outbound.Mark)
            ?? CheckTable(outbound)
            ?? CheckProbe(outbound)
            ?? (OutboundKind.HasLink(outbound.Kind) ? CheckTunnel(outbound) : CheckPlain(outbound));
    }

    /// <summary>
    /// Returns why the name of an outbound is unusable, or null when it holds.
    /// </summary>
    public static OutboundFault? CheckName(string? name) =>
        ConfigRules.CheckName(name) is { } fault ? new OutboundFault(fault.Code, fault.Message) : null;

    /// <summary>
    /// Returns why the kind of an outbound is unusable, or null when it holds.
    /// </summary>
    public static OutboundFault? CheckKind(string? kind) =>
        OutboundKind.Known(kind) ? null : Fault("bad-kind", $"'{kind}' is not a kind an outbound takes");

    /// <summary>
    /// Returns why the probe of an outbound is unusable, or null when it holds.
    /// </summary>
    public static OutboundFault? CheckProbe(OutboundConfig outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        if (outbound.Probe.Length == 0)
        {
            return null;
        }

        if (!DnsRules.Upstream(outbound.Probe, out _))
        {
            return Fault("bad-probe", $"'{outbound.Probe}' is not an address of a name server");
        }

        return outbound.ProbeEvery is >= ProbeDefaults.MinEvery and <= ProbeDefaults.MaxEvery
            ? null
            : Fault("bad-probe", "the probe goes out no oftener than every " + ProbeDefaults.MinEvery + " seconds");
    }

    /// <summary>
    /// Returns the routing table an outbound of a kind looks the way out up in.
    /// </summary>
    public static int TableOf(uint mark, string kind) =>
        OutboundKind.HasLink(kind) ? FirstTable + (int)(mark - FirstMark) : MainTable;

    /// <summary>
    /// Returns the place a mark takes in the rule list.
    /// </summary>
    public static int PriorityOf(uint mark) => FirstPriority + (int)(mark - FirstMark);

    private static OutboundFault? CheckTable(OutboundConfig outbound) =>
        outbound.Table == TableOf(outbound.Mark, outbound.Kind)
            ? null
            : Fault("bad-table", "the routing table does not go with the mark");

    private static OutboundFault? CheckMark(uint mark) =>
        mark is >= FirstMark and <= LastMark
            ? null
            : Fault("bad-mark", $"the mark is outside {FirstMark} to {LastMark}");

    /// <summary>
    /// Returns why the proxy of an outbound is unusable, or null when it holds.
    /// </summary>
    public static OutboundFault? CheckProxy(string? proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy))
        {
            return Fault("bad-proxy", "the address of the websocket proxy is empty");
        }

        var parsed = WsEndpoint.Parse(proxy, 0, string.Empty);
        if (parsed.Host.Length == 0)
        {
            return Fault("bad-proxy", $"'{proxy}' is not an address of a websocket proxy");
        }

        return ConfigRules.CheckHost(parsed.Host) is { } fault
            ? new OutboundFault("bad-proxy", fault.Message)
            : CheckProxyPort(parsed.Port);
    }

    private static OutboundFault? CheckProxyPort(int port) =>
        port is > 0 and <= 65535 ? null : Fault("bad-proxy", "the port of the websocket proxy is outside 1 to 65535");

    private static OutboundFault? CheckPlain(OutboundConfig outbound) =>
        outbound.Host.Length > 0 || outbound.Port > 0 || outbound.PrivateKey.Length > 0 || outbound.Proxy.Length > 0
            ? Fault("bad-kind", "an outbound that leaves through the host itself carries no server")
            : CheckServers(outbound.Dns);

    private static OutboundFault? CheckTunnel(OutboundConfig outbound) =>
        (OutboundKind.HasProxy(outbound.Kind) ? CheckProxy(outbound.Proxy) : CheckNoProxy(outbound.Proxy))
        ?? CheckHost(outbound.Host)
        ?? CheckPort(outbound.Port)
        ?? CheckKey(outbound.PrivateKey, "bad-key", "the private key")
        ?? CheckKey(outbound.PeerKey, "bad-peer-key", "the public key of the server")
        ?? CheckPreshared(outbound.PresharedKey)
        ?? CheckRanges(outbound.Address)
        ?? CheckServers(outbound.Dns)
        ?? CheckMtu(outbound.Mtu)
        ?? CheckKeepalive(outbound.Keepalive)
        ?? CheckObfuscation(outbound.Obfuscation);

    private static OutboundFault? CheckNoProxy(string? proxy) =>
        string.IsNullOrEmpty(proxy)
            ? null
            : Fault("bad-proxy", "only an outbound of the websocket kind carries a proxy");

    private static OutboundFault? CheckHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Fault("bad-host", "the address of the server is empty");
        }

        return ConfigRules.CheckHost(host) is { } fault ? new OutboundFault(fault.Code, fault.Message) : null;
    }

    private static OutboundFault? CheckPort(int port) =>
        port is > 0 and <= 65535 ? null : Fault("bad-port", "the port is outside 1 to 65535");

    private static OutboundFault? CheckKey(string? key, string code, string owner) =>
        Curve25519.IsKey(key) ? null : Fault(code, $"{owner} is not 32 bytes in base64");

    private static OutboundFault? CheckPreshared(string? key) =>
        string.IsNullOrEmpty(key) || Curve25519.IsKey(key)
            ? null
            : Fault("bad-preshared", "the preshared key is not 32 bytes in base64");

    private static OutboundFault? CheckRanges(IReadOnlyList<string> ranges)
    {
        if (ranges.Count == 0)
        {
            return Fault("bad-address", "the interface carries no address range");
        }

        foreach (var range in ranges)
        {
            if (!AwgAllowedIp.TryParse(range, out _))
            {
                return Fault("bad-address", $"'{range}' is not an address range");
            }
        }

        return null;
    }

    private static OutboundFault? CheckServers(IReadOnlyList<string> servers)
    {
        foreach (var server in servers)
        {
            if (!IPAddress.TryParse(server, out _))
            {
                return Fault("bad-dns", $"'{server}' is not a name server address");
            }
        }

        return null;
    }

    private static OutboundFault? CheckMtu(int mtu) =>
        mtu == 0 || mtu is >= ConfigRules.MinMtu and <= ConfigRules.MaxMtu
            ? null
            : Fault("bad-mtu", $"the packet size is outside {ConfigRules.MinMtu} to {ConfigRules.MaxMtu}");

    private static OutboundFault? CheckKeepalive(int keepalive) =>
        keepalive is >= 0 and <= ConfigRules.MaxKeepalive
            ? null
            : Fault("bad-keepalive", $"the keepalive is outside 0 to {ConfigRules.MaxKeepalive}");

    private static OutboundFault? CheckObfuscation(ObfuscationSettings obfuscation)
    {
        if (IsPlain(obfuscation))
        {
            return null;
        }

        return ConfigRules.CheckObfuscation(obfuscation) is { } fault
            ? new OutboundFault(fault.Code, fault.Message)
            : null;
    }

    /// <summary>
    /// Tells whether a tunnel carries no obfuscation at all.
    /// </summary>
    public static bool IsPlain(ObfuscationSettings obfuscation) =>
        obfuscation.Jc == 0
        && obfuscation.Jmin == 0
        && obfuscation.Jmax == 0
        && obfuscation.S1 == 0
        && obfuscation.S2 == 0
        && obfuscation.S3 == 0
        && obfuscation.S4 == 0
        && string.IsNullOrEmpty(obfuscation.H1)
        && string.IsNullOrEmpty(obfuscation.H2)
        && string.IsNullOrEmpty(obfuscation.H3)
        && string.IsNullOrEmpty(obfuscation.H4)
        && string.IsNullOrEmpty(obfuscation.I1)
        && string.IsNullOrEmpty(obfuscation.I2)
        && string.IsNullOrEmpty(obfuscation.I3)
        && string.IsNullOrEmpty(obfuscation.I4)
        && string.IsNullOrEmpty(obfuscation.I5)
        && obfuscation.HeaderProtectionKey.Length == 0;

    private static OutboundFault Fault(string code, string message) => new(code, message);
}
