using System.Globalization;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The long lived token commands of the utility.
/// </summary>
public static class TokenCommands
{
    /// <summary>
    /// Runs one token command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct) => args.At(1) switch
    {
        "list" => await ListAsync(context, ct).ConfigureAwait(false),
        "add" => await AddAsync(context, args, ct).ConfigureAwait(false),
        "revoke" => await RevokeAsync(context, args, ct).ConfigureAwait(false),
        _ => Usage(),
    };

    /// <summary>
    /// Prints every token with the role it acts by.
    /// </summary>
    public static async Task<int> ListAsync(Context context, CancellationToken ct)
    {
        var found = await context.Tokens.ListAsync(ct).ConfigureAwait(false);
        if (found.Count == 0)
        {
            Terminal.Say("no tokens yet");

            return 0;
        }

        var now = TimeProvider.System.GetUtcNow();
        Terminal.Say($"{"id",-6} {"name",-24} {"role",-16} {"expires",-16} {"used",-16} state");
        foreach (var token in found)
        {
            var state = token.IsExpired(now) ? "expired" : "active";
            Terminal.Say($"{token.Id,-6} {token.Name,-24} {token.Role,-16} {Stamp(token.ExpiresUtc),-16} {Stamp(token.LastUsedUtc),-16} {state}");
        }

        return 0;
    }

    /// <summary>
    /// Mints a token that acts by a role and prints its secret once.
    /// </summary>
    public static async Task<int> AddAsync(Context context, Arguments args, CancellationToken ct)
    {
        var asked = args.At(2) is null;
        var name = args.At(2) ?? Terminal.Ask("name: ");
        var role = args.Value("role") ?? (asked ? Terminal.Ask("role: ") : string.Empty);
        var text = args.Value("days") ?? (asked ? Terminal.Ask("days, empty for no end: ") : string.Empty);
        var days = Days(text);
        if (text.Length > 0 && days is null)
        {
            Terminal.Fail("days take a whole number");

            return 2;
        }

        var result = await context.Tokens.MintAsync(name, role, days, actor: null, address: null, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            Terminal.Fail(result.Message);

            return 1;
        }

        var minted = result.Minted!;
        Terminal.Say($"minted {minted.Token.Id} as {minted.Token.Role}, expires {Stamp(minted.Token.ExpiresUtc)}");
        Terminal.Say(minted.Secret);

        return 0;
    }

    /// <summary>
    /// Revokes a token by its identifier.
    /// </summary>
    public static async Task<int> RevokeAsync(Context context, Arguments args, CancellationToken ct)
    {
        var text = args.At(2) ?? Terminal.Ask("token id: ");
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            Terminal.Fail("name the token by its id");

            return 2;
        }

        if (!args.Has("yes") && !Terminal.Confirm($"revoke token {id}?"))
        {
            return 1;
        }

        var result = await context.Tokens.RevokeAsync(id, actor: null, address: null, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            Terminal.Fail(result.Message);

            return 1;
        }

        Terminal.Say($"revoked {id}");

        return 0;
    }

    private static int? Days(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;

    private static string Stamp(DateTimeOffset? at) =>
        at is { } value ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "never";

    private static int Usage()
    {
        Terminal.Fail("usage: token list | add <name> --role <role> [--days <n>] | revoke <id> [--yes]");

        return 2;
    }
}
