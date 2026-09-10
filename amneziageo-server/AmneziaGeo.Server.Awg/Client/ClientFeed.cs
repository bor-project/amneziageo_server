using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// What a subscription hands out: the links of its clients and the traffic they made.
/// </summary>
/// <param name="Links">The vpn:// links of the clients.</param>
/// <param name="Upload">Bytes the clients sent.</param>
/// <param name="Download">Bytes the clients took.</param>
public sealed record ClientFeed(IReadOnlyList<string> Links, ulong Upload, ulong Download)
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
        $"upload={Upload}; download={Download}; total=0; expire=0");

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
        Func<ServerConfig, IReadOnlyList<TunnelClient>, IReadOnlyList<ClientState>> states)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(states);

        var links = new List<string>();
        var upload = 0UL;
        var download = 0UL;
        foreach (var endpoint in endpoints.Where(one => one.IsEnabled))
        {
            var mine = members
                .Where(one => one.ConfigId == endpoint.Id && one.IsEnabled && one.PrivateKey.Length > 0)
                .ToArray();
            if (mine.Length == 0)
            {
                continue;
            }

            var held = states(endpoint, mine);
            for (var index = 0; index < mine.Length; index++)
            {
                var template = mine[index].TemplateId is { } chosen ? templates.GetValueOrDefault(chosen) : null;
                links.Add(ClientLink.Link(endpoint, mine[index], template));
                upload += held[index].RxBytes;
                download += held[index].TxBytes;
            }
        }

        return new ClientFeed(links, upload, download);
    }
}
