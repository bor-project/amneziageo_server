using System.Text.Json;
using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that set the panel up and hand out tokens.
/// </summary>
public static class LoginCommands
{
    /// <summary>
    /// Makes the first administrator while the panel carries none.
    /// </summary>
    public static async Task<int> InitAsync(Context context, Arguments args, CancellationToken ct)
    {
        if (await context.Accounts.HasAdminAsync(ct).ConfigureAwait(false))
        {
            Terminal.Fail("the panel already carries an administrator, this window is closed");

            return 1;
        }

        return args.Value("user") is { Length: > 0 } name
            ? await LocalAdminAsync(context, args, name, ct).ConfigureAwait(false)
            : await HostAdminAsync(context, args, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Signs the host account the utility runs as in and prints its tokens.
    /// </summary>
    public static async Task<int> LoginAsync(Context context, Arguments args, CancellationToken ct)
    {
        var result = args.Value("user") is { Length: > 0 } name
            ? await context.Login.PasswordAsync(name, Terminal.AskSecret("password: "), Address(), "cli", ct).ConfigureAwait(false)
            : await context.Login.HostAsync(Address(), "cli", ct).ConfigureAwait(false);

        return Print(context, result, args.Has("json"));
    }

    /// <summary>
    /// Trades a refresh token for a fresh pair.
    /// </summary>
    public static async Task<int> RefreshAsync(Context context, Arguments args, CancellationToken ct)
    {
        var token = args.At(1) ?? Terminal.Ask("refresh token: ");
        var result = await context.Login.RefreshAsync(token, ct).ConfigureAwait(false);

        return Print(context, result, args.Has("json"));
    }

    /// <summary>
    /// Prints the host account the utility runs as and what the panel makes of it.
    /// </summary>
    public static async Task<int> WhoAsync(Context context, CancellationToken ct)
    {
        if (!LocalUsers.IsSupported)
        {
            Terminal.Fail("the host does not carry an account database this build can read");

            return 1;
        }

        var user = LocalUsers.Current();
        if (user is null)
        {
            Terminal.Fail("the host does not know the account this process runs as");

            return 1;
        }

        var record = context.Login.FindByHostUser(user.Name);
        var view = record is null ? null : await context.Accounts.ViewAsync(record).ConfigureAwait(false);
        Terminal.Say($"host user:  {user.Name} (uid {user.Uid})");
        Terminal.Say($"host grants: {context.Login.HostRole(user) ?? "no role"}");
        Terminal.Say($"registered: {(view is null ? "no" : $"yes, {(view.Role is { Length: > 0 } ? view.Role : "no role")}, {(view.Record.IsEnabled ? "enabled" : "disabled")}")}");
        Terminal.Say($"host login: {context.Options.HostLogin.ToString().ToLowerInvariant()}");

        return 0;
    }

    /// <summary>
    /// Returns what an outcome means in words.
    /// </summary>
    public static string Explain(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.UnknownUser => "there is no such user",
        LoginOutcome.WrongPassword => "the password does not match",
        LoginOutcome.NoPassword => "this account carries no password",
        LoginOutcome.Disabled => "this account is disabled",
        LoginOutcome.Locked => "this account is locked after too many wrong passwords",
        LoginOutcome.NoHostUser => "the host does not know the account this process runs as",
        LoginOutcome.NameTaken => "the panel carries a user of that name that is not this host account",
        LoginOutcome.Unsupported => "the host does not carry an account database this build can read",
        LoginOutcome.HostLoginOff => "host accounts do not sign in on this server",
        LoginOutcome.NoRole => "no group of the host maps this account to a role",
        LoginOutcome.RefreshExpired => "the refresh token has expired",
        LoginOutcome.RefreshReplayed => "the refresh token was already spent, the session is closed",
        LoginOutcome.SessionEnded => "the session is closed",
        LoginOutcome.RefreshUnknown => "the refresh token is unknown",
        _ => "signed in",
    };

    private static async Task<int> HostAdminAsync(Context context, Arguments args, CancellationToken ct)
    {
        if (context.Options.HostLogin == HostLogin.Off)
        {
            Terminal.Fail("host accounts do not sign in on this server, name the administrator with --user");

            return 1;
        }

        if (!LocalUsers.IsSupported || LocalUsers.Current() is not { } user)
        {
            Terminal.Fail("the host does not know the account this process runs as, name one with --user");

            return 1;
        }

        if (await context.Users.FindByNameAsync(user.Name).ConfigureAwait(false) is not null)
        {
            Terminal.Fail($"the panel already carries a user called '{user.Name}'");

            return 1;
        }

        var registered = await context.Accounts
            .RegisterHostUserAsync(user.Name, user.Uid, Roles.Admin, ct)
            .ConfigureAwait(false);

        if (!registered.IsOk)
        {
            Terminal.Fail(registered.Message);

            return 1;
        }

        Terminal.Say($"{user.Name} is the administrator");
        Terminal.Say($"put the other host users into the {context.Options.HostGroupNames} groups to let them in");

        var result = await context.Login.HostAsync(Address(), "cli", ct).ConfigureAwait(false);

        return Print(context, result, args.Has("json"));
    }

    private static async Task<int> LocalAdminAsync(Context context, Arguments args, string name, CancellationToken ct)
    {
        var refusal = await UserCommands.WhyNotAsync(context, name, ct).ConfigureAwait(false);
        if (refusal is not null)
        {
            Terminal.Fail(refusal);

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

        var result = await context.Accounts
            .AddAsync(name, name, Roles.Admin, password, !args.Has("permanent"), ct)
            .ConfigureAwait(false);

        if (!result.IsOk)
        {
            Terminal.Fail(result.Message);

            return 1;
        }

        if (args.Has("generate"))
        {
            Terminal.Say($"password: {password}");
        }

        Terminal.Say($"{name} is the administrator");

        return 0;
    }

    private static int Print(Context context, LoginResult result, bool json)
    {
        if (!result.IsOk)
        {
            Terminal.Fail(Explain(result.Outcome));

            return 1;
        }

        if (result.MustChangePassword)
        {
            Terminal.Fail("this password has to be replaced before the account is of any use");
        }

        if (json)
        {
            Terminal.Say(JsonSerializer.Serialize(new
            {
                access = result.Access,
                refresh = result.Refresh,
                expires_in = (int)context.Options.AccessLifetime.TotalSeconds,
                scopes = Rights(result),
            }));

            return 0;
        }

        Terminal.Say($"user:    {result.Principal?.Name}");
        Terminal.Say($"rights:  {string.Join(" ", Rights(result))}");
        Terminal.Say($"access:  {result.Access}");
        Terminal.Say($"refresh: {result.Refresh ?? "none"}");

        return 0;
    }

    private static string[] Rights(LoginResult result) =>
        result.Principal is null ? [] : [.. result.Principal.Scopes.Order(StringComparer.Ordinal)];

    private static string? Address()
    {
        var connection = Environment.GetEnvironmentVariable("SSH_CONNECTION");

        return connection?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }
}
