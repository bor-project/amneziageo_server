using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The role management commands of the utility.
/// </summary>
public static class RoleCommands
{
    /// <summary>
    /// Runs one role command and returns the exit code.
    /// </summary>
    public static async Task<int> RunAsync(Context context, Arguments args, CancellationToken ct) => args.At(1) switch
    {
        "list" => await ListAsync(context, ct).ConfigureAwait(false),
        "add" => await AddAsync(context, args, ct).ConfigureAwait(false),
        "set" => await SetAsync(context, args, ct).ConfigureAwait(false),
        "remove" => await RemoveAsync(context, args, ct).ConfigureAwait(false),
        _ => Usage(),
    };

    /// <summary>
    /// Prints every role with the rights it carries.
    /// </summary>
    public static async Task<int> ListAsync(Context context, CancellationToken ct)
    {
        var found = await context.Catalog.ListAsync(ct).ConfigureAwait(false);
        Terminal.Say($"{"name",-16} {"title",-20} {"users",-6} rights");
        foreach (var view in found)
        {
            var rights = string.Join(" ", view.Scopes.Order(StringComparer.Ordinal));
            Terminal.Say($"{view.Record.Name,-16} {view.Record.Title,-20} {view.Users,-6} {rights}");
        }

        Terminal.Say($"rights the server knows: {string.Join(" ", Scopes.All)}");

        return 0;
    }

    /// <summary>
    /// Adds a role with the rights it carries.
    /// </summary>
    public static async Task<int> AddAsync(Context context, Arguments args, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("role: ");
        var scopes = args.Positional.Skip(3).ToArray();
        var result = await context.Catalog.AddAsync(name, args.Value("title"), scopes, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Fail(result);
        }

        Terminal.Say($"added {result.Record!.Name} with {(scopes.Length == 0 ? "no rights" : string.Join(" ", scopes))}");

        return 0;
    }

    /// <summary>
    /// Replaces the title of a role, the rights it carries, or both.
    /// </summary>
    public static async Task<int> SetAsync(Context context, Arguments args, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("role: ");
        var scopes = args.Positional.Skip(3).ToArray();
        var result = await context.Catalog
            .ChangeAsync(name, args.Value("title"), scopes.Length == 0 ? null : scopes, ct)
            .ConfigureAwait(false);

        if (!result.IsOk)
        {
            return Fail(result);
        }

        var view = await context.Catalog.FindAsync(name, ct).ConfigureAwait(false);
        Terminal.Say($"{name} carries {(view is null || view.Scopes.Count == 0 ? "no rights" : string.Join(" ", view.Scopes.Order(StringComparer.Ordinal)))}");

        return 0;
    }

    /// <summary>
    /// Removes a role no account is given.
    /// </summary>
    public static async Task<int> RemoveAsync(Context context, Arguments args, CancellationToken ct)
    {
        var name = args.At(2) ?? Terminal.Ask("role: ");
        if (!args.Has("yes") && !Terminal.Confirm($"remove {name}?"))
        {
            return 1;
        }

        var result = await context.Catalog.RemoveAsync(name, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Fail(result);
        }

        Terminal.Say($"removed {result.Record!.Name}");

        return 0;
    }

    private static int Fail(RoleResult result)
    {
        Terminal.Fail(result.Message);

        return 1;
    }

    private static int Usage()
    {
        Terminal.Fail("usage: role list | add | set | remove");

        return 2;
    }
}
