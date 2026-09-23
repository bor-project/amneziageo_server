using System.Security.Cryptography;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// What the panel answers from when nothing is set.
/// </summary>
public static class PanelDefaults
{
    /// <summary>
    /// The port the panel listens on.
    /// </summary>
    public const int Port = 8443;

    /// <summary>
    /// The path a fresh panel sits under, ahead of the name it is given.
    /// </summary>
    public const string PathHead = "sub";

    /// <summary>
    /// The language that leaves the choice to the browser.
    /// </summary>
    public const string Language = "auto";

    /// <summary>
    /// Where the certificates of the host are looked for.
    /// </summary>
    public const string CertificateRoot = "/etc/letsencrypt/live";

    /// <summary>
    /// The name of the certificate chain inside the directory of a domain.
    /// </summary>
    public const string Chain = "fullchain.pem";

    /// <summary>
    /// The name of the certificate key inside the directory of a domain.
    /// </summary>
    public const string Key = "privkey.pem";

    /// <summary>
    /// The languages the panel opens in.
    /// </summary>
    public static readonly string[] Languages = ["auto", "en", "ru"];

    /// <summary>
    /// The settings the panel starts with when it holds none.
    /// </summary>
    public static readonly PanelSettings Settings = new();

    /// <summary>
    /// Returns the path a panel that holds no settings sits under.
    /// </summary>
    public static string FreshPath() => PathHead + "/" + RandomNumberGenerator.GetString(Letters, NameLength);

    private const string Letters = "abcdefghijklmnopqrstuvwxyz0123456789";

    private const int NameLength = 16;
}
