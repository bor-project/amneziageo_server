using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Routing.XHttp;

/// <summary>
/// Defines an XHTTP listener and its users.
/// </summary>
public sealed record XHttpProxy(long Id, int Port, string Path, IReadOnlyList<string> Credentials, ProxyTargets? Targets = null);

/// <summary>
/// Renders the configuration of an XHTTP listener.
/// </summary>
public static class XHttpFile
{
    /// <summary>
    /// Identifies the supported Xray release.
    /// </summary>
    public const string Version = "26.10.31";

    /// <summary>
    /// Renders an XHTTP listener.
    /// </summary>
    public static string Render(XHttpProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(proxy.Id);
        if (proxy.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(proxy), "The listener port must be between 1 and 65535.");
        }

        ArgumentNullException.ThrowIfNull(proxy.Path);
        var path = proxy.Path.Trim('/');
        if (path.Length == 0 || path is "." or ".." || path.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not '~'))
        {
            throw new ArgumentException("The path must contain one nonempty URL segment.", nameof(proxy));
        }

        ArgumentNullException.ThrowIfNull(proxy.Credentials);
        if (proxy.Credentials.Count == 0)
        {
            throw new ArgumentException("The listener requires at least one user.", nameof(proxy));
        }

        var users = proxy.Credentials.Select(ParseCredential)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(id => new { id })
            .ToArray();
        var configuration = new
        {
            log = new { loglevel = "warning" },
            inbounds = new[]
            {
                new
                {
                    tag = "xhttp",
                    listen = "127.0.0.1",
                    port = proxy.Port,
                    protocol = "vless",
                    settings = new { clients = users, decryption = "none" },
                    streamSettings = new
                    {
                        network = "xhttp",
                        security = "none",
                        xhttpSettings = new
                        {
                            path = $"/{path}/",
                            mode = "packet-up",
                            extra = new
                            {
                                uplinkHTTPMethod = "GET",
                                uplinkDataPlacement = "header",
                                uplinkDataKey = "X-Data",
                                uplinkChunkSize = 2048,
                                scMaxEachPostBytes = 2048,
                                serverMaxHeaderBytes = 8192,
                                xPaddingBytes = "100-100",
                            },
                        },
                    },
                },
            },
            outbounds = new[]
            {
                new
                {
                    tag = "udp-targets",
                    protocol = "freedom",
                    settings = new
                    {
                        finalRules = new object[]
                        {
                            new { action = "allow", network = "udp" },
                            new { action = "block", blockDelay = "0-1" },
                        },
                    },
                },
            },
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var text = JsonSerializer.Serialize(configuration, options);
        var targets = ProxyTargetRules.TryNormalize(proxy.Targets, out var normalized) ? normalized : ProxyTargetRules.Denied;
        if (targets is not { Restricted: true })
        {
            return text;
        }

        var document = JsonNode.Parse(text)!.AsObject();
        document["outbounds"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
        {
            tag = "target-denied", protocol = "blackhole", settings = new { },
        }));
        var rules = new JsonArray();
        foreach (var destination in targets.Destinations)
        {
            var rule = new JsonObject
            {
                ["type"] = "field", ["network"] = "udp",
                ["inboundTag"] = JsonSerializer.SerializeToNode(new[] { "xhttp" }),
                ["port"] = destination.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["outboundTag"] = "udp-targets",
            };
            if (IPAddress.TryParse(destination.Host, out var address))
            {
                var addresses = new[] { destination.Host + (address.AddressFamily == AddressFamily.InterNetwork ? "/32" : "/128") };
                rule["ip"] = JsonSerializer.SerializeToNode(addresses);
            }
            else
            {
                rule["domain"] = JsonSerializer.SerializeToNode(new[] { "regexp:" + ProxyTargetRules.HostPattern(destination.Host) });
            }

            rules.Add(rule);
        }

        rules.Add(JsonSerializer.SerializeToNode(new
        {
            type = "field", inboundTag = new[] { "xhttp" }, outboundTag = "target-denied",
        }));
        document["routing"] = new JsonObject { ["domainStrategy"] = "AsIs", ["rules"] = rules };
        return document.ToJsonString(options);
    }

    private static string ParseCredential(string credential)
    {
        if (!Guid.TryParse(credential, out var id) || id == Guid.Empty)
        {
            throw new ArgumentException("Each listener user must have a nonempty GUID.", nameof(credential));
        }

        return id.ToString("D");
    }
}
