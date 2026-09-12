using System.Buffers;
using System.Globalization;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Hello;

/// <summary>
/// The routes a client opens to know what server it reached and to measure the channel against it.
/// </summary>
public static class HelloEndpoints
{
    /// <summary>
    /// The path the point of the server sits at.
    /// </summary>
    public const string Path = "/api/hello";

    /// <summary>
    /// The path the legs of a measurement sit under.
    /// </summary>
    public const string SpeedPath = "/api/speed";

    private const int Chunk = 64 * 1024;

    /// <summary>
    /// Maps the point of the server and the legs of a measurement.
    /// </summary>
    public static IEndpointRouteBuilder MapHello(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet(Path, Greet);
        routes.MapPost(Path, ProveAsync);
        routes.MapGet(SpeedPath + "/down", DownAsync);
        routes.MapPost(SpeedPath + "/up", UpAsync);

        return routes;
    }

    private static IResult Greet(SpeedTickets tickets) =>
        Results.Ok(new HelloResponse(FeatureNames.Server, Version(), tickets.Challenge()));

    private static async Task<IResult> ProveAsync(
        HelloRequest request,
        HttpContext context,
        ClientStore clients,
        ConfigStore configs,
        SubscriptionState subscriptions,
        PanelSettings panel,
        WebOptions options,
        SpeedTickets tickets,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await clients.FindByKeyAsync(Trim(request.Key), ct).ConfigureAwait(false);
        if (client is null || !client.IsEnabled)
        {
            return Refuse("unknown-peer", "the panel carries no such peer");
        }

        var endpoint = await configs.FindAsync(client.ConfigId, ct).ConfigureAwait(false);
        if (endpoint is null || !endpoint.IsEnabled)
        {
            return Refuse("unknown-peer", "the endpoint of that peer is not up");
        }

        if (!tickets.Takes(Trim(request.Challenge)))
        {
            return Refuse("stale-challenge", "that challenge was not handed out or it has run out");
        }

        if (!PeerProof.Holds(endpoint.PrivateKey, client.PublicKey, Trim(request.Challenge), Trim(request.Proof)))
        {
            return Refuse("bad-proof", "the answer does not come from the key of that peer");
        }

        var features = new List<string>();
        var subscription = Subscription(subscriptions, panel, options, context, client.SubscriptionId, client.PrivateKey.Length > 0);
        if (subscription is not null)
        {
            features.Add(FeatureNames.Subscription);
        }

        var speed = endpoint.Speed ? Speed(context, tickets.Mint(client.Id)) : null;
        if (speed is not null)
        {
            features.Add(FeatureNames.Speed);
        }

        return Results.Ok(new FeatureResponse(
            FeatureNames.Server,
            Version(),
            client.Name,
            features,
            subscription,
            speed));
    }

    private static async Task DownAsync(HttpContext context, SpeedTickets tickets, long? bytes)
    {
        if (tickets.Find(context.Request.Query["ticket"]) is not { } ticket)
        {
            await SayAsync(context, StatusCodes.Status403Forbidden, "unknown-ticket", "that pass is not one of ours").ConfigureAwait(false);

            return;
        }

        if (!tickets.Enter(ticket.Value))
        {
            await SayAsync(context, StatusCodes.Status429TooManyRequests, "measuring", "that pass is already measuring").ConfigureAwait(false);

            return;
        }

        try
        {
            var size = Math.Clamp(bytes ?? SpeedTickets.DefaultBytes, 1, SpeedTickets.MaxBytes);
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength = size;
            context.Response.Headers.CacheControl = "no-store";

            var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
            try
            {
                Array.Clear(buffer);
                for (var left = size; left > 0 && !context.RequestAborted.IsCancellationRequested;)
                {
                    var step = (int)Math.Min(left, Chunk);
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, step), context.RequestAborted).ConfigureAwait(false);
                    left -= step;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            tickets.Leave(ticket.Value);
        }
    }

    private static async Task UpAsync(HttpContext context, SpeedTickets tickets)
    {
        if (tickets.Find(context.Request.Query["ticket"]) is not { } ticket)
        {
            await SayAsync(context, StatusCodes.Status403Forbidden, "unknown-ticket", "that pass is not one of ours").ConfigureAwait(false);

            return;
        }

        if (!tickets.Enter(ticket.Value))
        {
            await SayAsync(context, StatusCodes.Status429TooManyRequests, "measuring", "that pass is already measuring").ConfigureAwait(false);

            return;
        }

        var taken = 0L;
        var buffer = ArrayPool<byte>.Shared.Rent(Chunk);

        try
        {
            while (taken < SpeedTickets.MaxBytes)
            {
                var step = await context.Request.Body
                    .ReadAsync(buffer.AsMemory(0, Chunk), context.RequestAborted)
                    .ConfigureAwait(false);
                if (step == 0)
                {
                    break;
                }

                taken += step;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            tickets.Leave(ticket.Value);
        }

        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new { bytes = taken }, context.RequestAborted).ConfigureAwait(false);
    }

    private static SubscriptionFeature? Subscription(
        SubscriptionState subscriptions,
        PanelSettings panel,
        WebOptions options,
        HttpContext context,
        string subscriptionId,
        bool carries)
    {
        var address = SubscriptionAnswer.Address(
            subscriptions.Current,
            panel,
            Listening.Chain(options, panel).Length > 0,
            context.Request.Host.Host,
            carries ? subscriptionId : string.Empty);

        return address.Length == 0
            ? null
            : new SubscriptionFeature(address, subscriptions.Current.UpdateHours);
    }

    private static SpeedFeature Speed(HttpContext context, SpeedTicket ticket)
    {
        var root = context.Request.Scheme + "://" + context.Request.Host.Value + SpeedPath;
        var bytes = SpeedTickets.DefaultBytes.ToString(CultureInfo.InvariantCulture);

        return new SpeedFeature(
            root + "/down?bytes=" + bytes + "&ticket=" + ticket.Value,
            root + "/up?ticket=" + ticket.Value,
            SpeedTickets.MaxBytes,
            ticket.Expires);
    }

    private static string Version() =>
        typeof(HelloEndpoints).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();

    private static IResult Refuse(string code, string message) =>
        Results.Json(new Failure(code, message), statusCode: StatusCodes.Status403Forbidden);

    private static async Task SayAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new Failure(code, message), context.RequestAborted).ConfigureAwait(false);
    }
}
