using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Carries a new name of an outbound, a balancer, an endpoint or a client into what names it, and takes the name of a
/// removed client or endpoint off the rules.
/// </summary>
internal static class NameFollow
{
    private const long ResolverRow = 1;

    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Carries a new name of an outbound or a balancer into the rules, the balancers and the resolver.
    /// </summary>
    public static async Task WayAsync(
        AppDbContext db,
        string old,
        string anew,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.Equals(old, anew, StringComparison.Ordinal))
        {
            return;
        }

        var rules = await db.Rules.Where(rule => rule.Outbound == old).ToListAsync(ct).ConfigureAwait(false);
        foreach (var rule in rules)
        {
            rule.Outbound = anew;
            rule.UpdatedUtc = now;
        }

        var balancers = await db.Balancers.ToListAsync(ct).ConfigureAwait(false);
        foreach (var balancer in balancers.Where(balancer => Names(balancer.Members, old, StringComparer.Ordinal)))
        {
            balancer.Members = Swap(balancer.Members, old, anew, StringComparer.Ordinal);
            balancer.UpdatedUtc = now;
        }

        var resolver = await db.Resolver.FirstOrDefaultAsync(row => row.Id == ResolverRow, ct).ConfigureAwait(false);
        if (resolver is not null && resolver.Outbound == old)
        {
            resolver.Outbound = anew;
            resolver.UpdatedUtc = now;
        }
    }

    /// <summary>
    /// Carries a new name of an endpoint into the rules that match by the interfaces.
    /// </summary>
    public static async Task InboundAsync(
        AppDbContext db,
        string old,
        string anew,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.Equals(old, anew, StringComparison.Ordinal))
        {
            return;
        }

        var rules = await db.Rules.Where(rule => rule.Inbounds != string.Empty).ToListAsync(ct).ConfigureAwait(false);
        foreach (var rule in rules.Where(rule => Names(rule.Inbounds, old, StringComparer.Ordinal)))
        {
            rule.Inbounds = Swap(rule.Inbounds, old, anew, StringComparer.Ordinal);
            rule.UpdatedUtc = now;
        }
    }

    /// <summary>
    /// Carries a new name of a client into the rules that match by the clients.
    /// </summary>
    public static async Task ClientAsync(
        AppDbContext db,
        string old,
        string anew,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.Equals(old, anew, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var rules = await db.Rules.Where(rule => rule.Clients != string.Empty).ToListAsync(ct).ConfigureAwait(false);
        foreach (var rule in rules.Where(rule => Names(rule.Clients, old, StringComparer.OrdinalIgnoreCase)))
        {
            rule.Clients = Swap(rule.Clients, old, anew, StringComparer.OrdinalIgnoreCase);
            rule.UpdatedUtc = now;
        }
    }

    /// <summary>
    /// Tells whether the resolver asks through the outbound or the balancer under a name.
    /// </summary>
    public static Task<bool> AsksThroughAsync(AppDbContext db, string name, CancellationToken ct) =>
        db.Resolver.AnyAsync(row => row.Id == ResolverRow && row.Outbound == name, ct);

    /// <summary>
    /// Tells whether a list the database holds carries a name.
    /// </summary>
    public static bool Names(string list, string name, StringComparer comparer) => Parts(list).Contains(name, comparer);

    /// <summary>
    /// Takes the names of removed clients or endpoints off the rules, turning off a rule that named none but them.
    /// </summary>
    public static async Task GoneAsync(
        AppDbContext db,
        RuleSide side,
        IReadOnlyCollection<string> names,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (names.Count == 0)
        {
            return;
        }

        var comparer = side.Comparer();
        var rules = await db.Rules.ToListAsync(ct).ConfigureAwait(false);
        foreach (var rule in rules)
        {
            var held = Parts(side == RuleSide.Clients ? rule.Clients : rule.Inbounds).ToList();
            var left = held.Where(one => !names.Contains(one, comparer)).ToList();
            if (left.Count == held.Count)
            {
                continue;
            }

            if (left.Count == 0)
            {
                rule.IsEnabled = false;
            }
            else if (side == RuleSide.Clients)
            {
                rule.Clients = string.Join(", ", left);
            }
            else
            {
                rule.Inbounds = string.Join(", ", left);
            }

            rule.UpdatedUtc = now;
        }
    }

    private static string Swap(string list, string old, string anew, StringComparer comparer) =>
        string.Join(", ", Parts(list).Select(name => comparer.Equals(name, old) ? anew : name).Distinct(comparer));

    private static IEnumerable<string> Parts(string text) =>
        text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
