using System.Globalization;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// How a connection was decided, as the prefix of its record says.
/// </summary>
/// <param name="Verdict">How the connection was carried.</param>
/// <param name="Rule">The rule that decided, or null when none did.</param>
/// <param name="Guard">The guard of the resolver that dropped the connection, or empty.</param>
public sealed record AccessDecision(string Verdict, long? Rule, string Guard);

/// <summary>
/// Writes and reads the prefixes the records of the panel carry.
/// </summary>
public static class AccessTag
{
    /// <summary>
    /// The head of every prefix of the panel.
    /// </summary>
    public const string Head = "ag:";

    /// <summary>
    /// The prefix of a connection no rule took.
    /// </summary>
    public const string Nothing = "ag:n";

    /// <summary>
    /// The prefix of a connection the guard against DNS over TLS dropped.
    /// </summary>
    public const string Dot = "ag:g:dot";

    /// <summary>
    /// The prefix of a connection the guard against DNS over HTTPS dropped.
    /// </summary>
    public const string Doh = "ag:g:doh";

    /// <summary>
    /// The prefix of a packet that came back to a connection of a client.
    /// </summary>
    public const string Reply = "ag:r";

    /// <summary>
    /// Returns the prefix the records of a rule carry.
    /// </summary>
    public static string Of(RouteLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);

        var letter = leg.IsBlock ? 'b' : leg.IsHeld ? 'h' : leg.IsDirect ? 'd' : 'o';

        return $"{Head}{letter}:{leg.Rule.Id.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Reads how a connection was decided, or null when the prefix is not one of the panel.
    /// </summary>
    public static AccessDecision? Read(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (prefix == Nothing)
        {
            return new AccessDecision(AccessVerdict.Host, null, string.Empty);
        }

        var parts = prefix.Split(':');
        if (parts.Length != 3 || parts[0] != "ag" || parts[1].Length != 1)
        {
            return null;
        }

        if (parts[1] == "g")
        {
            return parts[2] is "dot" or "doh" ? new AccessDecision(AccessVerdict.Guard, null, parts[2]) : null;
        }

        if (!long.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var rule))
        {
            return null;
        }

        var verdict = parts[1] switch
        {
            "o" => AccessVerdict.Out,
            "d" => AccessVerdict.Host,
            "b" => AccessVerdict.Block,
            "h" => AccessVerdict.Held,
            _ => null,
        };

        return verdict is null ? null : new AccessDecision(verdict, rule, string.Empty);
    }

    /// <summary>
    /// Returns the statement that hands a packet to the log under a prefix.
    /// </summary>
    public static string Statement(string prefix, ushort group) =>
        $"log prefix \"{prefix}\" group {group.ToString(CultureInfo.InvariantCulture)}";
}
