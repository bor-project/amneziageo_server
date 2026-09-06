using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The user management commands of the utility.
/// </summary>
public static class UserCommands
{
    /// <summary>
    /// Runs one user command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct) => args.At(1) switch
    {
        "list" => await ListAsync(context, ct).ConfigureAwait(false),
        "add" => await AddAsync(context, args, ct).ConfigureAwait(false),
        "passwd" => await PasswordAsync(context, args, ct).ConfigureAwait(false),
        "role" => await RoleAsync(context, args, ct).ConfigureAwait(false),
        "enable" => await EnabledAsync(context, args, enabled: true, ct).ConfigureAwait(false),
        "disable" => await EnabledAsync(context, args, enabled: false, ct).ConfigureAwait(false),
        "remove" => await RemoveAsync(context, args, ct).ConfigureAwait(false),
        _ => Usage(),
    };

    /// <summary>
    /// Prints every account with its role and rights.
    /// </summary>
    public static async Task<int> ListAsync(Context context, CancellationToken ct)
    {
        var found = await context.Accounts.ListAsync(ct).ConfigureAwait(false);
        if (found.Count == 0)
        {
            Terminal.Say("no users yet");

            return 0;
        }

        Terminal.Say($"{"name",-20} {"kind",-6} {"role",-12} {"state",-9} rights");
        foreach (var view in found)
        {
            var rights = string.Join(" ", view.Scopes.Order(StringComparer.Ordinal));
            var role = view.Role is { Length: > 0 } ? view.Role : "none";
            Terminal.Say($"{view.Record.Name,-20} {view.Record.Kind.ToString().ToLowerInvariant(),-6} {role,-12} {(view.Record.IsEnabled ? "enabled" : "disabled"),-9} {rights}");
        }

        return 0;
    }

    /// <summary>
    /// Adds an account of the panel and sets its first password.
    /// </summary>
    public static async Task<int> AddAsync(Context context, Arguments args, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("login: ");
        if (await context.Accounts.WhyNotAsync(name, ct).ConfigureAwait(false) is { } refusal)
        {
            return Fail(refusal);
        }

        var password = args.Has("generate") ? Terminal.Generate() : Terminal.AskNewSecret();
        if (password is null)
        {
            return 1;
        }

        var result = await context.Accounts
            .AddAsync(name, args.Value("display"), args.Value("role"), password, !args.Has("permanent"), ct)
            .ConfigureAwait(false);

        if (!result.IsOk)
        {
            return Fail(result);
        }

        if (args.Has("generate"))
        {
            Terminal.Say($"password: {password}");
        }

        Terminal.Say($"added {name} as {args.Value("role") ?? "no role"}");

        return 0;
    }

    /// <summary>
    /// Replaces the password of an account.
    /// </summary>
    public static async Task<int> PasswordAsync(Context context, Arguments args, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("login: ");
        var password = args.Has("generate") ? Terminal.Generate() : Terminal.AskNewSecret();
        if (password is null)
        {
            return 1;
        }

        var result = await context.Accounts
            .SetPasswordAsync(name, password, !args.Has("permanent"), ct)
            .ConfigureAwait(false);

        if (!result.IsOk)
        {
            return Fail(result);
        }

        if (args.Has("generate"))
        {
            Terminal.Say($"password: {password}");
        }

        Terminal.Say($"password of {result.Record!.Name} replaced");

        return 0;
    }

    /// <summary>
    /// Gives an account a role, replacing the one it holds.
    /// </summary>
    public static async Task<int> RoleAsync(Context context, Arguments args, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("login: ");
        var role = args.At(3) ?? Terminal.Ask("role: ");
        var result = await context.Accounts.SetRoleAsync(name, role, actorId: 0, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Fail(result);
        }

        Terminal.Say($"{result.Record!.Name} is now {role}");

        return 0;
    }

    /// <summary>
    /// Turns an account on or off.
    /// </summary>
    public static async Task<int> EnabledAsync(Context context, Arguments args, bool enabled, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("login: ");
        var result = await context.Accounts.SetEnabledAsync(name, enabled, actorId: 0, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Fail(result);
        }

        Terminal.Say($"{result.Record!.Name} is {(enabled ? "enabled" : "disabled")}");

        return 0;
    }

    /// <summary>
    /// Removes an account with its sessions and tokens.
    /// </summary>
    public static async Task<int> RemoveAsync(Context context, Arguments args, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("login: ");
        if (!args.Has("yes") && !Terminal.Confirm($"remove {name}?"))
        {
            return 1;
        }

        var result = await context.Accounts.RemoveAsync(name, actorId: 0, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Fail(result);
        }

        Terminal.Say($"removed {result.Record!.Name}");

        return 0;
    }

    /// <summary>
    /// Returns why a name cannot be taken, or null when it can.
    /// </summary>
    public static async Task<string?> WhyNotAsync(Context context, string name, CancellationToken ct) =>
        (await context.Accounts.WhyNotAsync(name, ct).ConfigureAwait(false))?.Message;

    private static int Fail(AccountResult result)
    {
        Terminal.Fail(result.Message);

        return 1;
    }

    private static int Usage()
    {
        Terminal.Fail("usage: user list | add | passwd | role | enable | disable | remove");

        return 2;
    }
}
