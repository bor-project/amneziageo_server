using System.Net;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Dns;

/// <summary>
/// What the panel holds that decides the name servers of the clients.
/// </summary>
public sealed record DnsWorld
{
    /// <summary>
    /// The settings of the resolver as the panel holds them.
    /// </summary>
    public required DnsSettings Settings { get; init; }

    /// <summary>
    /// The interfaces whose clients the firewall of the host keeps from the resolver.
    /// </summary>
    public IReadOnlyList<string> KeptOut { get; init; } = [];

    /// <summary>
    /// The endpoints.
    /// </summary>
    public IReadOnlyList<ServerConfig> Configs { get; init; } = [];

    /// <summary>
    /// The client templates.
    /// </summary>
    public IReadOnlyList<ClientTemplate> Templates { get; init; } = [];

    /// <summary>
    /// The clients.
    /// </summary>
    public IReadOnlyList<TunnelClient> Clients { get; init; } = [];

    /// <summary>
    /// The clients whose last file names the resolver.
    /// </summary>
    public IReadOnlyCollection<long> Holders { get; init; } = [];

    /// <summary>
    /// The rules by name the host carries, the basic lists among them.
    /// </summary>
    public IReadOnlyList<RouteRule> Rules { get; init; } = [];

    /// <summary>
    /// Tells whether the routing needs the resolver.
    /// </summary>
    public bool Needed => Rules.Count > 0 || Templates.Any(template => template.StandIns);
}

/// <summary>
/// What a finding names: the resolver, a rule, a basic list, a template, an endpoint or a client.
/// </summary>
/// <param name="Kind">`dns`, `rule`, `basic`, `template`, `config` or `client`.</param>
/// <param name="Id">The number the panel holds it under, 0 for the resolver.</param>
/// <param name="Name">The name it is shown under.</param>
public sealed record DnsSubject(string Kind, long Id, string Name);

/// <summary>
/// A client a finding touches, where its name servers are picked and what they are.
/// </summary>
/// <param name="Id">The number of the client.</param>
/// <param name="Name">The name of the client.</param>
/// <param name="From">`client`, `template`, `endpoint` or `standins`.</param>
/// <param name="Source">The name of the client, template or endpoint the choice was made at.</param>
/// <param name="Servers">The name servers the file names.</param>
public sealed record DnsAffected(long Id, string Name, string From, string Source, IReadOnlyList<string> Servers);

/// <summary>
/// One contradiction between the settings that decide the name servers of the clients.
/// </summary>
/// <param name="Code">What contradicts.</param>
/// <param name="Level">`error`, `warning` or `hint`.</param>
/// <param name="Subjects">The settings that bring it.</param>
/// <param name="Clients">The clients it touches.</param>
public sealed record DnsFinding(string Code, string Level, IReadOnlyList<DnsSubject> Subjects, IReadOnlyList<DnsAffected> Clients);

/// <summary>
/// Who needs the resolver.
/// </summary>
/// <param name="Rules">The rules and basic lists by name.</param>
/// <param name="Templates">The templates routed by name.</param>
/// <param name="Picked">The clients that are on and pick the resolver, themselves or through their template or endpoint.</param>
/// <param name="Holders">The clients that are on and hold a file that names the resolver.</param>
public sealed record DnsNeeds(
    IReadOnlyList<DnsSubject> Rules,
    IReadOnlyList<DnsSubject> Templates,
    IReadOnlyList<DnsAffected> Picked,
    IReadOnlyList<DnsAffected> Holders);

/// <summary>
/// Who needs the resolver and what contradicts.
/// </summary>
public sealed record DnsReport(DnsNeeds Needs, IReadOnlyList<DnsFinding> Findings);

/// <summary>
/// Finds what contradicts in the settings that decide the name servers of the clients.
/// </summary>
public static class DnsConflicts
{
    /// <summary>
    /// The level of a contradiction that leaves a client without its names or a rule without effect.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// The level of a contradiction that makes the files take other servers or the rules miss some names.
    /// </summary>
    public const string Warning = "warning";

    /// <summary>
    /// The level of what is worth knowing about one client.
    /// </summary>
    public const string Hint = "hint";

    private static readonly DnsSubject Resolver = new("dns", 0, "dns");

    /// <summary>
    /// Returns who needs the resolver and what contradicts, the hints about single clients too when asked.
    /// </summary>
    public static DnsReport Of(DnsWorld world, bool hints = false)
    {
        ArgumentNullException.ThrowIfNull(world);

        var seen = Look(world);
        var findings = new List<DnsFinding>();
        if (!world.Settings.IsEnabled)
        {
            Off(world, seen, findings);
        }
        else
        {
            Lost(world, seen, findings);
            Outside(world, seen, findings, hints);
        }

        var needs = new DnsNeeds(
            [.. world.Rules.Select(Subject)],
            [.. world.Templates.Where(template => template.StandIns).Select(Subject)],
            [.. seen.Where(one => one.Picks).Select(one => one.Affected)],
            [.. seen.Where(one => world.Holders.Contains(one.Client.Id)).Select(one => one.Affected)]);

        return new DnsReport(needs, findings);
    }

    /// <summary>
    /// Returns the name servers the file of a client names, or null when the panel holds no endpoint of it.
    /// </summary>
    public static ClientDns? Line(DnsWorld world, TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(client);

        var config = world.Configs.FirstOrDefault(one => one.Id == client.ConfigId);
        if (config is null)
        {
            return null;
        }

        var template = client.TemplateId is { } id ? world.Templates.FirstOrDefault(one => one.Id == id) : null;

        return Written(world, config, client, template);
    }

    /// <summary>
    /// Returns whether the file of a client wants the resolver of the panel and loses it to the port, the addresses it listens on or the firewall.
    /// </summary>
    public static bool Loses(DnsWorld world, TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(client);

        var config = world.Configs.FirstOrDefault(one => one.Id == client.ConfigId);
        if (config is null)
        {
            return false;
        }

        var template = client.TemplateId is { } id ? world.Templates.FirstOrDefault(one => one.Id == id) : null;

        return Wants(world, config, client, template) && !Written(world, config, client, template).IsPanel;
    }

    /// <summary>
    /// Returns where the name servers of a file were picked, as the panel reads it.
    /// </summary>
    public static string From(DnsFrom from) => from switch
    {
        DnsFrom.Client => "client",
        DnsFrom.Template => "template",
        DnsFrom.StandIns => "standins",
        _ => "endpoint",
    };

    // The resolver is off: the rules, the templates, the files and the picks that need it.
    private static void Off(DnsWorld world, IReadOnlyList<Seen> seen, List<DnsFinding> findings)
    {
        foreach (var rule in world.Rules)
        {
            findings.Add(Finding("rule-off", Error, [Resolver, Subject(rule)], seen.Where(one => Covers(rule, one))));
        }

        foreach (var template in world.Templates.Where(template => template.StandIns))
        {
            findings.Add(Finding(
                "standins-off",
                Error,
                [Resolver, Subject(template)],
                seen.Where(one => one.Template?.Id == template.Id)));
        }

        var holders = seen.Where(one => world.Holders.Contains(one.Client.Id)).ToArray();
        if (holders.Length > 0)
        {
            findings.Add(Finding("holders-off", Error, [Resolver], holders));
        }

        foreach (var group in seen.Where(one => one.Picks).GroupBy(one => Source(one)))
        {
            findings.Add(Finding("panel-off", Warning, [Resolver, group.Key], group));
        }
    }

    // The resolver is on: the clients whose files lose it to the port, the addresses it listens on or the firewall.
    private static void Lost(DnsWorld world, IReadOnlyList<Seen> seen, List<DnsFinding> findings)
    {
        var lost = seen.Where(one => one.Wanted && !one.Line.IsPanel).ToArray();
        if (lost.Length == 0)
        {
            return;
        }

        if (world.Settings.Port != DnsDefaults.Port)
        {
            findings.Add(Finding("port", Warning, [Resolver], lost));

            return;
        }

        foreach (var group in lost.GroupBy(one => one.Config.Id))
        {
            var config = group.First().Config;
            var code = world.KeptOut.Contains(config.Name, StringComparer.Ordinal) ? "kept" : "listen";
            findings.Add(Finding(code, Warning, [Resolver, Subject(config)], group));
        }
    }

    // The clients of the rules by name whose name servers lie outside the tunnel, and with hints the rest of them.
    private static void Outside(DnsWorld world, IReadOnlyList<Seen> seen, List<DnsFinding> findings, bool hints)
    {
        var outside = seen.Where(one => !one.Line.IsPanel && one.Outside.Count > 0).ToArray();
        foreach (var rule in world.Rules)
        {
            var missed = outside.Where(one => Covers(rule, one)).ToArray();
            if (missed.Length > 0)
            {
                findings.Add(Finding(
                    "rule-outside",
                    Warning,
                    [Subject(rule), .. missed.Select(Source).Distinct()],
                    missed));
            }
        }

        if (!hints)
        {
            return;
        }

        foreach (var one in outside.Where(one => !world.Rules.Any(rule => Covers(rule, one))))
        {
            findings.Add(Finding("outside", Hint, [Subject(one.Client)], [one]));
        }
    }

    // Looks at every client that is on, at an endpoint that is on.
    private static List<Seen> Look(DnsWorld world)
    {
        var endpoints = world.Configs.ToDictionary(one => one.Id);
        var templates = world.Templates.ToDictionary(one => one.Id);
        var seen = new List<Seen>();
        foreach (var client in world.Clients.Where(one => one.IsEnabled).OrderBy(one => one.Name, StringComparer.Ordinal))
        {
            if (!endpoints.TryGetValue(client.ConfigId, out var config) || !config.IsEnabled)
            {
                continue;
            }

            var template = client.TemplateId is { } id ? templates.GetValueOrDefault(id) : null;
            var line = Written(world, config, client, template);
            var wanted = Wants(world, config, client, template);
            var outside = Beyond(line.Servers, ClientText.Routed(config, client, template, line));
            seen.Add(new Seen(client, config, template, line, wanted, ClientText.Picks(config, client, template), outside));
        }

        return seen;
    }

    // Returns whether the file of a client would name the resolver were it on at port 53 on every address.
    private static bool Wants(DnsWorld world, ServerConfig config, TunnelClient client, ClientTemplate? template)
    {
        var own = DnsHandout.For(config, DnsDefaults.Settings with { IsEnabled = true });

        return ClientText.Dns(config, client, template, world.Needed ? own : null, own).IsPanel;
    }

    // Returns the name servers the file of a client names with the settings of the world.
    private static ClientDns Written(DnsWorld world, ServerConfig config, TunnelClient client, ClientTemplate? template)
    {
        var handing = world.Settings with { IsEnabled = world.Settings.IsEnabled && world.Needed };

        return ClientText.Dns(
            config,
            client,
            template,
            DnsHandout.For(config, handing, world.KeptOut),
            DnsHandout.For(config, world.Settings, world.KeptOut));
    }

    // Returns the name servers the ranges of a client leave out.
    private static IReadOnlyList<string> Beyond(IReadOnlyList<string> servers, IReadOnlyList<string> routed)
    {
        var ranges = routed
            .Select(text => AwgAllowedIp.TryParse(text, out var range) ? range : null)
            .OfType<AwgAllowedIp>()
            .ToList();

        return
        [
            .. servers.Where(server =>
                !IPAddress.TryParse(server, out var address) || !ranges.Exists(range => ClientPool.Inside(range, address))),
        ];
    }

    private static bool Covers(RouteRule rule, Seen one) =>
        (rule.Inbounds.Count == 0 || rule.Inbounds.Contains(one.Config.Name, StringComparer.Ordinal))
        && (rule.Clients.Count == 0 || rule.Clients.Contains(one.Client.Name, StringComparer.OrdinalIgnoreCase));

    private static DnsFinding Finding(string code, string level, IReadOnlyList<DnsSubject> subjects, IEnumerable<Seen> seen) =>
        new(code, level, subjects, [.. seen.Select(one => one.Affected)]);

    private static DnsSubject Source(Seen one) => one.Line.From switch
    {
        DnsFrom.Client => Subject(one.Client),
        DnsFrom.Template or DnsFrom.StandIns when one.Template is { } template => Subject(template),
        _ => Subject(one.Config),
    };

    private static DnsSubject Subject(RouteRule rule) => new(rule.Id < 0 ? "basic" : "rule", rule.Id, rule.Name);

    private static DnsSubject Subject(ClientTemplate template) => new("template", template.Id, template.Name);

    private static DnsSubject Subject(ServerConfig config) => new("config", config.Id, config.Name);

    private static DnsSubject Subject(TunnelClient client) => new("client", client.Id, client.Name);

    private sealed record Seen(
        TunnelClient Client,
        ServerConfig Config,
        ClientTemplate? Template,
        ClientDns Line,
        bool Wanted,
        bool Picks,
        IReadOnlyList<string> Outside)
    {
        public DnsAffected Affected => new(
            Client.Id,
            Client.Name,
            From(Line.From),
            Line.From switch
            {
                DnsFrom.Client => Client.Name,
                DnsFrom.Template or DnsFrom.StandIns => Template?.Name ?? string.Empty,
                _ => Config.Name,
            },
            Line.Servers);
    }
}
