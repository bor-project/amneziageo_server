using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Awg.Config;

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
    /// Returns what a subscription hands out of its clients that are on and hold a private key at endpoints that are on,
    /// counting a client together with its devices once.
    /// </summary>
    public static ClientFeed Of(
        IReadOnlyList<ServerConfig> endpoints,
        IReadOnlyList<TunnelClient> members,
        IReadOnlyDictionary<long, ClientTemplate> templates,
        Func<TunnelClient, ClientUsage> used,
        int helloPort = 0,
        Func<ServerConfig, IReadOnlyList<string>>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(used);

        var links = new List<string>();
        var groups = new Dictionary<long, Share>();
        foreach (var endpoint in endpoints.Where(one => one.IsEnabled))
        {
            foreach (var member in members.Where(one => one.ConfigId == endpoint.Id && one.IsEnabled && one.PrivateKey.Length > 0))
            {
                var template = member.TemplateId is { } chosen ? templates.GetValueOrDefault(chosen) : null;
                links.Add(ClientLink.Link(endpoint, member, template, helloPort, resolver?.Invoke(endpoint)));
                groups[member.ParentId ?? member.Id] = new Share(used(member), member.DailyLimit);
            }
        }

        var shares = groups.Values;
        var total = shares.Any(one => one.Limit <= 0) ? 0UL : shares.Aggregate(0UL, (sum, one) => sum + (ulong)one.Limit);

        return new ClientFeed(
            links,
            shares.Aggregate(0UL, (sum, one) => sum + one.Used.Rx),
            shares.Aggregate(0UL, (sum, one) => sum + one.Used.Tx),
            total);
    }

    private sealed record Share(ClientUsage Used, long Limit);
}
