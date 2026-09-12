using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Names the groups a privileged account belongs to on the host and the calls that carry it there.
/// </summary>
public static partial class HostUserPlan
{
    /// <summary>
    /// The group that reaches the files of the panel.
    /// </summary>
    public const string Group = "amneziageo";

    /// <summary>
    /// What the group of a role starts with.
    /// </summary>
    public const string RolePrefix = "amneziageo-";

    /// <summary>
    /// The shell a privileged user is given.
    /// </summary>
    public const string Shell = "/bin/bash";

    /// <summary>
    /// The tools the host carries the user with.
    /// </summary>
    public const string AddUser = "useradd";

    /// <summary>
    /// The tool a user is changed with.
    /// </summary>
    public const string ChangeUser = "usermod";

    /// <summary>
    /// The tool a group is added with.
    /// </summary>
    public const string AddGroup = "groupadd";

    /// <summary>
    /// The tool a user is taken out of a group with.
    /// </summary>
    public const string Membership = "gpasswd";

    /// <summary>
    /// Returns the group that carries a role.
    /// </summary>
    public static string GroupOf(string role) => RolePrefix + role.Trim().ToLowerInvariant();

    /// <summary>
    /// Returns the groups a privileged account of a role belongs to.
    /// </summary>
    public static IReadOnlyList<string> Groups(string? role) => role is { Length: > 0 }
        ? [Group, GroupOf(role)]
        : [Group];

    /// <summary>
    /// Returns the call that adds a user of the host.
    /// </summary>
    public static IReadOnlyList<string> Add(string name, string? role) =>
        ["--create-home", "--shell", Shell, "--groups", string.Join(",", Groups(role)), name];

    /// <summary>
    /// Returns the call that puts a user the host already carries into the groups of the panel.
    /// </summary>
    public static IReadOnlyList<string> Join(string name, string? role) =>
        ["--append", "--groups", string.Join(",", Groups(role)), name];

    /// <summary>
    /// Returns the call that closes the way into the host for a user.
    /// </summary>
    public static IReadOnlyList<string> Shut(string name) => ["--lock", "--expiredate", "1", name];

    /// <summary>
    /// Returns the call that opens the way into the host for a user again.
    /// </summary>
    public static IReadOnlyList<string> Open(string name) => ["--unlock", "--expiredate", string.Empty, name];

    /// <summary>
    /// Returns the call that takes a user out of a group.
    /// </summary>
    public static IReadOnlyList<string> Leave(string name, string group) => ["--delete", name, group];

    /// <summary>
    /// Returns the file the keys of a user are read from.
    /// </summary>
    public static string Keys(string home) => Path.Combine(home, ".ssh", "authorized_keys");

    /// <summary>
    /// Tells whether a line is a public key the host takes.
    /// </summary>
    public static bool IsKey(string? text) => text is { Length: > 0 } line && KeyShape().IsMatch(line.Trim());

    [GeneratedRegex(@"^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(256|384|521)|sk-ssh-ed25519@openssh\.com|sk-ecdsa-sha2-nistp256@openssh\.com) [A-Za-z0-9+/]+={0,3}( .*)?$")]
    private static partial Regex KeyShape();
}
