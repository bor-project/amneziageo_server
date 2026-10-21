using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Rules;

/// <summary>
/// Names a client or an endpoint in the rules a request lists.
/// </summary>
public static class RuleNaming
{
    /// <summary>
    /// Returns the rules a request lists for a new client or endpoint together with the rules that name it already,
    /// none when it lists none.
    /// </summary>
    public static async Task<IReadOnlyList<long>?> AddedAsync(
        RouteStore store,
        RuleSide side,
        string name,
        IReadOnlyList<long>? listed,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (listed is not { Count: > 0 })
        {
            return null;
        }

        var held = RuleSides.Naming(await store.ListAsync(ct).ConfigureAwait(false), side, name);

        return [.. listed.Concat(held).Distinct()];
    }

    /// <summary>
    /// Returns the refusal of the rules a request lists for the client or the endpoint under the name, or null when
    /// the caller may write them; a request that lists none leaves the rules alone.
    /// </summary>
    public static async Task<IResult?> RefuseAsync(
        HttpContext http,
        RouteStore store,
        RuleSide side,
        string name,
        IReadOnlyList<long>? wanted,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(store);

        if (wanted is null || Same(RuleSides.Naming(await store.ListAsync(ct).ConfigureAwait(false), side, name), wanted))
        {
            return null;
        }

        if (http.Caller()?.Holds(Scopes.ManageRouting) != true)
        {
            return Results.Json(
                new Failure("forbidden", "the account does not hold " + Scopes.ManageRouting),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var refusal = await store.RefuseNamingAsync(side, name, wanted, ct).ConfigureAwait(false);

        return refusal is null
            ? null
            : Results.Json(new Failure(refusal.Code, refusal.Message), statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Names the client or the endpoint in the rules a request lists and tells whether the rules changed.
    /// </summary>
    public static async Task<bool> NameAsync(
        RouteStore store,
        RuleSide side,
        string name,
        IReadOnlyList<long>? wanted,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (wanted is null || Same(RuleSides.Naming(await store.ListAsync(ct).ConfigureAwait(false), side, name), wanted))
        {
            return false;
        }

        await store.NameInAsync(side, name, wanted, ct).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Tells whether a rule that is on names one of the clients or the endpoints.
    /// </summary>
    public static bool Names(IReadOnlyList<RouteRule> rules, RuleSide side, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return names.Any(name => rules.Any(rule => rule.IsEnabled && rule.Of(side).Contains(name, side.Comparer())));
    }

    private static bool Same(IReadOnlyList<long> held, IReadOnlyList<long> wanted) =>
        held.Order().SequenceEqual(wanted.Distinct().Order());
}
