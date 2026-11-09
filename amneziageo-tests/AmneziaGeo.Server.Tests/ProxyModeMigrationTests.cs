using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyModeMigrationTests
{
    [Fact]
    public async Task ExistingRowsKeepIdentityAndPublicTlsAfterUpgrade()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await using (db.ConfigureAwait(false))
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("TransportProxyTargets").ConfigureAwait(false);
            var secret = new string('a', 64);
            var targets = "{\"restricted\":true,\"destinations\":[{\"host\":\"vpn.example.net\",\"port\":51820}]}";
            foreach (var transport in new[] { "websocket", "xhttp" })
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO TransportProxies (Name, Transport, Host, Port, ListenPort, OriginHttp, Path, Certificate, CertificateKey, ServerName, Pin, TargetsJson, Secret, IsEnabled, Opened, UpdatedUtc) "
                    + "VALUES ({0}, {0}, 'proxy.example.net', 443, 0, 1, {0}, '/cert.pem', '/key.pem', 'proxy.example.net', '', {1}, {2}, 1, 0, '2026-11-08 10:00:00+00:00')",
                    transport, targets, secret).ConfigureAwait(false);
            }

            var identities = await db.Database.SqlQueryRaw<long>("SELECT Id AS Value FROM TransportProxies ORDER BY Id").ToArrayAsync().ConfigureAwait(false);
            await migrator.MigrateAsync().ConfigureAwait(false);
            var rows = await db.Set<TransportProxyEntity>().AsNoTracking().OrderBy(one => one.Id).ToArrayAsync().ConfigureAwait(false);
            Assert.Equal(identities, rows.Select(one => one.Id));
            Assert.Equal(new[] { "websocket", "xhttp" }, rows.Select(one => one.Transport));
            foreach (var row in rows)
            {
                Assert.Equal(secret, row.Secret);
                Assert.Equal(targets, row.TargetsJson);
                Assert.Equal(row.Transport, row.Path);
                Assert.Equal("/cert.pem", row.Certificate);
                Assert.Equal("/key.pem", row.CertificateKey);
                Assert.True(row.IsEnabled);
                Assert.True(row.Hosted);
                Assert.True(row.OriginHttp);
                Assert.Null(row.UseCertificate);
                Assert.True(row.UsesCertificate);
                Assert.Equal("GET", row.HttpMethod);
                Assert.Equal(0, row.ProtocolVersion);
            }
        }
    }
}
