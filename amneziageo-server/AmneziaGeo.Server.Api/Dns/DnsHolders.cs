using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// Knows how many clients hold a file that names the resolver of the panel.
/// </summary>
public sealed class DnsHolders
{
    private int _count;

    /// <summary>
    /// Raised when the number of the clients changed.
    /// </summary>
    public event Action? Stirred;

    /// <summary>
    /// How many clients that are on hold a file that names the resolver.
    /// </summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// Takes the number of the clients anew.
    /// </summary>
    public async Task RefreshAsync(ClientStore store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);

        var count = await store.ResolverHoldersAsync(ct).ConfigureAwait(false);
        if (Interlocked.Exchange(ref _count, count) != count)
        {
            Stirred?.Invoke();
        }
    }

    /// <summary>
    /// Keeps which clients were handed a file that names the resolver and which a file that does not.
    /// </summary>
    public async Task HandedAsync(
        ClientStore store,
        IReadOnlyCollection<long> named,
        IReadOnlyCollection<long> unnamed,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (await store.NoteResolverAsync(named, unnamed, ct).ConfigureAwait(false))
        {
            await RefreshAsync(store, ct).ConfigureAwait(false);
        }
    }
}
