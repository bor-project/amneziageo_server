using System.Runtime.InteropServices;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// An account of the host the server runs on.
/// </summary>
public sealed record LocalUser(string Name, uint Uid, uint Gid, string Home, string Shell);

/// <summary>
/// A group of the host the server runs on.
/// </summary>
public sealed record LocalGroup(string Name, uint Gid, IReadOnlyList<string> Members);

/// <summary>
/// Reads the account database of the host.
/// </summary>
public static partial class LocalUsers
{
    private static readonly Lock _sync = new();

    /// <summary>
    /// Tells whether the host carries an account database this class can read.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsLinux();

    /// <summary>
    /// Returns the account the current process runs as.
    /// </summary>
    public static LocalUser? Current()
    {
        if (!IsSupported)
        {
            return null;
        }

        return FindByUid(GetEuid());
    }

    /// <summary>
    /// Returns an account by name, or null when the host does not carry it.
    /// </summary>
    public static LocalUser? Find(string name)
    {
        if (!IsSupported || string.IsNullOrEmpty(name))
        {
            return null;
        }

        lock (_sync)
        {
            return Read(GetPwNam(name));
        }
    }

    /// <summary>
    /// Returns an account by user id, or null when the host does not carry it.
    /// </summary>
    public static LocalUser? FindByUid(uint uid)
    {
        if (!IsSupported)
        {
            return null;
        }

        lock (_sync)
        {
            return Read(GetPwUid(uid));
        }
    }

    /// <summary>
    /// Returns a group by name, or null when the host does not carry it.
    /// </summary>
    public static LocalGroup? FindGroup(string name)
    {
        if (!IsSupported || string.IsNullOrEmpty(name))
        {
            return null;
        }

        lock (_sync)
        {
            return ReadGroup(GetGrNam(name));
        }
    }

    /// <summary>
    /// Tells whether an account belongs to a group, counting its primary one.
    /// </summary>
    public static bool IsMemberOf(LocalUser user, string group)
    {
        var found = FindGroup(group);
        if (found is null)
        {
            return false;
        }

        return user.Gid == found.Gid || found.Members.Contains(user.Name, StringComparer.Ordinal);
    }

    private static LocalUser? Read(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var entry = Marshal.PtrToStructure<Passwd>(handle);

        return new LocalUser(
            Marshal.PtrToStringUTF8(entry.Name) ?? string.Empty,
            entry.Uid,
            entry.Gid,
            Marshal.PtrToStringUTF8(entry.Home) ?? string.Empty,
            Marshal.PtrToStringUTF8(entry.Shell) ?? string.Empty);
    }

    private static LocalGroup? ReadGroup(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var entry = Marshal.PtrToStructure<Group>(handle);
        var members = new List<string>();

        for (var step = 0; entry.Members != IntPtr.Zero; step++)
        {
            var item = Marshal.ReadIntPtr(entry.Members, step * IntPtr.Size);
            if (item == IntPtr.Zero)
            {
                break;
            }

            var name = Marshal.PtrToStringUTF8(item);
            if (name is not null)
            {
                members.Add(name);
            }
        }

        return new LocalGroup(Marshal.PtrToStringUTF8(entry.Name) ?? string.Empty, entry.Gid, members);
    }

    [LibraryImport("libc", EntryPoint = "getpwnam", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetPwNam(string name);

    [LibraryImport("libc", EntryPoint = "getpwuid")]
    private static partial IntPtr GetPwUid(uint uid);

    [LibraryImport("libc", EntryPoint = "getgrnam", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetGrNam(string name);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEuid();

    [StructLayout(LayoutKind.Sequential)]
    private struct Passwd
    {
        public IntPtr Name;
        public IntPtr Password;
        public uint Uid;
        public uint Gid;
        public IntPtr Gecos;
        public IntPtr Home;
        public IntPtr Shell;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Group
    {
        public IntPtr Name;
        public IntPtr Password;
        public uint Gid;
        public IntPtr Members;
    }
}
