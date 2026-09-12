using System.Globalization;

namespace AmneziaGeo.Server.Routing.Firewall;

/// <summary>
/// A rule of ufw the panel keeps.
/// </summary>
/// <param name="Arguments">The rule as ufw takes it, without its comment.</param>
/// <param name="Note">The comment the rule is marked with.</param>
public sealed record FirewallRule(IReadOnlyList<string> Arguments, string Note)
{
    /// <summary>
    /// Tells whether the rule carries what travels through the host.
    /// </summary>
    public bool IsRoute => Arguments.Count > 0 && string.Equals(Arguments[0], Route, StringComparison.Ordinal);

    /// <summary>
    /// Tells whether another rule says the same.
    /// </summary>
    public bool Same(FirewallRule other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal);
    }

    private const string Route = "route";
}

/// <summary>
/// Reads the rules ufw was given and writes the ones the panel keeps.
/// </summary>
public static class UfwRules
{
    /// <summary>
    /// The word a comment of the panel starts with.
    /// </summary>
    public const string Mark = "amneziageo";

    /// <summary>
    /// Returns the rules of the panel among the ones ufw was given.
    /// </summary>
    public static IReadOnlyList<FirewallRule> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var found = new List<FirewallRule>();
        foreach (var line in text.Split('\n'))
        {
            if (Rule(line.Trim()) is { } rule)
            {
                found.Add(rule);
            }
        }

        return found;
    }

    /// <summary>
    /// Returns the rules that let the plan through.
    /// </summary>
    public static IReadOnlyList<FirewallRule> Wanted(FirewallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var rules = new List<FirewallRule>();
        foreach (var port in plan.Ports)
        {
            rules.Add(new FirewallRule(["allow", Point(port)], Note(port.Note)));
        }

        foreach (var name in plan.Interfaces)
        {
            rules.Add(new FirewallRule(["route", "allow", "in", "on", name], Note(name)));
            rules.Add(new FirewallRule(["route", "allow", "out", "on", name], Note(name)));
        }

        return rules;
    }

    /// <summary>
    /// Returns the rules to put into ufw and the ones of the panel to take out of it.
    /// </summary>
    public static (IReadOnlyList<FirewallRule> Put, IReadOnlyList<FirewallRule> Take) Difference(
        IReadOnlyList<FirewallRule> wanted,
        IReadOnlyList<FirewallRule> held)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(held);

        var put = wanted
            .Where(rule => !held.Any(one => one.Same(rule)
                && string.Equals(one.Note, rule.Note, StringComparison.Ordinal)))
            .ToArray();
        var take = held.Where(rule => !wanted.Any(one => one.Same(rule))).ToArray();

        return (put, take);
    }

    /// <summary>
    /// Returns the arguments that put a rule into ufw.
    /// </summary>
    public static IReadOnlyList<string> Add(FirewallRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return [.. rule.Arguments, "comment", rule.Note];
    }

    /// <summary>
    /// Returns the arguments that take a rule out of ufw.
    /// </summary>
    public static IReadOnlyList<string> Drop(FirewallRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.IsRoute ? ["route", "delete", .. rule.Arguments.Skip(1)] : ["delete", .. rule.Arguments];
    }

    private const string Head = "ufw ";

    private const string Comment = " comment ";

    private static FirewallRule? Rule(string line)
    {
        if (!line.StartsWith(Head, StringComparison.Ordinal))
        {
            return null;
        }

        var body = line[Head.Length..];
        var at = body.IndexOf(Comment, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var note = body[(at + Comment.Length)..].Trim().Trim('\'');

        return note.StartsWith(Mark, StringComparison.Ordinal)
            ? new FirewallRule(Words(body[..at]), note)
            : null;
    }

    private static string[] Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Point(FirewallPort port) =>
        string.Create(CultureInfo.InvariantCulture, $"{port.Port}/{port.Protocol}");

    private static string Note(string what) => Mark + " " + what;
}
