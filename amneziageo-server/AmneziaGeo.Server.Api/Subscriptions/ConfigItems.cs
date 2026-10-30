using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// The configurations a subscription publishes, as the hello names them: what each one is, when it changed, the mark
/// of the file it hands out and the address to fetch it at.
/// </summary>
public static class ConfigItems
{
    /// <summary>
    /// Returns the items of a feed, the address of the subscription naming where the file of each one lies.
    /// </summary>
    public static IReadOnlyList<ConfigItem> Of(IReadOnlyList<ClientSheet> sheets, string address)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(address);

        var root = address.TrimEnd('/');
        var items = new List<ConfigItem>(sheets.Count);
        foreach (var sheet in sheets)
        {
            if (sheet.MemberId <= 0 || sheet.Link.Length == 0)
            {
                continue;
            }

            var id = Id(sheet.MemberId);
            items.Add(new ConfigItem(
                id,
                sheet.Name,
                sheet.Updated,
                Mark(sheet.Link),
                sheet.Default,
                sheet.Required,
                root + "/" + id));
        }

        return items;
    }

    /// <summary>
    /// Returns the mark of a file: the digest of the text its address hands out.
    /// </summary>
    public static string Mark(string link) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(link)));

    /// <summary>
    /// Returns the identifier a configuration is asked by at the address of its subscription.
    /// </summary>
    public static string Id(long member) => member.ToString(CultureInfo.InvariantCulture);
}
