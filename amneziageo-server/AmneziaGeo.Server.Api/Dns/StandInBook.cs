using System.Globalization;
using System.Net;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Geo.Files;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// Knows the clients the resolver answers with stand-in addresses and the names it does it for.
/// </summary>
public sealed class StandInBook
{
    /// <summary>
    /// How often the clients and the templates are taken anew without a word that they changed.
    /// </summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _turn = new(1, 1);

    private readonly TimeProvider _time;

    private Dictionary<IPAddress, DomainMatcher> _clients = [];

    private Dictionary<long, DomainMatcher> _matchers = [];

    private string _shape = string.Empty;

    private long _next;

    /// <summary>
    /// ctor
    /// </summary>
    public StandInBook(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Raised when the clients or the templates changed.
    /// </summary>
    public event Action? Stirred;

    /// <summary>
    /// Tells whether the clients and the templates are due to be taken anew.
    /// </summary>
    public bool Due => _time.GetUtcNow().UtcTicks >= Interlocked.Read(ref _next);

    /// <summary>
    /// Returns the names a client is answered with stand-in addresses for, or null when it is answered as it is.
    /// </summary>
    public DomainMatcher? For(IPAddress client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var key = client.IsIPv4MappedToIPv6 ? client.MapToIPv4() : client;

        return Volatile.Read(ref _clients).GetValueOrDefault(key);
    }

    /// <summary>
    /// Has the clients and the templates taken anew at once.
    /// </summary>
    public void Stir()
    {
        Interlocked.Exchange(ref _next, 0);
        Stirred?.Invoke();
    }

    /// <summary>
    /// Takes the clients and the templates anew.
    /// </summary>
    public async Task RefreshAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref _next, (_time.GetUtcNow() + Every).UtcTicks);
            var templates = (await services.GetRequiredService<TemplateStore>().ListAsync(ct).ConfigureAwait(false))
                .Where(template => template.StandIns)
                .ToArray();
            if (templates.Length == 0)
            {
                Volatile.Write(ref _clients, []);
                _matchers = [];
                _shape = string.Empty;

                return;
            }

            var sources = await services.GetRequiredService<GeoStore>().ListAsync(ct).ConfigureAwait(false);
            var shape = Shape(templates, sources);
            if (!string.Equals(shape, _shape, StringComparison.Ordinal))
            {
                var index = GeoIndex.Load(sources, services.GetRequiredService<IGeoFileStore>());
                _matchers = templates.ToDictionary(template => template.Id, template => Matcher(template, index));
                _shape = shape;
            }

            var clients = await services.GetRequiredService<ClientStore>().ListAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _clients, Clients(clients, _matchers));
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Returns the names the entries of a template stand for.
    /// </summary>
    public static DomainMatcher Matcher(ClientTemplate template, GeoIndex index)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(index);

        var domains = new List<GeoDomain>();
        foreach (var entry in template.Entries)
        {
            var rule = TemplateList.Rule(entry);
            if (rule?.Kind == GeoRuleKind.GeoSite)
            {
                domains.AddRange(index.Domains(rule.Value));
            }
            else if (rule?.Kind == GeoRuleKind.Domain)
            {
                domains.Add(new GeoDomain(GeoDomainKind.Domain, rule.Value));
            }
        }

        return new DomainMatcher(domains);
    }

    private static Dictionary<IPAddress, DomainMatcher> Clients(
        IReadOnlyList<TunnelClient> clients,
        Dictionary<long, DomainMatcher> matchers)
    {
        var found = new Dictionary<IPAddress, DomainMatcher>();
        foreach (var client in clients)
        {
            if (!client.IsEnabled || client.TemplateId is not { } id || !matchers.TryGetValue(id, out var matcher))
            {
                continue;
            }

            foreach (var range in client.Address)
            {
                var slash = range.IndexOf('/', StringComparison.Ordinal);
                if (IPAddress.TryParse(slash < 0 ? range : range[..slash], out var address))
                {
                    found[address] = matcher;
                }
            }
        }

        return found;
    }

    // Returns what the matchers were built from: the templates with their entries and the geo files.
    private static string Shape(IReadOnlyList<ClientTemplate> templates, IReadOnlyList<GeoSource> sources) =>
        string.Join(
            "|",
            templates
                .Select(template => template.Id.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(",", template.Entries))
                .Concat(sources.Select(source => $"{source.Id}:{source.IsEnabled}:{source.Position}:{source.Sha256}")));
}
