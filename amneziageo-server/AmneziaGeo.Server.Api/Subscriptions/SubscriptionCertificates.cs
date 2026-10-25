using System.Collections.Concurrent;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// Tells which certificate the subscriptions answer under and whether it carries them.
/// </summary>
public sealed class SubscriptionCertificates
{
    private const int MaxSaid = 256;

    private readonly SubscriptionState _state;

    private readonly PanelSettings _panel;

    private readonly WebOptions _options;

    private readonly CertificateVerdicts _verdicts;

    private readonly ILogger<SubscriptionCertificates> _logger;

    private readonly ConcurrentDictionary<string, string> _said = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionCertificates(
        SubscriptionState state,
        PanelSettings panel,
        WebOptions options,
        CertificateVerdicts verdicts,
        ILogger<SubscriptionCertificates> logger)
    {
        _state = state;
        _panel = panel;
        _options = options;
        _verdicts = verdicts;
        _logger = logger;
    }

    /// <summary>
    /// Returns the chain the subscriptions answer under as the settings serve them, empty when they answer under none
    /// of their own.
    /// </summary>
    public string Chain(SubscriptionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var panel = Listening.Chain(_options, _panel);
        if (!settings.Separate || settings.Port == _panel.Port)
        {
            return panel;
        }

        return settings.Certificate.Length > 0 ? settings.Certificate : panel;
    }

    /// <summary>
    /// Returns what keeps the subscriptions from answering a request at a host, and writes it down once.
    /// </summary>
    public CertificateVerdict OnRequest(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var verdict = _verdicts.Of(Chain(_state.Current), host);
        var said = verdict.IsGood ? string.Empty : verdict.Fault + " " + verdict.Detail;
        if (_said.TryGetValue(host, out var before) && string.Equals(before, said, StringComparison.Ordinal))
        {
            return verdict;
        }

        if (_said.Count >= MaxSaid)
        {
            _said.Clear();
        }

        _said[host] = said;
        if (!verdict.IsGood)
        {
            _logger.LogWarning(
                "the subscriptions do not answer at {Host}: the certificate they answer under is not one every client takes ({Fault} {Detail})",
                host,
                verdict.Fault,
                verdict.Detail);
        }

        return verdict;
    }

    /// <summary>
    /// Returns what keeps a client from reading its subscription at an address, the good verdict for no address.
    /// </summary>
    public CertificateVerdict Of(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.Length == 0 || !Uri.TryCreate(address, UriKind.Absolute, out var url))
        {
            return CertificateVerdict.Good;
        }

        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return new CertificateVerdict(CertificateCheck.Missing, string.Empty);
        }

        return _verdicts.Of(Chain(_state.Current), url.IdnHost);
    }

    /// <summary>
    /// Returns what keeps the chain in a file from being taken at a host; an empty host is not checked.
    /// </summary>
    public CertificateVerdict OfChain(string chain, string host)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(host);

        return _verdicts.Of(chain, host);
    }

    /// <summary>
    /// Returns what keeps the subscriptions of the endpoints from being read, the first fault found.
    /// </summary>
    public CertificateVerdict Served(IReadOnlyList<ServerConfig> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var settings = _state.Current;
        if (!settings.IsEnabled)
        {
            return CertificateVerdict.Good;
        }

        var chain = Chain(settings);
        if (settings.Separate)
        {
            return _verdicts.Of(chain, SubscriptionAnswer.Name(settings, _panel, string.Empty));
        }

        var verdict = _verdicts.Of(chain, string.Empty);
        foreach (var endpoint in endpoints.Where(one => one.IsEnabled).OrderBy(one => one.Id))
        {
            if (!verdict.IsGood)
            {
                break;
            }

            verdict = _verdicts.Of(chain, SubscriptionAnswer.Name(settings, _panel, endpoint.Host));
        }

        return verdict;
    }
}
