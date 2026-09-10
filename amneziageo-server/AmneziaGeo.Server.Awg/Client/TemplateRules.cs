using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Shapes the settings of a client template have to take.
/// </summary>
public static class TemplateRules
{
    /// <summary>
    /// The longest name a template takes.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Returns why the settings of a template are unusable, or null when they hold.
    /// </summary>
    public static ClientFault? Check(ClientTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return CheckName(template.Name)
            ?? CheckRanges(template.AllowedIps)
            ?? CheckServers(template.Dns)
            ?? CheckMtu(template.Mtu)
            ?? CheckKeepalive(template.Keepalive);
    }

    /// <summary>
    /// Returns why the name of a template is unusable, or null when it holds.
    /// </summary>
    public static ClientFault? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fault("bad-template-name", "the name is empty");
        }

        return name.Length > MaxNameLength
            ? Fault("bad-template-name", $"the name is longer than {MaxNameLength} characters")
            : null;
    }

    private static ClientFault? CheckRanges(IReadOnlyList<string> ranges)
    {
        foreach (var range in ranges)
        {
            if (!AwgAllowedIp.TryParse(range, out _))
            {
                return Fault("bad-allowed", $"'{range}' is not an address range");
            }
        }

        return null;
    }

    private static ClientFault? CheckServers(IReadOnlyList<string> servers)
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

    private static ClientFault? CheckMtu(int? mtu) =>
        mtu is null or >= ConfigRules.MinMtu and <= ConfigRules.MaxMtu
            ? null
            : Fault("bad-mtu", $"the packet size is outside {ConfigRules.MinMtu} to {ConfigRules.MaxMtu}");

    private static ClientFault? CheckKeepalive(int? keepalive) =>
        keepalive is null or >= 0 and <= ConfigRules.MaxKeepalive
            ? null
            : Fault("bad-keepalive", $"the keepalive is outside 0 to {ConfigRules.MaxKeepalive}");

    private static ClientFault Fault(string code, string message) => new(code, message);
}
