using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// What carrying an account to the host left.
/// </summary>
/// <param name="IsDone">Whether the host took the account.</param>
/// <param name="Message">What the host answered when it refused.</param>
public sealed record HostUserSync(bool IsDone, string Message)
{
    /// <summary>
    /// The answer of a host that took the account.
    /// </summary>
    public static readonly HostUserSync Done = new(true, string.Empty);
}

/// <summary>
/// Carries the privileged accounts of the panel to the users of the host.
/// </summary>
public sealed class HostUsers
{
    private const string OwnerTool = "chown";

    private const string GroupTool = "chgrp";

    private const string ModeTool = "chmod";

    private readonly IHostCommands _commands;

    private readonly AuthOptions _options;

    private readonly string _database;

    /// <summary>
    /// ctor
    /// </summary>
    public HostUsers(IHostCommands commands, AuthOptions options, string? database = null)
    {
        _commands = commands;
        _options = options;
        _database = database is { Length: > 0 } set ? set : ServerDatabase.DefaultPath();
    }

    /// <summary>
    /// Tells whether the host carries users this service reaches.
    /// </summary>
    public static bool IsSupported => LocalUsers.IsSupported;

    /// <summary>
    /// Puts a user of the host behind an account, adding it where the host carries none.
    /// </summary>
    public async Task<HostUserSync> CarryAsync(
        string name,
        string? role,
        string? key,
        IReadOnlyList<string> roles,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (!IsSupported)
        {
            return new HostUserSync(false, "the host keeps no users of its own");
        }

        foreach (var group in HostUserPlan.Groups(role))
        {
            var made = await RunAsync(HostUserPlan.AddGroup, ["--force", group], ct).ConfigureAwait(false);
            if (!made.IsOk)
            {
                return Refused(made);
            }
        }

        var held = LocalUsers.Find(name);
        var carried = held is null
            ? await RunAsync(HostUserPlan.AddUser, HostUserPlan.Add(name, role), ct).ConfigureAwait(false)
            : await RunAsync(HostUserPlan.ChangeUser, HostUserPlan.Join(name, role), ct).ConfigureAwait(false);
        if (!carried.IsOk)
        {
            return Refused(carried);
        }

        await OpenAsync(name, ct).ConfigureAwait(false);
        await LeaveAsync(name, Others(role, roles), ct).ConfigureAwait(false);
        if (key is { Length: > 0 })
        {
            var written = await KeyAsync(name, key, ct).ConfigureAwait(false);
            if (!written.IsDone)
            {
                return written;
            }
        }

        return await FilesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the way into the host for a user and takes it out of the groups of the panel.
    /// </summary>
    public async Task<HostUserSync> ShutAsync(string name, IReadOnlyList<string> roles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (!IsSupported || LocalUsers.Find(name) is null)
        {
            return HostUserSync.Done;
        }

        var shut = await RunAsync(HostUserPlan.ChangeUser, HostUserPlan.Shut(name), ct).ConfigureAwait(false);
        await LeaveAsync(name, Every(roles), ct).ConfigureAwait(false);

        return shut.IsOk ? HostUserSync.Done : Refused(shut);
    }

    /// <summary>
    /// Takes a user out of the groups of the panel, leaving the user itself on the host.
    /// </summary>
    public async Task<HostUserSync> ReleaseAsync(string name, IReadOnlyList<string> roles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (!IsSupported || LocalUsers.Find(name) is null)
        {
            return HostUserSync.Done;
        }

        await LeaveAsync(name, Every(roles), ct).ConfigureAwait(false);

        return HostUserSync.Done;
    }

    /// <summary>
    /// Writes the public key a user signs in to the host with.
    /// </summary>
    public async Task<HostUserSync> KeyAsync(string name, string key, CancellationToken ct)
    {
        if (LocalUsers.Find(name) is not { } user || user.Home.Length == 0)
        {
            return new HostUserSync(false, $"the host carries no user called '{name}'");
        }

        var path = HostUserPlan.Keys(user.Home);
        var folder = Path.GetDirectoryName(path)!;

        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(path, key.Trim() + "\n", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HostUserSync(false, ex.Message);
        }

        var owner = $"{user.Name}:{user.Name}";
        await RunAsync(OwnerTool, [owner, folder], ct).ConfigureAwait(false);
        await RunAsync(OwnerTool, [owner, path], ct).ConfigureAwait(false);
        await RunAsync(ModeTool, ["700", folder], ct).ConfigureAwait(false);
        await RunAsync(ModeTool, ["600", path], ct).ConfigureAwait(false);

        return HostUserSync.Done;
    }

    /// <summary>
    /// Hands the files of the panel to the group, so a privileged user reaches them through the console.
    /// </summary>
    public async Task<HostUserSync> FilesAsync(CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(_database);
        if (folder is { Length: > 0 })
        {
            await RunAsync(GroupTool, [HostUserPlan.Group, folder], ct).ConfigureAwait(false);
            await RunAsync(ModeTool, ["2770", folder], ct).ConfigureAwait(false);
        }

        foreach (var file in Files().Where(File.Exists))
        {
            await RunAsync(GroupTool, [HostUserPlan.Group, file], ct).ConfigureAwait(false);
            await RunAsync(ModeTool, ["660", file], ct).ConfigureAwait(false);
        }

        return HostUserSync.Done;
    }

    private IEnumerable<string> Files() =>
        [_database, _database + "-wal", _database + "-shm", _options.SigningKeyPath];

    private static IReadOnlyList<string> Others(string? role, IReadOnlyList<string> roles) =>
    [
        .. roles
            .Where(one => !string.Equals(one, role, StringComparison.OrdinalIgnoreCase))
            .Select(HostUserPlan.GroupOf)
    ];

    private static IReadOnlyList<string> Every(IReadOnlyList<string> roles) =>
        [HostUserPlan.Group, .. roles.Select(HostUserPlan.GroupOf)];

    private async Task LeaveAsync(string name, IReadOnlyList<string> groups, CancellationToken ct)
    {
        foreach (var group in groups.Where(one => LocalUsers.FindGroup(one) is not null))
        {
            await RunAsync(HostUserPlan.Membership, HostUserPlan.Leave(name, group), ct).ConfigureAwait(false);
        }
    }

    private async Task OpenAsync(string name, CancellationToken ct) =>
        await RunAsync(HostUserPlan.ChangeUser, HostUserPlan.Open(name), ct).ConfigureAwait(false);

    private async Task<CommandResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        try
        {
            return await _commands.RunAsync(tool, arguments, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HostNetworkException or IOException or UnauthorizedAccessException)
        {
            return new CommandResult(1, string.Empty, ex.Message);
        }
    }

    private static HostUserSync Refused(CommandResult result) => new(false, result.Complaint);
}
