using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// What a subscription hands out: the links of its clients and the traffic they made today.
/// </summary>
/// <param name="Links">The vpn:// links of the clients.</param>
/// <param name="Upload">Bytes the clients sent today.</param>
/// <param name="Download">Bytes the clients took today.</param>
/// <param name="Total">The daily limits of the clients added up, 0 when one of them has none.</param>
public sealed record ClientFeed(IReadOnlyList<string> Links, ulong Upload, ulong Download, ulong Total)
{
    /// <summary>
    /// The configurations behind the links, as the page of the subscription shows them.
    /// </summary>
    public IReadOnlyList<ClientSheet> Sheets { get; init; } = [];

    /// <summary>
    /// Returns the body a subscription answers with, the links line by line in base64.
    /// </summary>
    public string Body => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', Links)));

    /// <summary>
    /// Returns the traffic of the clients as the Subscription-Userinfo header carries it.
    /// </summary>
    public string Usage => string.Create(
        CultureInfo.InvariantCulture,
        $"upload={Upload}; download={Download}; total={Total}; expire=0");

    /// <summary>
    /// Returns the name of a subscription as the Profile-Title header carries it.
    /// </summary>
    public static string Title(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        return "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
    }

    /// <summary>
    /// Returns what a subscription hands out of its clients that are on and hold a private key at endpoints that are on.
    /// </summary>
    public static ClientFeed Of(
        IReadOnlyList<ServerConfig> endpoints,
        IReadOnlyList<TunnelClient> members,
        IReadOnlyDictionary<long, ClientTemplate> templates,
        Func<TunnelClient, ClientUsage> used,
        Func<ServerConfig, IReadOnlyList<string>>? resolver = null,
        Func<ServerConfig, TunnelClient, string>? title = null,
        Func<ServerConfig, IReadOnlyList<string>>? panel = null,
        DnsFiles? files = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(used);

        var links = new List<string>();
        var sheets = new List<ClientSheet>();
        var shares = new List<Share>();
        foreach (var endpoint in endpoints.Where(one => one.IsEnabled))
        {
            foreach (var member in members.Where(one => one.ConfigId == endpoint.Id && one.IsEnabled && one.PrivateKey.Length > 0))
            {
                var template = member.TemplateId is { } chosen ? templates.GetValueOrDefault(chosen) : null;
                var names = resolver?.Invoke(endpoint);
                var picked = panel?.Invoke(endpoint);
                var name = title?.Invoke(endpoint, member) ?? ClientText.Title(endpoint, member);
                var link = ClientLink.Link(endpoint, member, template, names, name, picked, files);
                links.Add(link);
                sheets.Add(new ClientSheet(
                    name,
                    endpoint.Name,
                    ClientText.Endpoint(endpoint),
                    $"{ConfigName.File(name)}.conf",
                    ClientText.Text(endpoint, member, template, names, picked, files),
                    link,
                    member.Id,
                    endpoint.Id,
                    endpoint.UpdatedUtc,
                    endpoint.IsDefault,
                    endpoint.RequireUpdate));
                shares.Add(new Share(used(member), member.DailyLimit));
            }
        }

        var total = shares.Any(one => one.Limit <= 0) ? 0UL : shares.Aggregate(0UL, (sum, one) => sum + (ulong)one.Limit);

        return new ClientFeed(
            links,
            shares.Aggregate(0UL, (sum, one) => sum + one.Used.Rx),
            shares.Aggregate(0UL, (sum, one) => sum + one.Used.Tx),
            total)
        {
            Sheets = sheets,
        };
    }

    private sealed record Share(ClientUsage Used, long Limit);
}
