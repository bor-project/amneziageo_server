using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Tests;

public class DatabaseBackupTests
{
    [Fact]
    public async Task TheCopyCarriesTheAccountsAndTheRoles()
    {
        using var bench = new Bench();
        await bench.UserAsync("keeper", "long-enough");
        await bench.RoleAsync("watcher", Scopes.ReadState);

        var copy = await new DatabaseBackup(bench.Db).CopyAsync(CancellationToken.None);
        try
        {
            Assert.Equal(1L, await CountAsync(copy, "SELECT COUNT(*) FROM AspNetUsers WHERE UserName = 'keeper';"));
            Assert.Equal(1L, await CountAsync(copy, "SELECT COUNT(*) FROM AspNetRoles WHERE Name = 'watcher';"));
            Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(bench.DatabasePath)), Path.GetDirectoryName(copy));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(copy));
            }
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public async Task TheBuiltInRoleHoldsTheBackupRight()
    {
        using var bench = new Bench();

        var role = await bench.RoleStore.FindByNameAsync(Roles.Admin);
        var claims = await bench.RoleStore.GetClaimsAsync(role!);

        Assert.Contains(claims, claim => claim.Type == Scopes.ClaimType && claim.Value == Scopes.ReadBackup);
    }

    private static async Task<long> CountAsync(string path, string sql)
    {
        var text = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var connection = new SqliteConnection(text);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
