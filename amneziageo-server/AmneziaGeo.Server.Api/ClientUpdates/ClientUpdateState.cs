using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Updates;

namespace AmneziaGeo.Server.Api.ClientUpdates;

/// <summary>
/// The settings the releases of the client are served with right now, the store that holds them and what keeps them
/// from being served.
/// </summary>
public sealed class ClientUpdateState
{
    private volatile ClientUpdateSettings _current = ClientUpdateDefaults.Settings;

    private volatile string _fault = string.Empty;

    private volatile string _detail = string.Empty;

    private volatile string _addressFault = string.Empty;

    private volatile string _pin = string.Empty;

    private volatile ReleaseStore? _store;

    private DateTimeOffset? _checked;

    /// <summary>
    /// The settings the releases are served with.
    /// </summary>
    public ClientUpdateSettings Current
    {
        get => _current;
        set => _current = value;
    }

    /// <summary>
    /// Why the host refused to serve the releases from the store of the panel, empty when it did not.
    /// </summary>
    public string Fault
    {
        get => _fault;
        set => _fault = value;
    }

    /// <summary>
    /// What the host said about the refusal.
    /// </summary>
    public string Detail
    {
        get => _detail;
        set => _detail = value;
    }

    /// <summary>
    /// Why the service apart did not answer the last check, empty when it did.
    /// </summary>
    public string AddressFault
    {
        get => _addressFault;
        set => _addressFault = value;
    }

    /// <summary>
    /// The SHA-256 of the certificate the service apart answered under, empty before it answered.
    /// </summary>
    public string Pin
    {
        get => _pin;
        set => _pin = value;
    }

    /// <summary>
    /// The store of the panel while the releases are served from it.
    /// </summary>
    public ReleaseStore? Store
    {
        get => _store;
        set => _store = value;
    }

    /// <summary>
    /// When the service apart was last checked, null before the first check.
    /// </summary>
    public DateTimeOffset? Checked
    {
        get => _checked;
        set => _checked = value;
    }
}
