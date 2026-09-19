using System.Globalization;
using System.Net;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Asks a name server about one name.
/// </summary>
public static class DnsLookup
{
    private static readonly DnsRecordType[] Kinds = [DnsRecordType.A, DnsRecordType.Aaaa];

    /// <summary>
    /// Returns the addresses a name answered with, none when nobody answered.
    /// </summary>
    public static async Task<IReadOnlyList<IPAddress>> AskAsync(IDnsUpstream upstream, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(name);

        if (Ascii(name) is not { } ascii)
        {
            return [];
        }

        var found = new List<IPAddress>();
        foreach (var kind in Kinds)
        {
            var answer = await upstream.AskAsync(DnsQuestion.Packet(ascii, kind, DnsQuestion.Id()), false, ct)
                .ConfigureAwait(false);
            if (answer is not null && DnsMessage.Read(answer) is { Code: 0 } message)
            {
                found.AddRange(message.Answers
                    .Where(record => record.Type == kind)
                    .Select(record => record.Address)
                    .OfType<IPAddress>());
            }
        }

        return found;
    }

    private static string? Ascii(string name)
    {
        try
        {
            return new IdnMapping().GetAscii(name.Trim('.'));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
