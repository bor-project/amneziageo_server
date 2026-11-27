using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Dal.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20261127153000_RoutingRows")]
public sealed class RoutingRows : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE _RoutingRowsGuard (Value INTEGER NOT NULL CHECK(Value = 0));
            INSERT INTO _RoutingRowsGuard
            SELECT COUNT(*) FROM TrafficRules WHERE DefaultKey = 'route-fallback';

            CREATE TEMP TABLE _RoutingRowsFallback AS
            SELECT e.Id AS EntryId, t.Id AS RuleId
            FROM RoutingEntries e
            JOIN RoutingEntryRules m ON m.EntryId = e.Id
            JOIN TrafficRules t ON t.Id = m.RuleId
            WHERE e.Id = (SELECT Id FROM RoutingEntries ORDER BY Position DESC, Id DESC LIMIT 1)
              AND e.IsEnabled = 1 AND e.Protocol = 'any'
              AND e.Action IN ('direct', 'block', 'out')
              AND (e.Action <> 'out' OR trim(e.Outbound) <> '')
              AND trim(e.Clients || e.Inbounds || e.Ports || e.SourcePorts, ' ,' || char(9) || char(10) || char(13)) = ''
              AND (SELECT COUNT(*) FROM RoutingEntryRules one WHERE one.EntryId = e.Id) = 1
              AND t.DefaultKey = 'resolver-default'
              AND trim(t.Targets, ' ,' || char(9) || char(10) || char(13)) = '';

            CREATE TEMP TABLE _RoutingRowsSplit AS
            WITH ordered AS (
                SELECT e.Id AS EntryId, m.RuleId,
                       ROW_NUMBER() OVER (PARTITION BY e.Id ORDER BY m.Position, m.RuleId) AS Part,
                       ROW_NUMBER() OVER (ORDER BY e.Position, e.Id, m.Position, m.RuleId) AS Place
                FROM RoutingEntries e
                LEFT JOIN RoutingEntryRules m ON m.EntryId = e.Id
            ), ids AS (
                SELECT MAX(COALESCE(MAX(Id), 0),
                           COALESCE((SELECT seq FROM sqlite_sequence WHERE name = 'RoutingEntries'), 0),
                           (SELECT COALESCE(MAX(RuleId), 0) FROM Standings)) AS LastId
                FROM RoutingEntries
            )
            SELECT o.EntryId, o.RuleId, o.Part, o.Place,
                   CASE WHEN o.Part = 1 THEN o.EntryId ELSE ids.LastId + o.Place END AS NewId
            FROM ordered o CROSS JOIN ids;

            CREATE UNIQUE INDEX _RoutingRowsSplit_Place ON _RoutingRowsSplit(Place);
            CREATE UNIQUE INDEX _RoutingRowsSplit_EntryPart ON _RoutingRowsSplit(EntryId, Part);

            CREATE TEMP TABLE _RoutingRowsNames AS
            WITH RECURSIVE names(Place, Prefix, Stem, Try, Suffix) AS (
                SELECT p.Place, e.Name, p.EntryId || ':' || p.Part, 0, ' (' || p.EntryId || ':' || p.Part || ')'
                FROM _RoutingRowsSplit p JOIN RoutingEntries e ON e.Id = p.EntryId
                WHERE p.Part > 1
                UNION ALL
                SELECT Place, Prefix, Stem, Try + 1, ' (' || Stem || ':' || (Try + 1) || ')'
                FROM names
                WHERE EXISTS (
                    SELECT 1 FROM RoutingEntries
                    WHERE Name = trim(substr(Prefix, 1, MAX(0, (32 - length(Suffix)) / 2)) || Suffix) COLLATE NOCASE
                )
            )
            SELECT Place, trim(substr(Prefix, 1, MAX(0, (32 - length(Suffix)) / 2)) || Suffix) AS Name
            FROM names
            WHERE NOT EXISTS (
                SELECT 1 FROM RoutingEntries
                WHERE Name = trim(substr(Prefix, 1, MAX(0, (32 - length(Suffix)) / 2)) || Suffix) COLLATE NOCASE
            );
            CREATE UNIQUE INDEX _RoutingRowsNames_Place ON _RoutingRowsNames(Place);

            UPDATE RoutingEntries
            SET Position = (SELECT p.Place FROM _RoutingRowsSplit p WHERE p.EntryId = RoutingEntries.Id AND p.Part = 1);

            INSERT INTO RoutingEntries (Id, Name, Position, IsEnabled, Action, Outbound, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc)
            SELECT p.NewId, n.Name, p.Place, e.IsEnabled, e.Action, e.Outbound, e.Clients, e.Inbounds, e.Ports, e.SourcePorts, e.Protocol, e.CreatedUtc, e.UpdatedUtc
            FROM _RoutingRowsSplit p
            JOIN _RoutingRowsNames n ON n.Place = p.Place
            JOIN RoutingEntries e ON e.Id = p.EntryId
            WHERE p.Part > 1
            ORDER BY p.Place;

            INSERT INTO Standings (RuleId, Address, SeenUtc)
            SELECT p.NewId, h.Address, h.SeenUtc
            FROM _RoutingRowsSplit p
            JOIN Standings h ON h.RuleId = p.EntryId
            WHERE p.Part > 1;

            DELETE FROM RoutingEntryRules
            WHERE RuleId IN (
                SELECT p.RuleId FROM _RoutingRowsSplit p WHERE p.EntryId = RoutingEntryRules.EntryId AND p.Part > 1
            );
            UPDATE RoutingEntryRules SET Position = 0 WHERE Position <> 0;
            INSERT INTO RoutingEntryRules (EntryId, RuleId, Position)
            SELECT NewId, RuleId, 0 FROM _RoutingRowsSplit WHERE Part > 1;

            UPDATE RoutingEntries SET Position = 2147483647
            WHERE Id = (SELECT EntryId FROM _RoutingRowsFallback);

            UPDATE TrafficRules
            SET DefaultKey = 'route-fallback'
            WHERE Id = (SELECT RuleId FROM _RoutingRowsFallback)
              AND (SELECT COUNT(*) FROM RoutingEntryRules WHERE RuleId = TrafficRules.Id) = 1;

            INSERT INTO TrafficRules (DefaultKey, Name, Targets, DnsMode, Dns, DnsMembers, CreatedUtc, UpdatedUtc)
            SELECT 'route-fallback',
                   (WITH RECURSIVE names(N, Candidate) AS (
                       SELECT 0, 'Default route'
                       UNION ALL SELECT N + 1, 'Default route ' || (N + 1) FROM names
                       WHERE EXISTS (SELECT 1 FROM TrafficRules WHERE Name = Candidate COLLATE NOCASE)
                   ) SELECT Candidate FROM names
                   WHERE NOT EXISTS (SELECT 1 FROM TrafficRules WHERE Name = Candidate COLLATE NOCASE) LIMIT 1),
                   '', COALESCE(t.DnsMode, 'default'), COALESCE(t.Dns, ''), COALESCE(t.DnsMembers, '{}'),
                   COALESCE(t.CreatedUtc, '2026-11-27 15:30:00+00:00'),
                   COALESCE(t.UpdatedUtc, '2026-11-27 15:30:00+00:00')
            FROM (SELECT 1) anchor
            LEFT JOIN TrafficRules t ON t.Id = (SELECT RuleId FROM _RoutingRowsFallback)
            WHERE NOT EXISTS (SELECT 1 FROM TrafficRules WHERE DefaultKey = 'route-fallback');

            UPDATE RoutingEntryRules
            SET RuleId = (SELECT Id FROM TrafficRules WHERE DefaultKey = 'route-fallback')
            WHERE EntryId = (SELECT EntryId FROM _RoutingRowsFallback)
              AND RuleId <> (SELECT Id FROM TrafficRules WHERE DefaultKey = 'route-fallback');

            INSERT INTO RoutingEntries (Name, Position, IsEnabled, Action, Outbound, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc)
            SELECT (WITH RECURSIVE names(N, Candidate) AS (
                       SELECT 0, 'Default route'
                       UNION ALL SELECT N + 1, 'Default route ' || (N + 1) FROM names
                       WHERE EXISTS (SELECT 1 FROM RoutingEntries WHERE Name = Candidate COLLATE NOCASE)
                   ) SELECT Candidate FROM names
                   WHERE NOT EXISTS (SELECT 1 FROM RoutingEntries WHERE Name = Candidate COLLATE NOCASE) LIMIT 1),
                   2147483647,
                   1, 'direct', '', '', '', '', '', 'any', '2026-11-27 15:30:00+00:00', '2026-11-27 15:30:00+00:00'
            WHERE NOT EXISTS (SELECT 1 FROM _RoutingRowsFallback);
            INSERT INTO RoutingEntryRules (EntryId, RuleId, Position)
            SELECT (SELECT Id FROM RoutingEntries ORDER BY Position DESC, Id DESC LIMIT 1), Id, 0
            FROM TrafficRules
            WHERE DefaultKey = 'route-fallback' AND NOT EXISTS (SELECT 1 FROM _RoutingRowsFallback);

            DROP TABLE _RoutingRowsNames;
            DROP TABLE _RoutingRowsSplit;
            DROP TABLE _RoutingRowsFallback;
            DROP TABLE _RoutingRowsGuard;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("restore the pre-upgrade database backup to return to the previous routing rows");
    }
}
