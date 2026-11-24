using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Diagnostics;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Api.Clients;

public static partial class ClientEndpoints
{
    private static async Task<IResult> InfoAsync(
        long id,
        int? days,
        HttpContext context,
        ConfigStore configs,
        ClientStore clients,
        TemplateStore templates,
        PresetStore presets,
        DnsStore dns,
        DnsState resolver,
        TrafficStore traffic,
        ClientHost host,
        ClientGuard guard,
        TrafficLedger ledger,
        TimeProvider time,
        CancellationToken ct)
    {
        var count = days ?? 30;
        if (count is not (7 or 30 or 90))
        {
            return Refuse(StatusCodes.Status400BadRequest, "bad-statistics-period", "the traffic history period is 7, 30 or 90 days");
        }

        var client = await clients.FindAsync(id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-client", $"there is no client under the number {id}");
        }

        var config = await configs.FindAsync(client.ConfigId, ct).ConfigureAwait(false);
        var template = await templates.EffectiveAsync(client.TemplateId, ct).ConfigureAwait(false);
        var state = config is null ? ClientState.Missing(client) : Seen(host.States(config, [client])[0], config, client, guard);
        var snapshot = ledger.Snapshot(client);
        var totals = snapshot.Traffic;
        var observation = snapshot.Observation;
        var day = snapshot.Day;
        var history = await traffic.DaysAsync(id, day.AddDays(1 - count), day, ct).ConfigureAwait(false);
        var rows = history.Select(row => new ClientTrafficDayInfo(row.Day, row.Used.Rx, row.Used.Tx)).ToList();
        if (observation.TodayKnown)
        {
            rows.RemoveAll(row => row.Day == day);
            rows.Add(new ClientTrafficDayInfo(day, totals.Used.Rx, totals.Used.Tx));
        }
        var today = rows.FirstOrDefault(row => row.Day == day);
        var recent = ClientInformation.Recent(observation.ObservedUtc, time.GetUtcNow());
        var measured = state.IsPresent && recent && observation.RateKnown;
        var available = context.Caller()?.Holds(Scopes.ManageAccess) == true;
        var access = available ? context.RequestServices.GetService<AccessHost>() : null;
        var configuration = default(ClientConfigurationInfo);
        if (config is not null)
        {
            var held = await dns.ReadAsync(ct).ConfigureAwait(false);
            var names = DnsHandout.For(config, resolver.Handing ?? held with { IsEnabled = false }, resolver.KeptOut);
            var picked = DnsHandout.For(config, resolver.Chosen ?? held, resolver.KeptOut);
            var line = ClientText.Dns(config, client, template, names, picked, held.Files);
            var assigned = await presets.ListAsync(template?.Presets ?? [], ct).ConfigureAwait(false);
            configuration = ClientInformation.Configuration(config, client, template, line,
                [.. assigned.Select(one => new ClientInfoReference(one.Id, one.Name))]);
        }

        return Results.Ok(new ClientInfoResponse(
            client.Id, client.Name, client.IsEnabled, client.CreatedUtc, client.UpdatedUtc,
            new ClientConnectionInfo(state.IsOnline, state.IsPresent, state.LastHandshake, observation.LastActivityUtc,
                null, state.Endpoint.Length > 0 ? state.Endpoint : null,
                config is null ? [] : guard.Cut(config.Name, client.PublicKey),
                ClientInformation.Reason(client, config, state, totals)),
            new ClientTrafficInfo(state.IsPresent ? state.RxBytes : null, state.IsPresent ? state.TxBytes : null,
                measured ? totals.Rate.Rx : null, measured ? totals.Rate.Tx : null, today?.RxBytes, today?.TxBytes,
                day, time.LocalTimeZone.Id, [.. rows.OrderBy(row => row.Day)], totals.Total),
            ClientInformation.Limits(client, template, totals.Limits), configuration,
            new ClientStatisticsInfo(available, access?.Settings.IsEnabled, access?.IsRunning, access?.Fault)));
    }

    private static async Task<IResult> StatisticsAsync(
        long id,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ClientStore clients,
        ConfigStore configs,
        AccessRecords records,
        AccessHost host,
        TimeProvider time,
        CancellationToken ct)
    {
        var end = to ?? time.GetUtcNow();
        var start = ClientInformation.PeriodStart(from, end);
        if (start >= end || end - start > TimeSpan.FromDays(90))
        {
            return Refuse(StatusCodes.Status400BadRequest, "bad-statistics-period", "the statistics period is positive and no longer than 90 days");
        }
        var client = await clients.FindAsync(id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Refuse(StatusCodes.Status404NotFound, "unknown-client", $"there is no client under the number {id}");
        }
        var config = await configs.FindAsync(client.ConfigId, ct).ConfigureAwait(false);
        var query = ClientInformation.Access(client, config, start, end);
        var summary = await Task.Run(() => records.Summary(query, AmneziaGeo.Server.Routing.Access.AccessGrouping.Domain), ct)
            .ConfigureAwait(false);
        return Results.Ok(new ClientAccessStatistics(start, end, ClientInformation.BindingSince(client, config),
            host.Settings.IsEnabled, host.IsRunning, host.Fault, summary));
    }
}
