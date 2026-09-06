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
        "grant" => await ScopesAsync(context, args, grant: true, ct).ConfigureAwait(false),
        "revoke" => await ScopesAsync(context, args, grant: false, ct).ConfigureAwait(false),
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
        var found = await context.Principals.ListAsync(ct).ConfigureAwait(false);
        if (found.Count == 0)
        {
            Terminal.Say("no users yet");

            return 0;
        }

        Terminal.Say($"{"name",-20} {"kind",-6} {"role",-9} {"state",-9} rights");
        foreach (var item in found)
        {
            var extra = item.Extra.Count == 0 ? string.Empty : string.Join(" ", item.Extra.Order(StringComparer.Ordinal));
            Terminal.Say($"{item.Name,-20} {item.Kind.ToString().ToLowerInvariant(),-6} {Roles.Text(item.Role),-9} {(item.IsEnabled ? "enabled" : "disabled"),-9} {extra}");
        }

        return 0;
    }

    /// <summary>
    /// Adds an account of the panel and sets its first password.
    /// </summary>
    public static async Task<int> AddAsync(Context context, Arguments args, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("login: ");
        var refusal = await WhyNotAsync(context, name, ct).ConfigureAwait(false);
        if (refusal is not null)
        {
            Terminal.Fail(refusal);

            return 1;
        }

        var role = Roles.Parse(args.Value("role") ?? "viewer");
        var password = args.Has("generate") ? Terminal.Generate() : Terminal.AskNewSecret();
        if (password is null)
        {
            return 1;
        }

        var complaint = AccountRules.CheckPassword(password);
        if (complaint is not null)
        {
            Terminal.Fail(complaint);

            return 1;
        }

        var record = await context.Principals
            .AddAsync(name, args.Value("display") ?? name, role, ct)
            .ConfigureAwait(false);

        await context.Passwords
            .SetAsync(record.Id, password, !args.Has("permanent"), DateTimeOffset.UtcNow, ct)
            .ConfigureAwait(false);

        if (args.Has("generate"))
        {
            Terminal.Say($"password: {password}");
        }

        Terminal.Say($"added {name} as {Roles.Text(role)}");

        return 0;
    }

    /// <summary>
    /// Replaces the password of an account.
    /// </summary>
    public static async Task<int> PasswordAsync(Context context, Arguments args, CancellationToken ct)
    {
        var record = await FindAsync(context, args.At(2) ?? Terminal.Ask("login: "), ct).ConfigureAwait(false);
        if (record is null)
        {
            return 1;
        }

        if (record.Kind == PrincipalKind.Host)
        {
            Terminal.Fail("a host account signs in by the host, not by a password");

            return 1;
        }

        var password = args.Has("generate") ? Terminal.Generate() : Terminal.AskNewSecret();
        if (password is null)
        {
            return 1;
        }

        var complaint = AccountRules.CheckPassword(password);
        if (complaint is not null)
        {
            Terminal.Fail(complaint);

            return 1;
        }

        await context.Passwords
            .SetAsync(record.Id, password, !args.Has("permanent"), DateTimeOffset.UtcNow, ct)
            .ConfigureAwait(false);

        if (args.Has("generate"))
        {
            Terminal.Say($"password: {password}");
        }

        Terminal.Say($"password of {record.Name} replaced");

        return 0;
    }

    /// <summary>
    /// Sets the role of an account.
    /// </summary>
    public static async Task<int> RoleAsync(Context context, Arguments args, CancellationToken ct)
    {
        var record = await FindAsync(context, args.At(2) ?? Terminal.Ask("login: "), ct).ConfigureAwait(false);
        if (record is null)
        {
            return 1;
        }

        var text = args.At(3) ?? Terminal.Ask("role [none|viewer|operator|admin]: ");
        if (!Enum.TryParse<Role>(text, ignoreCase: true, out var role))
        {
            Terminal.Fail($"there is no role '{text}'");

            return 1;
        }

        if (record.Role == Role.Admin && role != Role.Admin && !await AnotherAdminAsync(context, record.Id, ct).ConfigureAwait(false))
        {
            Terminal.Fail("this is the last administrator");

            return 1;
        }

        await context.Principals.SetRoleAsync(record.Id, role, ct).ConfigureAwait(false);
        Terminal.Say($"{record.Name} is now {Roles.Text(role)}");

        return 0;
    }

    /// <summary>
    /// Adds rights on top of the role, or takes them back.
    /// </summary>
    public static async Task<int> ScopesAsync(Context context, Arguments args, bool grant, CancellationToken ct)
    {
        var record = await FindAsync(context, args.At(2) ?? Terminal.Ask("login: "), ct).ConfigureAwait(false);
        if (record is null)
        {
            return 1;
        }

        var asked = args.Positional.Skip(3).ToArray();
        if (asked.Length == 0)
        {
            Terminal.Fail($"name the rights: {string.Join(" ", Scopes.All)}");

            return 1;
        }

        var unknown = asked.Where(scope => !Scopes.Known(scope)).ToArray();
        if (unknown.Length > 0)
        {
            Terminal.Fail($"there are no rights called {string.Join(" ", unknown)}");

            return 1;
        }

        var extra = record.Extra.ToHashSet(StringComparer.Ordinal);
        foreach (var scope in asked)
        {
            if (grant)
            {
                extra.Add(scope);
            }
            else
            {
                extra.Remove(scope);
            }
        }

        await context.Principals.SetExtraAsync(record.Id, extra, ct).ConfigureAwait(false);
        Terminal.Say($"{record.Name} holds {Roles.Text(record.Role)} and {(extra.Count == 0 ? "nothing on top" : string.Join(" ", extra.Order(StringComparer.Ordinal)))}");

        return 0;
    }

    /// <summary>
    /// Turns an account on or off.
    /// </summary>
    public static async Task<int> EnabledAsync(Context context, Arguments args, bool enabled, CancellationToken ct)
    {
        var record = await FindAsync(context, args.At(2) ?? Terminal.Ask("login: "), ct).ConfigureAwait(false);
        if (record is null)
        {
            return 1;
        }

        if (!enabled && record.Role == Role.Admin && !await AnotherAdminAsync(context, record.Id, ct).ConfigureAwait(false))
        {
            Terminal.Fail("this is the last administrator");

            return 1;
        }

        await context.Principals.SetEnabledAsync(record.Id, enabled, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        Terminal.Say($"{record.Name} is {(enabled ? "enabled" : "disabled")}");

        return 0;
    }

    /// <summary>
    /// Removes an account with its sessions and tokens.
    /// </summary>
    public static async Task<int> RemoveAsync(Context context, Arguments args, CancellationToken ct)
    {
        var record = await FindAsync(context, args.At(2) ?? Terminal.Ask("login: "), ct).ConfigureAwait(false);
        if (record is null)
        {
            return 1;
        }

        if (record.Role == Role.Admin && !await AnotherAdminAsync(context, record.Id, ct).ConfigureAwait(false))
        {
            Terminal.Fail("this is the last administrator");

            return 1;
        }

        if (!args.Has("yes") && !Terminal.Confirm($"remove {record.Name}?"))
        {
            return 1;
        }

        await context.Principals.RemoveAsync(record.Id, ct).ConfigureAwait(false);
        Terminal.Say($"removed {record.Name}");

        return 0;
    }

    /// <summary>
    /// Returns why a name cannot be taken, or null when it can.
    /// </summary>
    public static async Task<string?> WhyNotAsync(Context context, string name, CancellationToken ct)
    {
        var shape = AccountRules.CheckName(name);
        if (shape is not null)
        {
            return shape;
        }

        if (LocalUsers.Find(name) is not null)
        {
            return $"the host carries a user called '{name}', that name is left to it";
        }

        if (await context.Principals.FindAsync(name, ct).ConfigureAwait(false) is not null)
        {
            return $"the panel already carries a user called '{name}'";
        }

        return null;
    }

    private static async Task<PrincipalRecord?> FindAsync(Context context, string name, CancellationToken ct)
    {
        var record = await context.Principals.FindAsync(name, ct).ConfigureAwait(false);
        if (record is null)
        {
            Terminal.Fail($"there is no user called '{name}'");
        }

        return record;
    }

    private static async Task<bool> AnotherAdminAsync(Context context, long id, CancellationToken ct)
    {
        var found = await context.Principals.ListAsync(ct).ConfigureAwait(false);

        return found.Any(item => item.Id != id && item.Role == Role.Admin && item.IsEnabled);
    }

    private static int Usage()
    {
        Terminal.Fail("usage: user list | add | passwd | role | grant | revoke | enable | disable | remove");

        return 2;
    }
}
