using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmneziaGeo.Server.Dal.Migrations;

/// <inheritdoc />
public partial class RoutingModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE _RoutingGuard (Value INTEGER NOT NULL CHECK(Value = 0));
            INSERT INTO _RoutingGuard
            SELECT COUNT(*) FROM Rules r, json_each('[' || r.Groups || ']') j
            WHERE CAST(j.value AS INTEGER) <= 0
               OR NOT EXISTS (SELECT 1 FROM AddressGroups g WHERE g.Id = CAST(j.value AS INTEGER))
               OR EXISTS (SELECT 1 FROM AddressGroups g WHERE g.Id = CAST(j.value AS INTEGER) AND trim(g.Targets, ' ,' || char(9) || char(10) || char(13)) = '');

            CREATE TEMP TABLE _RoutingDns AS
            SELECT COALESCE((SELECT Selection FROM Resolver WHERE Id = 1), 'global') AS Selection,
                   COALESCE((SELECT Outbound FROM Resolver WHERE Id = 1), '') AS Outbound,
                   COALESCE((SELECT Upstreams FROM Resolver WHERE Id = 1), 'https://1.1.1.1/dns-query,https://8.8.4.4/dns-query') AS Upstreams,
                   CASE WHEN NOT EXISTS (SELECT 1 FROM Resolver)
                         AND NOT EXISTS (SELECT 1 FROM Outbounds)
                         AND NOT EXISTS (SELECT 1 FROM Balancers)
                         AND NOT EXISTS (SELECT 1 FROM Rules)
                         AND NOT EXISTS (SELECT 1 FROM RouteBasics)
                         AND NOT EXISTS (SELECT 1 FROM AddressGroups)
                         AND NOT EXISTS (SELECT 1 FROM Configs)
                         AND NOT EXISTS (SELECT 1 FROM Clients)
                        THEN 1 ELSE 0 END AS IsFresh;

            CREATE TEMP TABLE _RoutingMembers AS
            SELECT b.Name AS Balancer, j.key AS Position, j.value AS Member
            FROM Balancers b, json_each('["' || replace(replace(replace(replace(replace(b.Members, char(9), ''), char(10), ','), char(13), ''), ' ', ''), ',', '","') || '"]') j
            WHERE j.value <> '';

            CREATE TEMP TABLE _RoutingOutboundDns AS
            WITH RECURSIVE tokens(Name, Rest, Value, Position) AS (
                SELECT Name, replace(replace(replace(replace(Dns, char(9), ','), char(10), ','), char(13), ','), ' ', ',') || ',', '', 0 FROM Outbounds
                UNION ALL
                SELECT Name, substr(Rest, instr(Rest, ',') + 1), substr(Rest, 1, instr(Rest, ',') - 1), Position + 1 FROM tokens WHERE Rest <> ''
            )
            SELECT o.Name, o.Dns, COALESCE((SELECT json_group_array(Value) FROM (SELECT Value FROM tokens WHERE Name = o.Name AND Value <> '' ORDER BY Position)), '[]') AS Servers
            FROM Outbounds o;

            CREATE TEMP TABLE _RoutingEntries AS SELECT * FROM Rules;
            INSERT INTO _RoutingEntries (Id, Name, Position, IsEnabled, Action, Outbound, Targets, Groups, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc)
            SELECT -1, (WITH RECURSIVE names(N, Candidate) AS (
                        SELECT 0, 'basic-block'
                        UNION ALL SELECT N + 1, 'basic-block-' || (N + 1) FROM names
                        WHERE EXISTS (SELECT 1 FROM _RoutingEntries WHERE Name = Candidate COLLATE NOCASE)
                    ) SELECT Candidate FROM names WHERE NOT EXISTS (SELECT 1 FROM _RoutingEntries WHERE Name = Candidate COLLATE NOCASE) LIMIT 1), -2147483648, 1, 'block', '', Block, '', '', '', '', '', 'any', UpdatedUtc, UpdatedUtc FROM RouteBasics
            WHERE Id = 1 AND trim(Block) <> '' AND NOT EXISTS (SELECT 1 FROM Rules WHERE Id = -1);
            INSERT INTO _RoutingEntries (Id, Name, Position, IsEnabled, Action, Outbound, Targets, Groups, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc)
            SELECT -2, (WITH RECURSIVE names(N, Candidate) AS (
                        SELECT 0, 'basic-direct'
                        UNION ALL SELECT N + 1, 'basic-direct-' || (N + 1) FROM names
                        WHERE EXISTS (SELECT 1 FROM _RoutingEntries WHERE Name = Candidate COLLATE NOCASE)
                    ) SELECT Candidate FROM names WHERE NOT EXISTS (SELECT 1 FROM _RoutingEntries WHERE Name = Candidate COLLATE NOCASE) LIMIT 1), -2147483647, 1, 'direct', '', Direct, '', '', '', '', '', 'any', UpdatedUtc, UpdatedUtc FROM RouteBasics
            WHERE Id = 1 AND trim(Direct) <> '' AND NOT EXISTS (SELECT 1 FROM Rules WHERE Id = -2);

            UPDATE _RoutingEntries SET IsEnabled = 0
            WHERE (SELECT COUNT(*) FROM json_each('["' || replace(replace(replace(replace(replace(Targets, char(9), ','), char(10), ','), char(13), ','), ' ', ''), ',', '","') || '"]') WHERE value <> '') > 64
               OR json_array_length('[' || Groups || ']') > 64;

            CREATE TEMP TABLE _RoutingFallbacks AS
            SELECT r.*, (SELECT COALESCE(MAX(Id), 0) FROM AddressGroups)
                   + ROW_NUMBER() OVER (ORDER BY r.Position, r.Id) AS CriterionId
            FROM _RoutingEntries r;

            CREATE TABLE TrafficRules (
                Id INTEGER NOT NULL CONSTRAINT PK_TrafficRules PRIMARY KEY AUTOINCREMENT,
                DefaultKey TEXT NULL, Name TEXT NOT NULL, Targets TEXT NOT NULL, DnsMode TEXT NOT NULL DEFAULT 'custom', Dns TEXT NOT NULL, DnsMembers TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
            CREATE UNIQUE INDEX IX_TrafficRules_Name ON TrafficRules (Name);
            CREATE UNIQUE INDEX IX_TrafficRules_DefaultKey ON TrafficRules (DefaultKey);
            INSERT INTO TrafficRules (Id, DefaultKey, Name, Targets, Dns, DnsMembers, CreatedUtc, UpdatedUtc)
            SELECT Id, DefaultKey, Name, Targets, Dns, '{}', CreatedUtc, UpdatedUtc FROM AddressGroups;

            INSERT INTO TrafficRules (Id, Name, Targets, Dns, DnsMembers, CreatedUtc, UpdatedUtc)
            SELECT r.CriterionId,
                   (WITH RECURSIVE names(N, Candidate) AS (
                        SELECT 0, r.Name
                        UNION ALL SELECT N + 1,
                            substr(r.Name, 1, 32 - length(' (' || r.CriterionId || CASE WHEN N = 0 THEN '' ELSE '-' || N END || ')'))
                            || ' (' || r.CriterionId || CASE WHEN N = 0 THEN '' ELSE '-' || N END || ')' FROM names
                        WHERE EXISTS (SELECT 1 FROM TrafficRules WHERE Name = Candidate COLLATE NOCASE)
                           OR EXISTS (SELECT 1 FROM _RoutingEntries other WHERE other.Id <> r.Id AND other.Name = Candidate COLLATE NOCASE)
                    ) SELECT Candidate FROM names
                      WHERE NOT EXISTS (SELECT 1 FROM TrafficRules WHERE Name = Candidate COLLATE NOCASE)
                        AND NOT EXISTS (SELECT 1 FROM _RoutingEntries other WHERE other.Id <> r.Id AND other.Name = Candidate COLLATE NOCASE) LIMIT 1),
                   r.Targets,
                   CASE d.Selection
                       WHEN 'outbound' THEN COALESCE(NULLIF(bd.Dns, ''), NULLIF(od.Dns, ''),
                           (SELECT group_concat(value, ',') FROM (SELECT DISTINCT j.value FROM _RoutingMembers m JOIN _RoutingOutboundDns md ON md.Name = m.Member, json_each(md.Servers) j WHERE m.Balancer = d.Outbound ORDER BY m.Position, j.key)), '')
                       WHEN 'routing' THEN CASE WHEN r.Action = 'out' THEN COALESCE(NULLIF(br.Dns, ''), NULLIF(ort.Dns, ''), d.Upstreams) ELSE d.Upstreams END
                       ELSE d.Upstreams
                   END,
                   CASE WHEN d.Selection = 'routing' AND r.Action = 'out' AND br.Name IS NOT NULL AND trim(br.Dns) = '' THEN
                       COALESCE((SELECT json_group_object(m.Member, json(CASE WHEN trim(md.Dns) <> '' THEN md.Servers ELSE
                           '["' || replace(replace(replace(replace(replace(d.Upstreams, char(9), ','), char(10), ','), char(13), ','), ' ', ''), ',', '","') || '"]' END))
                           FROM _RoutingMembers m JOIN _RoutingOutboundDns md ON md.Name = m.Member WHERE m.Balancer = r.Outbound), '{}')
                       WHEN d.Selection = 'outbound' AND bd.Name IS NOT NULL AND trim(bd.Dns) = '' THEN
                       COALESCE((SELECT json_group_object(m.Member, json(md.Servers)) FROM _RoutingMembers m JOIN _RoutingOutboundDns md ON md.Name = m.Member WHERE m.Balancer = d.Outbound), '{}')
                       ELSE '{}'
                   END,
                   r.CreatedUtc, r.UpdatedUtc
            FROM _RoutingFallbacks r CROSS JOIN _RoutingDns d
            LEFT JOIN Balancers bd ON bd.Name = d.Outbound
            LEFT JOIN _RoutingOutboundDns od ON od.Name = d.Outbound
            LEFT JOIN Balancers br ON br.Name = r.Outbound
            LEFT JOIN _RoutingOutboundDns ort ON ort.Name = r.Outbound;

            CREATE TEMP TABLE _RoutingGroupUses AS
            SELECT r.Id AS EntryId, CAST(j.value AS INTEGER) AS GroupId, MIN(CAST(j.key AS INTEGER)) AS Position,
                   CASE WHEN d.Selection = 'routing' AND trim(g.Dns) <> '' THEN 1 ELSE 0 END AS IsExplicit,
                   r.CriterionId
            FROM _RoutingFallbacks r CROSS JOIN _RoutingDns d, json_each('[' || r.Groups || ']') j
            JOIN AddressGroups g ON g.Id = CAST(j.value AS INTEGER)
            GROUP BY r.Id, CAST(j.value AS INTEGER);
            CREATE INDEX _RoutingGroupUses_GroupId ON _RoutingGroupUses(GroupId);

            CREATE TEMP TABLE _RoutingPolicies AS
            WITH RECURSIVE tokens(Id, Rest, Value, Position) AS (
                SELECT t.Id, replace(replace(replace(replace(t.Dns, char(9), ','), char(10), ','), char(13), ','), ' ', ',') || ',', '', 0
                FROM TrafficRules t JOIN _RoutingFallbacks r ON r.CriterionId = t.Id
                UNION ALL
                SELECT Id, substr(Rest, instr(Rest, ',') + 1), substr(Rest, 1, instr(Rest, ',') - 1), Position + 1
                FROM tokens WHERE Rest <> ''
            )
            SELECT t.Id,
                   COALESCE((SELECT json_group_array(Value) FROM (SELECT Value FROM tokens WHERE Id = t.Id AND Value <> '' ORDER BY Position)), '[]') AS Servers,
                   COALESCE((SELECT group_concat(Value, ',') FROM (SELECT Value FROM tokens WHERE Id = t.Id AND Value <> '' ORDER BY Position)), '') AS Dns,
                   COALESCE((SELECT json_group_object(key, json(value)) FROM (SELECT key, value FROM json_each(t.DnsMembers) ORDER BY key)), '{}') AS Members
            FROM TrafficRules t JOIN _RoutingFallbacks r ON r.CriterionId = t.Id;
            CREATE UNIQUE INDEX _RoutingPolicies_Id ON _RoutingPolicies(Id);

            CREATE TEMP TABLE _RoutingGroupPolicies AS
            SELECT g.Id, COUNT(u.EntryId) AS Uses,
                   COUNT(DISTINCT p.Servers || char(31) || p.Members) AS Policies,
                   MIN(p.Dns) AS Dns, MIN(p.Members) AS Members
            FROM AddressGroups g LEFT JOIN _RoutingGroupUses u ON u.GroupId = g.Id
            LEFT JOIN _RoutingPolicies p ON p.Id = u.CriterionId
            GROUP BY g.Id;

            CREATE TEMP TABLE _RoutingGroupDns AS
            SELECT g.Id,
                   CASE WHEN d.Selection = 'routing' AND trim(g.Dns) <> '' THEN 'custom'
                        WHEN p.Uses = 0 THEN CASE WHEN trim(g.Dns) = '' THEN 'default' ELSE 'custom' END
                        WHEN p.Policies = 1 THEN 'custom' ELSE 'default' END AS DnsMode,
                   CASE WHEN d.Selection = 'routing' AND trim(g.Dns) <> '' OR p.Uses = 0 THEN g.Dns
                        WHEN p.Policies = 1 THEN p.Dns ELSE '' END AS Dns,
                   CASE WHEN d.Selection = 'routing' AND trim(g.Dns) <> '' OR p.Uses = 0 THEN '{}'
                        WHEN p.Policies = 1 THEN p.Members ELSE '{}' END AS Members
            FROM AddressGroups g CROSS JOIN _RoutingDns d JOIN _RoutingGroupPolicies p ON p.Id = g.Id;

            UPDATE TrafficRules SET
                DnsMode = (SELECT DnsMode FROM _RoutingGroupDns WHERE Id = TrafficRules.Id),
                Dns = (SELECT Dns FROM _RoutingGroupDns WHERE Id = TrafficRules.Id),
                DnsMembers = (SELECT Members FROM _RoutingGroupDns WHERE Id = TrafficRules.Id)
            WHERE Id IN (SELECT Id FROM _RoutingGroupDns);

            CREATE TABLE RoutingEntries (
                Id INTEGER NOT NULL CONSTRAINT PK_RoutingEntries PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL, Position INTEGER NOT NULL, IsEnabled INTEGER NOT NULL, Action TEXT NOT NULL, Outbound TEXT NOT NULL,
                Clients TEXT NOT NULL, Inbounds TEXT NOT NULL, Ports TEXT NOT NULL, SourcePorts TEXT NOT NULL, Protocol TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
            CREATE INDEX IX_RoutingEntries_Position ON RoutingEntries (Position);
            INSERT INTO RoutingEntries (Id, Name, Position, IsEnabled, Action, Outbound, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc)
            SELECT Id, Name, Position, IsEnabled, Action, Outbound, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc FROM _RoutingEntries;

            CREATE TABLE RoutingEntryRules (
                EntryId INTEGER NOT NULL, RuleId INTEGER NOT NULL, Position INTEGER NOT NULL,
                CONSTRAINT PK_RoutingEntryRules PRIMARY KEY (EntryId, RuleId),
                CONSTRAINT FK_RoutingEntryRules_RoutingEntries_EntryId FOREIGN KEY (EntryId) REFERENCES RoutingEntries (Id) ON DELETE CASCADE,
                CONSTRAINT FK_RoutingEntryRules_TrafficRules_RuleId FOREIGN KEY (RuleId) REFERENCES TrafficRules (Id) ON DELETE RESTRICT);
            CREATE UNIQUE INDEX IX_RoutingEntryRules_EntryId_Position ON RoutingEntryRules (EntryId, Position);
            CREATE INDEX IX_RoutingEntryRules_RuleId ON RoutingEntryRules (RuleId);
            INSERT INTO RoutingEntryRules (EntryId, RuleId, Position)
            SELECT EntryId, GroupId, ROW_NUMBER() OVER (PARTITION BY EntryId ORDER BY IsExplicit DESC, Position) - 1
            FROM _RoutingGroupUses;
            INSERT INTO RoutingEntryRules (EntryId, RuleId, Position)
            SELECT r.Id, r.CriterionId, (SELECT COUNT(*) FROM RoutingEntryRules one WHERE one.EntryId = r.Id)
            FROM _RoutingFallbacks r
            WHERE trim(r.Targets, ' ,' || char(9) || char(10) || char(13)) <> '' OR NOT EXISTS (SELECT 1 FROM _RoutingGroupUses one WHERE one.EntryId = r.Id);
            DELETE FROM TrafficRules
            WHERE Id IN (SELECT CriterionId FROM _RoutingFallbacks)
              AND NOT EXISTS (SELECT 1 FROM RoutingEntryRules one WHERE one.RuleId = TrafficRules.Id);

            INSERT INTO TrafficRules (DefaultKey, Name, Targets, DnsMode, Dns, DnsMembers, CreatedUtc, UpdatedUtc)
            SELECT 'resolver-default',
                   (WITH RECURSIVE names(N, Candidate) AS (
                        SELECT 0, 'Default DNS'
                        UNION ALL SELECT N + 1, 'Default DNS ' || (N + 1) FROM names
                        WHERE EXISTS (SELECT 1 FROM TrafficRules WHERE Name = Candidate COLLATE NOCASE)
                    ) SELECT Candidate FROM names WHERE NOT EXISTS (SELECT 1 FROM TrafficRules WHERE Name = Candidate COLLATE NOCASE) LIMIT 1), '',
                   CASE WHEN d.IsFresh = 1 THEN 'default' ELSE 'custom' END,
                   CASE WHEN d.IsFresh = 1 THEN '' WHEN d.Selection = 'outbound' THEN COALESCE(NULLIF(b.Dns, ''), NULLIF(o.Dns, ''),
                       (SELECT group_concat(value, ',') FROM (SELECT DISTINCT j.value FROM _RoutingMembers m JOIN _RoutingOutboundDns md ON md.Name = m.Member, json_each(md.Servers) j WHERE m.Balancer = d.Outbound ORDER BY m.Position, j.key)), '')
                       ELSE d.Upstreams END,
                   '{}', '2026-11-25 00:00:00+00:00', '2026-11-25 00:00:00+00:00'
            FROM _RoutingDns d LEFT JOIN Balancers b ON b.Name = d.Outbound LEFT JOIN _RoutingOutboundDns o ON o.Name = d.Outbound;
            INSERT INTO RoutingEntries (Name, Position, IsEnabled, Action, Outbound, Clients, Inbounds, Ports, SourcePorts, Protocol, CreatedUtc, UpdatedUtc)
            SELECT (WITH RECURSIVE names(N, Candidate) AS (
                        SELECT 0, 'Default DNS'
                        UNION ALL SELECT N + 1, 'Default DNS ' || (N + 1) FROM names
                        WHERE EXISTS (SELECT 1 FROM RoutingEntries WHERE Name = Candidate COLLATE NOCASE)
                    ) SELECT Candidate FROM names WHERE NOT EXISTS (SELECT 1 FROM RoutingEntries WHERE Name = Candidate COLLATE NOCASE) LIMIT 1), 2147483647, 1, 'direct', '', '', '', '', '', 'any', '2026-11-25 00:00:00+00:00', '2026-11-25 00:00:00+00:00';
            INSERT INTO RoutingEntryRules (EntryId, RuleId, Position)
            SELECT (SELECT MAX(Id) FROM RoutingEntries), (SELECT MAX(Id) FROM TrafficRules), 0;
            """);

        migrationBuilder.Sql("""
            DROP TABLE AddressGroups;
            DROP TABLE RouteBasics;
            DROP TABLE Rules;
            ALTER TABLE Resolver DROP COLUMN Selection;
            ALTER TABLE Resolver DROP COLUMN Outbound;
            ALTER TABLE Resolver DROP COLUMN Upstreams;
            DROP TABLE _RoutingGuard;
            DROP TABLE _RoutingDns;
            DROP TABLE _RoutingMembers;
            DROP TABLE _RoutingOutboundDns;
            DROP TABLE _RoutingEntries;
            DROP TABLE _RoutingFallbacks;
            DROP TABLE _RoutingGroupUses;
            DROP TABLE _RoutingPolicies;
            DROP TABLE _RoutingGroupPolicies;
            DROP TABLE _RoutingGroupDns;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("restore the pre-upgrade database backup to return to the previous routing schema");
    }
}
