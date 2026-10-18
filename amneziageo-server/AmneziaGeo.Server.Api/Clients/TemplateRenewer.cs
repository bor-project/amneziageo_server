using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Works the ranges of the client templates out anew and keeps what came out.
/// </summary>
public sealed class TemplateRenewer
{
    private readonly TemplateStore _store;

    private readonly TemplateRefresher _refresher;

    private readonly TimeProvider _time;

    private readonly ILogger<TemplateRenewer> _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public TemplateRenewer(
        TemplateStore store,
        TemplateRefresher refresher,
        TimeProvider time,
        ILogger<TemplateRenewer> logger)
    {
        _store = store;
        _refresher = refresher;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Works out anew the templates with entries whose ranges are older than the age, all of them without one, and
    /// returns the names of the templates it renewed.
    /// </summary>
    public async Task<IReadOnlyList<string>> RenewAsync(TimeSpan? olderThan, CancellationToken ct)
    {
        var held = await _store.ListAsync(ct).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var renewed = new List<string>();

        foreach (var template in held.Where(one => Due(one, olderThan, now)))
        {
            if (await RenewAsync(template, ct).ConfigureAwait(false))
            {
                renewed.Add(template.Name);
            }
        }

        return renewed;
    }

    // Works one template out anew and tells whether its ranges were written.
    private async Task<bool> RenewAsync(ClientTemplate template, CancellationToken ct)
    {
        var pass = await _refresher.RenewAsync(template, ct).ConfigureAwait(false);
        if (pass.Silence is { } silence)
        {
            _logger.LogWarning(
                "template {Name} keeps the ranges worked out at {Refreshed}: {Silence}",
                template.Name,
                template.RefreshedUtc,
                silence);

            return false;
        }

        if (!await _store.RenewAsync(pass.Template, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("template {Name} was changed while its ranges were worked out", template.Name);

            return false;
        }

        _logger.LogInformation(
            "template {Name} was worked out anew, {Count} ranges",
            template.Name,
            pass.Template.AllowedIps.Count);

        return true;
    }

    private static bool Due(ClientTemplate template, TimeSpan? olderThan, DateTimeOffset now) =>
        template.Entries.Count > 0
        && (olderThan is null || template.RefreshedUtc is not { } stamp || now - stamp >= olderThan);
}
