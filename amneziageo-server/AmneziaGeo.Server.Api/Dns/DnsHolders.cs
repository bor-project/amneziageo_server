using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// Knows how many clients hold a file that names the resolver of the panel and how many pick it.
/// </summary>
public sealed class DnsHolders
{
    private int _count;

    private int _picked;

    /// <summary>
    /// Raised when one of the numbers changed.
    /// </summary>
    public event Action? Stirred;

    /// <summary>
    /// How many clients that are on hold a file that names the resolver.
    /// </summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// How many clients that are on pick the resolver, themselves or through their template or endpoint.
    /// </summary>
    public int Picked => Volatile.Read(ref _picked);

    /// <summary>
    /// Returns how many clients that are on, at endpoints that are on, pick the resolver.
    /// </summary>
    public static int Picking(
        IReadOnlyList<TunnelClient> clients,
        IReadOnlyList<ClientTemplate> templates,
        IReadOnlyList<ServerConfig> configs,
        DnsFiles? files = null)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(configs);

        var held = templates.ToDictionary(one => one.Id);
        var endpoints = configs.ToDictionary(one => one.Id);

        return clients.Count(client => client.IsEnabled
            && endpoints.TryGetValue(client.ConfigId, out var config)
            && config.IsEnabled
            && ClientText.Picks(client, client.TemplateId is { } id ? held.GetValueOrDefault(id) : null, files));
    }

    /// <summary>
    /// Takes the numbers of the clients anew.
    /// </summary>
    public async Task RefreshAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        var clients = services.GetRequiredService<ClientStore>();
        var count = await clients.ResolverHoldersAsync(ct).ConfigureAwait(false);
        var picked = Picking(
            await clients.ListAsync(ct).ConfigureAwait(false),
            await services.GetRequiredService<TemplateStore>().ListAsync(ct).ConfigureAwait(false),
            await services.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false),
            (await services.GetRequiredService<DnsStore>().ReadAsync(ct).ConfigureAwait(false)).Files);
        Keep(count, picked);
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
            Keep(await store.ResolverHoldersAsync(ct).ConfigureAwait(false), Picked);
        }
    }

    private void Keep(int count, int picked)
    {
        var moved = Interlocked.Exchange(ref _count, count) != count;
        moved |= Interlocked.Exchange(ref _picked, picked) != picked;
        if (moved)
        {
            Stirred?.Invoke();
        }
    }
}
