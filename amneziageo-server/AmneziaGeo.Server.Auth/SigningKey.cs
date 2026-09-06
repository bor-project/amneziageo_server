using System.Security.Cryptography;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// The key access tokens are signed with.
/// </summary>
public static class SigningKey
{
    /// <summary>
    /// Reads the key at a path.
    /// </summary>
    public static ECDsa Load(string path)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(path));

        return key;
    }

    /// <summary>
    /// Writes a new key at a path, readable by its owner alone.
    /// </summary>
    public static ECDsa Create(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem() + Environment.NewLine);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return key;
    }

    /// <summary>
    /// Reads the key at a path, writing a new one when it is missing.
    /// </summary>
    public static ECDsa Ensure(string path) => File.Exists(path) ? Load(path) : Create(path);
}
