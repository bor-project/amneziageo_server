using System.Globalization;
using System.Text.Json;

namespace AmneziaGeo.Server.Routing.Traffic;

internal static class BandwidthClasses
{
    public static JsonElement[] Read(string text, IReadOnlySet<string> leaves)
    {
        if (text.TrimStart().StartsWith('['))
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.EnumerateArray()
                .Where(one => !Internal(one.TryGetProperty("kind", out var kind) ? kind.GetString() ?? string.Empty : string.Empty,
                    one.TryGetProperty("parent", out var parent) ? parent.GetString() ?? string.Empty : string.Empty, leaves))
                .Select(one => one.Clone()).ToArray();
        }

        var classes = new List<JsonElement>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 4 || words[0] != "class")
            {
                throw new InvalidOperationException("the traffic classes returned an invalid description");
            }

            var parent = Array.IndexOf(words, "parent");
            if (Internal(words[1], parent >= 0 && parent + 1 < words.Length ? words[parent + 1] : string.Empty, leaves))
            {
                continue;
            }

            var rate = Array.IndexOf(words, "rate");
            var ceil = Array.IndexOf(words, "ceil");
            if (words[1] != "htb" || !words[2].StartsWith("a804:", StringComparison.Ordinal)
                || rate < 0 || rate + 1 >= words.Length || ceil < 0 || ceil + 1 >= words.Length)
            {
                throw new InvalidOperationException("the interface carries foreign traffic classes");
            }

            classes.Add(JsonSerializer.SerializeToElement(new
            {
                kind = "htb",
                handle = words[2],
                legacy = true,
                options = new { rate = Rate(words[rate + 1]), ceil = Rate(words[ceil + 1]) },
            }));
        }

        return classes.ToArray();
    }

    private static bool Internal(string kind, string parent, IReadOnlySet<string> leaves) =>
        kind == "fq_codel" && leaves.Contains(parent);

    private static long Rate(string text)
    {
        var at = 0;
        while (at < text.Length && (char.IsAsciiDigit(text[at]) || text[at] == '.'))
        {
            at++;
        }

        if (!decimal.TryParse(text.AsSpan(0, at), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidOperationException("a traffic class returned an invalid rate");
        }

        var factor = text[at..] switch
        {
            "bit" => 1m,
            "Kbit" => 1000m,
            "Mbit" => 1000000m,
            "Gbit" => 1000000000m,
            "Tbit" => 1000000000000m,
            "bps" => 8m,
            "Kbps" => 8000m,
            "Mbps" => 8000000m,
            "Gbps" => 8000000000m,
            _ => throw new InvalidOperationException("a traffic class returned an unknown rate unit"),
        };
        if (number < 0 || number > long.MaxValue * 8m / factor)
        {
            throw new InvalidOperationException("a traffic class returned an invalid rate");
        }

        return (long)(number * factor / 8);
    }
}
