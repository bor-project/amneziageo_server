using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Route;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// A draft the panel asks what it contradicts: a client, a template, an endpoint, a rule or the resolver.
/// </summary>
/// <param name="Id">The number of the drafted client, template, endpoint or rule, none for a new one.</param>
/// <param name="Client">The draft of a client.</param>
/// <param name="Template">The draft of a template.</param>
/// <param name="Config">The draft of an endpoint.</param>
/// <param name="Rule">The draft of a rule.</param>
/// <param name="Dns">The draft of the settings of the resolver.</param>
public sealed record DnsCheckRequest(
    long? Id = null,
    ClientRequest? Client = null,
    TemplateRequest? Template = null,
    ConfigRequest? Config = null,
    RouteRequest? Rule = null,
    DnsRequest? Dns = null);

/// <summary>
/// The name servers a file names, as the panel reads them.
/// </summary>
/// <param name="Servers">The name servers of the DNS line.</param>
/// <param name="From">`client`, `template`, `endpoint` or `standins`.</param>
/// <param name="Choice">`auto`, `panel` or `own`, as picked there.</param>
/// <param name="IsPanel">Whether the servers are the resolver of the panel.</param>
/// <param name="Endpoint">The endpoint the file is at.</param>
/// <param name="Lost">Whether the file wants the resolver and loses it to the port, its addresses or the firewall.</param>
public sealed record DnsLineBody(IReadOnlyList<string> Servers, string From, string Choice, bool IsPanel, string Endpoint, bool Lost);

/// <summary>
/// What a draft contradicts and the name servers it gives a file.
/// </summary>
/// <param name="Line">The name servers a file of the draft names, null for a rule or the resolver.</param>
/// <param name="Endpoint">The endpoint the line was worked out at.</param>
/// <param name="Findings">What the draft contradicts.</param>
/// <param name="Lines">The name servers a file of the draft names at each endpoint of its clients.</param>
public sealed record DnsCheckResponse(DnsLineBody? Line, string Endpoint, IReadOnlyList<DnsFinding> Findings, IReadOnlyList<DnsLineBody> Lines);

/// <summary>
/// Reads who needs the resolver and what contradicts, in what the panel holds and in drafts.
/// </summary>
public static class DnsChecks
{
    /// <summary>
    /// Returns who needs the resolver and what contradicts in what the panel holds.
    /// </summary>
    public static async Task<IResult> ConflictsAsync(
        DnsStore store,
        ConfigStore configs,
        TemplateStore templates,
        ClientStore clients,
        RoutePlans plans,
        ResolverDoors doors,
        CancellationToken ct)
    {
        var world = await WorldAsync(store, configs, templates, clients, plans, doors, ct).ConfigureAwait(false);

        return Results.Ok(DnsConflicts.Of(world));
    }

    /// <summary>
    /// Returns what a draft contradicts and the name servers it gives a file.
    /// </summary>
    public static async Task<IResult> CheckAsync(
        DnsCheckRequest request,
        DnsStore store,
        ConfigStore configs,
        TemplateStore templates,
        ClientStore clients,
        RoutePlans plans,
        ResolverDoors doors,
        TemplateRefresher refresher,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(refresher);

        if (request is { Client: null, Template: null, Config: null, Rule: null, Dns: null })
        {
            return Results.Json(
                new Failure("empty-check", "the check names no client, template, endpoint, rule or resolver"),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var world = await WorldAsync(store, configs, templates, clients, plans, doors, ct).ConfigureAwait(false);
        var ranges = await RangesAsync(world, request, refresher, ct).ConfigureAwait(false);

        return Results.Ok(Check(world, request, ranges));
    }

    // Works out the ranges of a template draft whose entries differ from the template it changes.
    private static async Task<IReadOnlyList<string>?> RangesAsync(
        DnsWorld world,
        DnsCheckRequest request,
        TemplateRefresher refresher,
        CancellationToken ct)
    {
        if (request.Template is not { } template)
        {
            return null;
        }

        var entries = TemplateAnswers.Entries(template.Entries);
        var standIns = template.StandIns ?? false;
        var held = request.Id is > 0 ? world.Templates.FirstOrDefault(one => one.Id == request.Id) : null;
        if (held is not null && held.StandIns == standIns && held.Entries.SequenceEqual(entries, StringComparer.Ordinal))
        {
            return null;
        }

        if (entries.Count == 0 || TemplateList.Check(entries) is not null)
        {
            return [];
        }

        return (await refresher.FindAsync(entries, standIns, ct).ConfigureAwait(false)).AllowedIps;
    }

    /// <summary>
    /// Returns what a draft contradicts in a world and the name servers it gives a file.
    /// </summary>
    /// <param name="world">What the panel holds.</param>
    /// <param name="request">The draft.</param>
    /// <param name="ranges">The ranges the entries of a template draft stand for, none to keep those of the template it changes.</param>
    public static DnsCheckResponse Check(DnsWorld world, DnsCheckRequest request, IReadOnlyList<string>? ranges = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(request);

        var id = request.Id ?? 0;
        if (request.Client is { } client)
        {
            var held = id > 0 ? world.Clients.FirstOrDefault(one => one.Id == id) : null;
            var draft = ClientAnswers.Draft(client, held) with { Id = id, IsEnabled = true };
            var after = world with { Clients = [.. world.Clients.Where(one => one.Id != id), draft] };

            return Answer(
                after,
                draft,
                DnsConflicts.Of(after, hints: true).Findings.Where(one => one.Clients.Any(affected => affected.Id == id)));
        }

        if (request.Template is { } template)
        {
            var held = id > 0 ? world.Templates.FirstOrDefault(one => one.Id == id) : null;
            var draft = TemplateAnswers.Draft(template) with { Id = id, AllowedIps = ranges ?? held?.AllowedIps ?? [] };
            var after = world with { Templates = [.. world.Templates.Where(one => one.Id != id), draft] };
            var members = id > 0 ? after.Clients.Where(one => one.TemplateId == id).ToArray() : [];

            return Spread(
                after,
                id,
                Reached(after, members),
                DnsConflicts.Of(after).Findings.Where(one =>
                    Names(one, "template", id) || one.Clients.Any(affected => members.Any(member => member.Id == affected.Id))));
        }

        if (request.Config is { } endpoint)
        {
            var held = id > 0 ? world.Configs.FirstOrDefault(one => one.Id == id) : null;
            var draft = ConfigAnswers.Draft(endpoint, held) with { Id = id };
            var after = world with { Configs = [.. world.Configs.Where(one => one.Id != id), draft] };
            var members = id > 0 ? after.Clients.Where(one => one.ConfigId == id).Select(one => one.Id).ToHashSet() : [];

            return Answer(
                after,
                Probe(id),
                DnsConflicts.Of(after).Findings.Where(one =>
                    Names(one, "config", id) || one.Clients.Any(affected => members.Contains(affected.Id))));
        }

        if (request.Rule is { } rule)
        {
            var draft = RouteAnswers.Draft(rule) with { Id = id };
            var rules = world.Rules.Where(one => one.Id != id).ToList();
            if (draft.IsEnabled && ByName(draft))
            {
                rules.Add(draft);
            }

            var ruled = world with { Rules = rules };

            return Answer(ruled, null, DnsConflicts.Of(ruled).Findings.Where(one => Names(one, "rule", id)));
        }

        if (request.Dns is { } dns)
        {
            var settled = world with { Settings = DnsAnswers.Draft(dns) };

            return Answer(settled, null, DnsConflicts.Of(settled).Findings.Where(one => Names(one, "dns", 0)));
        }

        return new DnsCheckResponse(null, string.Empty, [], []);
    }

    // Reads what the panel holds that decides the name servers of the clients.
    private static async Task<DnsWorld> WorldAsync(
        DnsStore store,
        ConfigStore configs,
        TemplateStore templates,
        ClientStore clients,
        RoutePlans plans,
        ResolverDoors doors,
        CancellationToken ct)
    {
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);
        var endpoints = await configs.ListAsync(ct).ConfigureAwait(false);

        return new DnsWorld
        {
            Settings = settings,
            KeptOut = await doors.KeptOutAsync(settings with { IsEnabled = true }, endpoints, ct).ConfigureAwait(false),
            Configs = endpoints,
            Templates = await templates.ListAsync(ct).ConfigureAwait(false),
            Clients = await clients.ListAsync(ct).ConfigureAwait(false),
            Holders = await clients.ResolverHeldAsync(ct).ConfigureAwait(false),
            Rules = plans.Held is { } plan
                ? [.. plan.Legs.Where(leg => leg.IsOnHost && leg.Domains.Count > 0).Select(leg => leg.Rule)]
                : [],
        };
    }

    private static DnsCheckResponse Answer(DnsWorld world, TunnelClient? probe, IEnumerable<DnsFinding> findings)
    {
        var body = probe is null ? null : Body(world, probe);

        return new DnsCheckResponse(body, body?.Endpoint ?? string.Empty, [.. findings], body is null ? [] : [body]);
    }

    // Answers a template draft with the line at each endpoint, the first that keeps the resolver as the line.
    private static DnsCheckResponse Spread(DnsWorld world, long template, IReadOnlyList<ServerConfig> endpoints, IEnumerable<DnsFinding> findings)
    {
        var lines = endpoints.Select(config => Body(world, Probe(config.Id) with { TemplateId = template })).OfType<DnsLineBody>().ToArray();
        var first = lines.FirstOrDefault(one => !one.Lost) ?? lines.FirstOrDefault();

        return new DnsCheckResponse(first, first?.Endpoint ?? string.Empty, [.. findings], lines);
    }

    // Returns the endpoints that are on where the clients of a template are, or all that are on when it has none there.
    private static IReadOnlyList<ServerConfig> Reached(DnsWorld world, IReadOnlyList<TunnelClient> members)
    {
        var on = world.Configs.Where(one => one.IsEnabled).OrderBy(one => one.Name, StringComparer.Ordinal).ToArray();
        var used = on.Where(config => members.Any(member => member.IsEnabled && member.ConfigId == config.Id)).ToArray();

        return used.Length > 0 ? used : on;
    }

    // Returns the name servers the file of a client names at its endpoint, null when the panel holds no such endpoint.
    private static DnsLineBody? Body(DnsWorld world, TunnelClient probe)
    {
        var line = DnsConflicts.Line(world, probe);
        var endpoint = world.Configs.FirstOrDefault(one => one.Id == probe.ConfigId)?.Name ?? string.Empty;

        return line is null
            ? null
            : new DnsLineBody(line.Servers, DnsConflicts.From(line.From), DnsChoiceName.Of(line.Choice), line.IsPanel, endpoint, DnsConflicts.Loses(world, probe));
    }

    // Returns a client with nothing of its own at an endpoint, to read what a file there names.
    private static TunnelClient Probe(long config) => new() { Id = -1, ConfigId = config, Name = string.Empty };

    private static bool Names(DnsFinding finding, string kind, long id) =>
        finding.Subjects.Any(subject => subject.Kind == kind && subject.Id == id);

    private static bool ByName(RouteRule rule) =>
        rule.Targets.Select(RouteRules.Target).Any(target => target?.Kind is GeoRuleKind.Domain or GeoRuleKind.GeoSite);
}
