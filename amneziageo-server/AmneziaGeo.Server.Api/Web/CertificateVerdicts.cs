using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Keeps the verdicts of the certificates the ports answer under, read again when a file changes and every few minutes.
/// </summary>
public sealed class CertificateVerdicts
{
    private const int MaxHeld = 256;

    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time;

    private readonly X509Certificate2Collection? _roots;

    private readonly ConcurrentDictionary<(string Chain, string Name), Held> _held = new();

    /// <summary>
    /// ctor
    /// </summary>
    public CertificateVerdicts(TimeProvider? time = null, X509Certificate2Collection? roots = null)
    {
        _time = time ?? TimeProvider.System;
        _roots = roots;
    }

    /// <summary>
    /// Returns what keeps the chain in a file from carrying the subscriptions to a name; an empty name is not checked.
    /// </summary>
    public CertificateVerdict Of(string chain, string name)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(name);

        var now = _time.GetUtcNow();
        var stamp = Stamp(chain);
        var key = (chain, name.ToLowerInvariant());
        if (_held.TryGetValue(key, out var held) && held.Stamp == stamp && now - held.At < Keep)
        {
            return held.Verdict;
        }

        var verdict = CertificateCheck.Of(chain, name, now, _roots);
        if (_held.Count >= MaxHeld)
        {
            _held.Clear();
        }

        _held[key] = new Held(verdict, stamp, now);

        return verdict;
    }

    // Returns when the file of a chain last changed, nothing for no file.
    private static DateTime Stamp(string chain)
    {
        if (chain.Length == 0)
        {
            return default;
        }

        try
        {
            return File.GetLastWriteTimeUtc(chain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    // A verdict with the file it was read from and when.
    private sealed record Held(CertificateVerdict Verdict, DateTime Stamp, DateTimeOffset At);
}
