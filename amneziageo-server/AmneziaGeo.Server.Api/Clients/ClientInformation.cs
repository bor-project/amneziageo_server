using System.Net;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Api.Clients;

public static class ClientInformation
{
    public static ClientConfigurationInfo Configuration(
        ServerConfig config,
        TunnelClient client,
        ClientTemplate? template,
        ClientDns dns,
        IReadOnlyList<ClientInfoReference> presets)
    {
        var ranges = ClientText.Routed(config, client, template, dns);
        return new(
            config.Id,
            config.Name,
            config.IsEnabled,
            client.Address,
            client.TemplateId,
            template?.Name,
            ClientText.Endpoint(config),
            dns.Servers,
            dns.From switch
            {
                DnsFrom.Client => "client",
                DnsFrom.Template => "template",
                DnsFrom.StandIns => "stand-ins",
                _ => "server",
            },
            DnsChoiceName.Of(dns.Choice),
            [.. ranges.Take(1000)],
            template is null ? "interface"
                : template.AllowedIps.Count > 0 || dns.From == DnsFrom.StandIns ? "template"
                : config.AllowedIps.Count > 0 ? "interface" : "default",
            ranges.Count,
            ranges.Count > 1000,
            template?.Mtu ?? config.Mtu,
            template?.Mtu is not null ? "template" : "interface",
            template?.Keepalive ?? config.Keepalive,
            template?.Keepalive is not null ? "template" : "interface",
            InboundName.Of(client.Inbound == ClientInbound.Endpoint ? config.Inbound : client.Inbound),
            presets,
            template?.PresetsOnly ?? false,
            template?.ProxyIds ?? [],
            config.RequireUpdate);
    }

    public static ClientLimitsInfo Limits(TunnelClient client, ClientTemplate? template, ClientLimitStatus? status)
    {
        var policy = TrafficLimitPolicy.Resolve(client, template);
        var source = policy is null && client.DailyLimit > 0 ? "client"
            : client.OverrideLimits ? "client" : template?.Limits is not null ? "template" : "none";
        return new ClientLimitsInfo(status, policy, source, source == "template" ? template?.Id : null,
            policy is null ? client.DailyLimit : 0);
    }

    public static string? Reason(TunnelClient client, ServerConfig? config, ClientState state, ClientTraffic traffic) =>
        !client.IsEnabled ? "disabled-client"
        : config is { IsEnabled: false } ? "disabled-interface"
        : traffic.Limits is { Applied: false, Error.Length: > 0 } ? "limits-not-applied"
        : traffic.Limits is { IsBlocked: true } || traffic.IsSpent ? "quota-blocked"
        : traffic.Limits is { IsThrottled: true } ? "quota-throttled"
        : traffic.Limits is { Applied: false } ? "limits-not-applied"
        : !state.IsPresent ? "peer-not-present" : null;

    public static bool Recent(DateTimeOffset? observed, DateTimeOffset now) =>
        observed is { } at && now >= at && now - at <= TimeSpan.FromSeconds(10);

    public static DateTimeOffset BindingSince(TunnelClient client, ServerConfig? config) =>
        new[] { client.CreatedUtc, client.UpdatedUtc, config?.UpdatedUtc ?? DateTimeOffset.MinValue }.Max();

    public static DateTimeOffset PeriodStart(DateTimeOffset? from, DateTimeOffset end) => from
        ?? (end.Ticks >= TimeSpan.TicksPerHour && end.UtcTicks >= TimeSpan.TicksPerHour
            ? end - TimeSpan.FromHours(1) : end);

    public static AccessQuery Access(TunnelClient client, ServerConfig? config, DateTimeOffset from, DateTimeOffset to) => new()
    {
        From = from > BindingSince(client, config) ? from : BindingSince(client, config),
        To = to,
        Inbound = config?.Name ?? string.Empty,
        Sources = config is null ? [] : [.. client.Address
            .Select(value => value.Split('/')[0])
            .Select(value => IPAddress.TryParse(value, out var address) ? address.ToString() : string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)],
    };
}
