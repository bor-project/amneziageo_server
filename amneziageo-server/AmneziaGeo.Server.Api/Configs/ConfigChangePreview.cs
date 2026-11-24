using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Api.Configs;

public sealed record ClientConfigChange(long Id, string Name, string Revision, IReadOnlyList<string> Fields, bool RequiresCoordination);
public sealed record ConfigChangePreview(string BaseRevision, IReadOnlyList<ClientConfigChange> Clients, string ClientsRevision);
public sealed record PendingConfigBody(long ConfigId, string Name, DateTimeOffset CreatedUtc, bool Stale, ConfigChangePreview Preview);

public sealed class ConfigChangeReader(ClientStore clients, TemplateStore templates, DnsStore dns, DnsState resolver)
{
    /// <summary>
    /// Compares the effective client configurations.
    /// </summary>
    public async Task<ConfigChangePreview> PreviewAsync(ServerConfig before, ServerConfig after, CancellationToken ct)
    {
        var changed = new List<ClientConfigChange>();
        var revisions = new List<string>();
        var effective = new Dictionary<long, ClientTemplate?>();
        var settings = await dns.ReadAsync(ct).ConfigureAwait(false);
        var handing = resolver.Handing ?? settings with { IsEnabled = false };
        var offering = resolver.Chosen ?? settings;
        foreach (var member in (await clients.ListAsync(before.Id, ct).ConfigureAwait(false)).OrderBy(one => one.Id))
        {
            var key = member.TemplateId ?? 0;
            if (!effective.TryGetValue(key, out var template))
            {
                template = await templates.EffectiveAsync(member.TemplateId, ct).ConfigureAwait(false);
                effective.Add(key, template);
            }
            var left = Text(before);
            var right = Text(after);
            revisions.Add(member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + ConfigurationRevision.Of(left));
            var fields = ConfigurationRevision.Fields(left, right);
            if (fields.Count > 0)
            {
                changed.Add(new(member.Id, member.Name, ConfigurationRevision.Of(right), fields, ConfigurationRevision.RequiresCoordination(fields)));
            }

            string Text(ServerConfig endpoint) => ClientText.Text(endpoint, member, template,
                DnsHandout.For(endpoint, handing, resolver.KeptOut), DnsHandout.For(endpoint, offering, resolver.KeptOut), settings.Files);
        }
        return new(ConfigDraftStore.Revision(before), changed, ConfigurationRevision.Of(string.Join("\n", revisions)));
    }
}
