using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Opens the connections to the databases of the panel one at a time.
/// </summary>
public sealed class DatabaseOpening : DbConnectionInterceptor
{
    /// <summary>
    /// The interceptor the contexts of the panel open through.
    /// </summary>
    public static readonly DatabaseOpening Instance = new();

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly ConditionalWeakTable<DbConnection, object> Held = [];

    private DatabaseOpening()
    {
    }

    /// <summary>
    /// Opens a connection of its own in turn with the others.
    /// </summary>
    public static void Open(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Gate.Wait();
        try
        {
            connection.Open();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Waits for the turn of the connection.
    /// </summary>
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        Gate.Wait();
        Held.AddOrUpdate(connection, Gate);

        return result;
    }

    /// <summary>
    /// Waits for the turn of the connection.
    /// </summary>
    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Held.AddOrUpdate(connection, Gate);

        return result;
    }

    /// <summary>
    /// Lets the next connection open.
    /// </summary>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Leave(connection);

    /// <summary>
    /// Lets the next connection open.
    /// </summary>
    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Leave(connection);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Lets the next connection open.
    /// </summary>
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) => Leave(connection);

    /// <summary>
    /// Lets the next connection open.
    /// </summary>
    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Leave(connection);

        return Task.CompletedTask;
    }

    // Lets the turn go when the connection holds it.
    private static void Leave(DbConnection connection)
    {
        if (Held.Remove(connection))
        {
            Gate.Release();
        }
    }
}
