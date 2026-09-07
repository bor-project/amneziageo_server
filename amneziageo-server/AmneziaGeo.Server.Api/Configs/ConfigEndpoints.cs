using System.Security.Cryptography;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Configs;

/// <summary>
/// The routes the server endpoints are managed through.
/// </summary>
public static class ConfigEndpoints
{
    /// <summary>
    /// Maps the endpoint management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapConfigs(this IEndpointRouteBuilder routes)
    {
        var reading = routes.MapGroup("/api/configs").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/{id:long}", FindAsync);

        var writing = routes.MapGroup("/api/configs").RequireScope(Scopes.ManageInterfaces);
        writing.MapGet("/draft", DraftAsync);
        writing.MapPost("/keys", Keys);
        writing.MapPost("/preshared", Preshared);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(HttpContext context, ConfigStore store, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var secrets = Secrets(context);

        return Results.Ok(found.Select(config => ConfigAnswers.Config(config, secrets)).ToArray());
    }

    private static async Task<IResult> FindAsync(long id, HttpContext context, ConfigStore store, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);

        return found is null
            ? Refuse(StatusCodes.Status404NotFound, "unknown-config", $"there is no endpoint under the number {id}")
            : Results.Ok(ConfigAnswers.Config(found, Secrets(context)));
    }

    private static async Task<IResult> DraftAsync(string? name, ConfigStore store, CancellationToken ct)
    {
        var fresh = ConfigDefaults.Fresh(string.IsNullOrWhiteSpace(name) ? "awg0" : name.Trim());
        var port = await store.FreePortAsync(fresh.ListenPort, ct).ConfigureAwait(false);

        return Results.Ok(ConfigAnswers.Config(fresh with { ListenPort = port }, true));
    }

    private static IResult Keys()
    {
        var pair = Curve25519.Create();

        return Results.Ok(new KeyPairResponse(pair.PrivateKey, pair.PublicKey));
    }

    private static IResult Preshared() =>
        Results.Ok(new KeyResponse(Convert.ToBase64String(RandomNumberGenerator.GetBytes(Curve25519.KeySize))));

    private static async Task<IResult> AddAsync(ConfigRequest request, ConfigStore store, CancellationToken ct)
    {
        var result = await store.AddAsync(ConfigAnswers.Draft(request), ct).ConfigureAwait(false);

        return result.IsOk
            ? Results.Created($"/api/configs/{result.Record!.Id}", ConfigAnswers.Config(result.Record, true))
            : Explain(result);
    }

    private static async Task<IResult> ChangeAsync(long id, ConfigRequest request, ConfigStore store, CancellationToken ct)
    {
        var result = await store.ChangeAsync(id, ConfigAnswers.Draft(request), ct).ConfigureAwait(false);

        return result.IsOk ? Results.Ok(ConfigAnswers.Config(result.Record!, true)) : Explain(result);
    }

    private static async Task<IResult> RemoveAsync(long id, ConfigStore store, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);

        return result.IsOk ? Results.NoContent() : Explain(result);
    }

    private static bool Secrets(HttpContext context) =>
        context.Caller()?.Holds(Scopes.ManageInterfaces) == true;

    private static IResult Explain(ConfigResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(ConfigOutcome outcome) => outcome switch
    {
        ConfigOutcome.Unknown => StatusCodes.Status404NotFound,
        ConfigOutcome.NameTaken or ConfigOutcome.PortTaken => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
