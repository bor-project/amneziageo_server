using AmneziaGeo.Server.Auth;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Tests;

public class DatabaseTests
{
    [Fact]
    public async Task TheSchemaCarriesTheTablesEverySchemeNeeds()
    {
        using var bench = new Bench();

        var tables = await NamesAsync(bench);

        foreach (var table in new[]
                 {
                     "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetRoleClaims",
                     "AspNetUserClaims", "Sessions", "RefreshTokens", "AuditEntries",
                 })
        {
            Assert.Contains(table, tables);
        }
    }

    [Fact]
    public async Task TheRightsOfARoleFallWithIt()
    {
        using var bench = new Bench();
        var role = await bench.RoleAsync("watcher", Scopes.ReadState);

        await bench.RoleStore.DeleteAsync(role);

        Assert.Empty(bench.Db.RoleClaims.Where(claim => claim.RoleId == role.Id).ToList());
    }

    [Fact]
    public async Task TheSessionsOfAnAccountFallWithIt()
    {
        using var bench = new Bench();
        await bench.UserAsync("one", "long-enough");
        await bench.UserAsync("two", "long-enough");
        await bench.Login.PasswordAsync("two", "long-enough", null, "test", CancellationToken.None);

        await bench.Accounts.RemoveAsync("two", actorId: 0, CancellationToken.None);

        Assert.Empty(bench.Db.Sessions.ToList());
    }

    private static async Task<List<string>> NamesAsync(Bench bench)
    {
        var found = new List<string>();
        var connection = bench.Db.Database.GetDbConnection();
        await bench.Db.Database.OpenConnectionAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetString(0));
        }

        return found;
    }
}
