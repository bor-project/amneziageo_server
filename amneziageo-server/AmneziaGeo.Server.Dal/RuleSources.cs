using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// What became of a rule whose client addresses went over to the clients standing at them.
/// </summary>
/// <param name="Rule">The name of the rule.</param>
/// <param name="Sources">The client addresses the rule matched by.</param>
/// <param name="Added">The clients standing at those addresses, named by the rule now.</param>
/// <param name="Clients">Every client the rule names now.</param>
/// <param name="WasEnabled">Whether the rule was on.</param>
/// <param name="IsEnabled">Whether the rule is on now.</param>
/// <param name="Standing">How many clients stand at those addresses.</param>
public sealed record RuleSourcesMove(
    string Rule,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Clients,
    bool WasEnabled,
    bool IsEnabled,
    int Standing)
{
    /// <summary>
    /// Tells whether more clients stand at the addresses than a rule names, so none were added.
    /// </summary>
    public bool IsTooMany => Added.Count == 0 && Clients.Count + Standing > RouteRules.MaxConditions;
}

/// <summary>
/// Moves the client addresses the migration set aside over to the clients standing at them.
/// </summary>
public static class RuleSources
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Moves the client addresses set aside over to the clients, turns off a rule left without clients and drops the table
    /// they were set aside in.
    /// </summary>
    public static async Task<IReadOnlyList<RuleSourcesMove>> MoveAsync(AppDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var held = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'RuleSourcesLeft'")
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (held is not [> 0])
        {
            return [];
        }

        var left = await db.Database
            .SqlQueryRaw<SourceRow>("SELECT Id, Sources FROM RuleSourcesLeft")
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var sources = left.ToDictionary(row => row.Id, row => Parts(row.Sources));
        var ids = sources.Keys.ToArray();
        var rules = await db.Rules
            .Where(rule => ids.Contains(rule.Id))
            .OrderBy(rule => rule.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var standing = await db.Clients
            .AsNoTracking()
            .OrderBy(client => client.Id)
            .Select(client => new { client.Name, client.Address })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var clients = standing.Select(client => (client.Name, Parts(client.Address))).ToArray();
        var moves = new List<RuleSourcesMove>();
        foreach (var rule in rules)
        {
            var move = Move(rule.Name, sources[rule.Id], Parts(rule.Clients), rule.IsEnabled, clients);
            rule.Clients = string.Join(", ", move.Clients);
            rule.IsEnabled = move.IsEnabled;
            moves.Add(move);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE RuleSourcesLeft", ct).ConfigureAwait(false);

        return moves;
    }

    /// <summary>
    /// Returns what a rule becomes once its client addresses went over to the clients standing at them.
    /// </summary>
    public static RuleSourcesMove Move(
        string rule,
        IReadOnlyList<string> sources,
        IReadOnlyList<string> named,
        bool on,
        IReadOnlyList<(string Name, IReadOnlyList<string> Address)> clients)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(named);
        ArgumentNullException.ThrowIfNull(clients);

        var added = clients
            .Where(client => !named.Contains(client.Name, StringComparer.OrdinalIgnoreCase))
            .Where(client => client.Address.Any(address => Stands(sources, address)))
            .Select(client => client.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (named.Count + added.Length > RouteRules.MaxConditions)
        {
            return new RuleSourcesMove(rule, sources, [], named, on, false, added.Length);
        }

        var all = named.Concat(added).ToArray();

        return new RuleSourcesMove(rule, sources, added, all, on, on && all.Length > 0, added.Length);
    }

    private static bool Stands(IReadOnlyList<string> sources, string address) =>
        AwgAllowedIp.TryParse(address, out var found) && RouteProbe.Within(sources, found.Address);

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private sealed class SourceRow
    {
        public long Id { get; set; }

        public string Sources { get; set; } = string.Empty;
    }
}
