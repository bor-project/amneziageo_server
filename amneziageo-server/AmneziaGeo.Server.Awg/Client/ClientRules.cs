using System.Text.RegularExpressions;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Shapes the settings of a client have to take.
/// </summary>
public static partial class ClientRules
{
    /// <summary>
    /// The longest name a client takes.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// The longest note a client carries.
    /// </summary>
    public const int MaxNoteLength = 255;

    /// <summary>
    /// The most addresses a client carries in the tunnel.
    /// </summary>
    public const int MaxAddresses = 4;

    /// <summary>
    /// The longest name of the subscription a client carries.
    /// </summary>
    public const int MaxSubscriptionLength = 64;

    /// <summary>
    /// The most bytes a day a client is limited to.
    /// </summary>
    public const long MaxDailyLimit = 1L << 50;

    /// <summary>
    /// Returns why the settings of a client are unusable, or null when they hold.
    /// </summary>
    public static ClientFault? Check(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return CheckName(client.Name)
            ?? CheckKey(client.PrivateKey, client.PublicKey)
            ?? CheckPreshared(client.PresharedKey)
            ?? CheckAddress(client.Address)
            ?? CheckNote(client.Note)
            ?? CheckSubscription(client.SubscriptionId)
            ?? CheckLimit(client.DailyLimit);
    }

    /// <summary>
    /// Returns why the name of a client is unusable, or null when it holds.
    /// </summary>
    public static ClientFault? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fault("bad-client-name", "the name is empty");
        }

        if (name.Length > MaxNameLength)
        {
            return Fault("bad-client-name", $"the name is longer than {MaxNameLength} characters");
        }

        return NameShape().IsMatch(name)
            ? null
            : Fault("bad-client-name", "the name takes letters, digits, dash, dot and underscore");
    }

    /// <summary>
    /// Returns why the addresses of a client are unusable, or null when they hold.
    /// </summary>
    public static ClientFault? CheckAddress(IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        if (addresses.Count == 0)
        {
            return Fault("bad-client-address", "the client carries no address");
        }

        if (addresses.Count > MaxAddresses)
        {
            return Fault("bad-client-address", $"the client carries more than {MaxAddresses} addresses");
        }

        foreach (var address in addresses)
        {
            if (!AwgAllowedIp.TryParse(address, out _))
            {
                return Fault("bad-client-address", $"'{address}' is not an address");
            }
        }

        return null;
    }

    /// <summary>
    /// Tells whether a text names a subscription.
    /// </summary>
    public static bool IsSubscription(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= MaxSubscriptionLength && SubscriptionShape().IsMatch(id);

    private static ClientFault? CheckKey(string? privateKey, string? publicKey)
    {
        if (!string.IsNullOrEmpty(privateKey) && !Curve25519.IsKey(privateKey))
        {
            return Fault("bad-client-key", "the private key is not 32 bytes in base64");
        }

        return Curve25519.IsKey(publicKey)
            ? null
            : Fault("bad-client-key", "the public key is not 32 bytes in base64");
    }

    private static ClientFault? CheckPreshared(string? key) =>
        string.IsNullOrEmpty(key) || Curve25519.IsKey(key)
            ? null
            : Fault("bad-client-preshared", "the preshared key is not 32 bytes in base64");

    private static ClientFault? CheckNote(string? note) =>
        note is null || note.Length <= MaxNoteLength
            ? null
            : Fault("bad-client-note", $"the note is longer than {MaxNoteLength} characters");

    private static ClientFault? CheckSubscription(string? id) =>
        string.IsNullOrEmpty(id) || IsSubscription(id)
            ? null
            : Fault("bad-client-subscription", $"the subscription takes letters, digits, dash and underscore, up to {MaxSubscriptionLength}");

    private static ClientFault? CheckLimit(long limit) =>
        limit is >= 0 and <= MaxDailyLimit
            ? null
            : Fault("bad-client-limit", $"the daily limit lies outside 0 to {MaxDailyLimit} bytes");

    private static ClientFault Fault(string code, string message) => new(code, message);

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]*$")]
    private static partial Regex NameShape();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]+\z")]
    private static partial Regex SubscriptionShape();
}
