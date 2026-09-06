using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Signs and reads access tokens as compact JWS with an elliptic curve key.
/// </summary>
public sealed class TokenIssuer : ITokenIssuer, IDisposable
{
    private static readonly byte[] _header = "{\"alg\":\"ES256\",\"typ\":\"JWT\"}"u8.ToArray();

    private readonly Lock _gate = new();

    private readonly ECDsa _key;

    private readonly AuthOptions _options;

    private readonly bool _owned;

    /// <summary>
    /// ctor
    /// </summary>
    public TokenIssuer(ECDsa key, AuthOptions options, bool owned = false)
    {
        _key = key;
        _options = options;
        _owned = owned;
    }

    /// <summary>
    /// Opens the key at the configured path, writing a new one when it is missing.
    /// </summary>
    public static TokenIssuer Open(AuthOptions options) =>
        new(SigningKey.Ensure(options.SigningKeyPath), options, owned: true);

    /// <summary>
    /// Signs an access token for a principal.
    /// </summary>
    public string Issue(Principal principal, DateTimeOffset now)
    {
        var payload = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", _options.Issuer);
            writer.WriteString("aud", _options.Audience);
            writer.WriteString("sub", principal.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("name", principal.Name);
            writer.WriteNumber("sid", principal.SessionId);
            writer.WriteString("sch", principal.Scheme.ToString());
            writer.WriteString("scp", string.Join(' ', principal.Scopes));
            writer.WriteNumber("iat", now.ToUnixTimeSeconds());
            writer.WriteNumber("exp", now.Add(_options.AccessLifetime).ToUnixTimeSeconds());
            writer.WriteEndObject();
        }

        var head = Base64Url.EncodeToString(_header);
        var body = Base64Url.EncodeToString(payload.WrittenSpan);
        var signed = Encoding.ASCII.GetBytes(string.Concat(head, ".", body));
        var signature = Sign(signed);

        return string.Concat(head, ".", body, ".", Base64Url.EncodeToString(signature));
    }

    /// <summary>
    /// Reads a token back, returning null when the signature, lifetime or issuer does not hold.
    /// </summary>
    public Principal? Read(string token, DateTimeOffset now)
    {
        try
        {
            return Parse(token, now);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Releases the key when this issuer opened it.
    /// </summary>
    public void Dispose()
    {
        if (_owned)
        {
            _key.Dispose();
        }
    }

    private byte[] Sign(byte[] signed)
    {
        lock (_gate)
        {
            return _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
    }

    private bool Verify(byte[] signed, byte[] signature)
    {
        lock (_gate)
        {
            return _key.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
    }

    private Principal? Parse(string token, DateTimeOffset now)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        using (var head = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0])))
        {
            if (Text(head.RootElement, "alg") != "ES256")
            {
                return null;
            }
        }

        var signed = Encoding.ASCII.GetBytes(string.Concat(parts[0], ".", parts[1]));
        var signature = Base64Url.DecodeFromChars(parts[2]);
        if (!Verify(signed, signature))
        {
            return null;
        }

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        var claims = payload.RootElement;

        if (Text(claims, "iss") != _options.Issuer || Text(claims, "aud") != _options.Audience)
        {
            return null;
        }

        if (!claims.TryGetProperty("exp", out var expires) || expires.GetInt64() <= now.ToUnixTimeSeconds())
        {
            return null;
        }

        if (!long.TryParse(Text(claims, "sub"), CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        if (!Enum.TryParse<AuthScheme>(Text(claims, "sch"), out var scheme))
        {
            return null;
        }

        var scopes = (Text(claims, "scp") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        return new Principal(id, Text(claims, "name") ?? string.Empty, scheme, claims.GetProperty("sid").GetInt64(), scopes);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
}
