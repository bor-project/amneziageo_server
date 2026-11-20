using System.Text;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// Reports recognized process failures.
/// </summary>
public static class ProxyProcessError
{
    private static readonly string[] Codes = ["address-in-use", "permission-denied", "address-unavailable", "connection-refused", "missing-file", "invalid-configuration"];

    public static string Description(string code) => code switch
    {
        "address-in-use" => "The proxy listener address is already in use.",
        "permission-denied" => "The proxy process was denied permission.",
        "address-unavailable" => "The proxy listener address is unavailable.",
        "connection-refused" => "The proxy connection was refused.",
        "missing-file" => "A required proxy file was not found.",
        "invalid-configuration" => "The proxy configuration is invalid.",
        _ => string.Empty,
    };

    public static async Task<string> ReadAsync(TextReader reader, CancellationToken ct, Action<string>? recognized = null)
    {
        var buffer = new char[512];
        var text = new StringBuilder(4096);
        var failure = string.Empty;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (count == 0)
            {
                return failure;
            }

            text.Append(buffer, 0, count);
            if (text.Length > 4096)
            {
                text.Remove(0, text.Length - 4096);
            }

            var code = Code(text.ToString());
            if (code.Length > 0)
            {
                failure = code;
                recognized?.Invoke(code);
            }
        }
    }

    private static string Code(string text)
    {
        foreach (var code in Codes)
        {
            if (text.Contains(Description(code), StringComparison.Ordinal))
            {
                return code;
            }
        }

        if (Has("address in use") || Has("address already in use") || Has("os error 98") || Has("EADDRINUSE"))
        {
            return "address-in-use";
        }

        if (Has("permission denied") || Has("access is denied") || Has("os error 13"))
        {
            return "permission-denied";
        }

        if (Has("cannot assign requested address") || Has("address is unavailable") || Has("os error 99"))
        {
            return "address-unavailable";
        }

        if (Has("connection refused") || Has("connection was refused") || Has("os error 111"))
        {
            return "connection-refused";
        }

        if (Has("no such file or directory") || Has("file was not found") || Has("os error 2)"))
        {
            return "missing-file";
        }

        if (Has("invalid restriction") || Has("configuration is invalid") || Has("failed to parse restriction"))
        {
            return "invalid-configuration";
        }

        return string.Empty;

        bool Has(string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
