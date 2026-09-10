using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// The settings the subscriptions answer with right now and why the host refused them.
/// </summary>
public sealed class SubscriptionState
{
    private volatile SubscriptionSettings _current = SubscriptionDefaults.Settings;

    private volatile string _fault = string.Empty;

    /// <summary>
    /// The settings the subscriptions answer with.
    /// </summary>
    public SubscriptionSettings Current
    {
        get => _current;
        set => _current = value;
    }

    /// <summary>
    /// Why the host refused to serve the subscriptions, empty when it did not.
    /// </summary>
    public string Fault
    {
        get => _fault;
        set => _fault = value;
    }
}
