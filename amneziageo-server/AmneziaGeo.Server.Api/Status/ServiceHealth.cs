using System.Globalization;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Firewall;

namespace AmneziaGeo.Server.Api.Status;

/// <summary>
/// A port a service of the server answers on.
/// </summary>
/// <param name="Protocol">Whether the port takes tcp or udp.</param>
/// <param name="Port">The number of the port.</param>
public sealed record ServicePort(string Protocol, int Port)
{
    /// <summary>
    /// Names the port as the log writes it.
    /// </summary>
    public override string ToString() => $"{Protocol} {Port.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// One thing that keeps a service from working.
/// </summary>
/// <param name="Code">What failed, as the panel names it.</param>
/// <param name="Detail">What the host said about it, or the port it is about.</param>
public sealed record ServiceFault(string Code, string Detail);

/// <summary>
/// Whether one service the server runs for its clients works.
/// </summary>
/// <param name="Kind">subscription, endpoint or dns.</param>
/// <param name="Name">The interface of an endpoint, empty for the others.</param>
/// <param name="Ports">The ports the service answers on.</param>
/// <param name="Shared">Whether the service answers on the port of the panel.</param>
/// <param name="Parts">What the TCP port of an endpoint hands out.</param>
/// <param name="Faults">What keeps the service from working, empty when it works.</param>
/// <param name="Since">When the service last began or stopped working.</param>
public sealed record ServiceHealth(
    string Kind,
    string Name,
    IReadOnlyList<ServicePort> Ports,
    bool Shared,
    IReadOnlyList<string> Parts,
    IReadOnlyList<ServiceFault> Faults,
    DateTimeOffset Since)
{
    /// <summary>
    /// Tells whether the service works.
    /// </summary>
    public bool Works => Faults.Count == 0;
}

/// <summary>
/// The services of the server as the last check found them.
/// </summary>
/// <param name="Checked">When the check ran, null before the first one.</param>
/// <param name="Services">The services the server runs for its clients.</param>
public sealed record ServicesReport(DateTimeOffset? Checked, IReadOnlyList<ServiceHealth> Services)
{
    /// <summary>
    /// The report before the first check.
    /// </summary>
    public static readonly ServicesReport None = new(null, []);
}

/// <summary>
/// What the host said about the websocket front of an endpoint.
/// </summary>
/// <param name="IsRunning">Whether the process of the front runs.</param>
/// <param name="Message">Why the front does not run, or why it last fell over.</param>
/// <param name="Fell">How many times the front fell over since the check before.</param>
/// <param name="Listens">Whether the front listens on its loopback port.</param>
public sealed record FrontFacts(bool IsRunning, string Message, long Fell, bool Listens);

/// <summary>
/// What the resolver of the clients is doing.
/// </summary>
/// <param name="IsEnabled">Whether the resolver is turned on.</param>
/// <param name="IsRunning">Whether it takes questions.</param>
/// <param name="Port">The port it answers on.</param>
/// <param name="Fault">Why it does not run.</param>
/// <param name="Way">What is wrong with the way out its questions take.</param>
public sealed record ResolverFacts(bool IsEnabled, bool IsRunning, int Port, string Fault, string Way);

/// <summary>
/// What the checks of the services read off the panel and the host.
/// </summary>
/// <param name="Configs">The endpoints the panel holds.</param>
/// <param name="PanelPort">The port the panel answers on.</param>
/// <param name="Subscription">The settings the subscriptions answer with.</param>
/// <param name="SubscriptionFault">Why the host refused the port of the subscriptions, empty when it did not.</param>
/// <param name="Refused">The TCP ports of the services the host refused, with why.</param>
/// <param name="Fronts">The websocket fronts by the interface of their endpoint.</param>
/// <param name="Module">Whether the module of the tunnels is loaded.</param>
/// <param name="Links">The interfaces the host carries, null when they did not read.</param>
/// <param name="Walls">What the firewall of the host says about each port.</param>
/// <param name="Resolver">What the resolver of the clients is doing.</param>
public sealed record ServiceFacts(
    IReadOnlyList<ServerConfig> Configs,
    int PanelPort,
    SubscriptionSettings Subscription,
    string SubscriptionFault,
    IReadOnlyDictionary<int, string> Refused,
    IReadOnlyDictionary<string, FrontFacts> Fronts,
    bool Module,
    IReadOnlySet<string>? Links,
    IReadOnlyDictionary<ServicePort, string> Walls,
    ResolverFacts Resolver);

/// <summary>
/// Tells which services the server runs for its clients and what keeps each from working.
/// </summary>
public static class ServiceChecks
{
    /// <summary>
    /// The subscriptions of the clients.
    /// </summary>
    public const string Subscription = "subscription";

    /// <summary>
    /// An endpoint: its tunnel and the services on its TCP port.
    /// </summary>
    public const string Endpoint = "endpoint";

    /// <summary>
    /// The resolver of the clients.
    /// </summary>
    public const string Resolver = "dns";

    /// <summary>
    /// The hello a client asks before it connects.
    /// </summary>
    public const string Hello = "hello";

    /// <summary>
    /// The endpoint names no host, so its clients get no address to reach it at.
    /// </summary>
    public const string NoHost = "no-host";

    /// <summary>
    /// The interface of the endpoint is not on the host.
    /// </summary>
    public const string InterfaceDown = "interface-down";

    /// <summary>
    /// The module of the tunnels is not loaded.
    /// </summary>
    public const string NoModule = "no-module";

    /// <summary>
    /// The host did not let the panel bind the port.
    /// </summary>
    public const string PortRefused = "port-refused";

    /// <summary>
    /// The firewall of the host keeps the port closed.
    /// </summary>
    public const string PortClosed = "port-closed";

    /// <summary>
    /// The websocket front does not run.
    /// </summary>
    public const string FrontDown = "front-down";

    /// <summary>
    /// The websocket front fell over since the check before.
    /// </summary>
    public const string FrontFell = "front-fell";

    /// <summary>
    /// The websocket front runs and does not listen on its port.
    /// </summary>
    public const string FrontDeaf = "front-deaf";

    /// <summary>
    /// No endpoint serves the subscriptions.
    /// </summary>
    public const string NoEndpoint = "no-endpoint";

    /// <summary>
    /// The resolver does not run.
    /// </summary>
    public const string ResolverDown = "resolver-down";

    /// <summary>
    /// The way out of the resolver is broken.
    /// </summary>
    public const string ResolverWay = "resolver-way";

    /// <summary>
    /// Returns the services the server runs for its clients as the facts show them, each since the moment given.
    /// </summary>
    public static IReadOnlyList<ServiceHealth> Of(ServiceFacts facts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var endpoints = facts.Configs.Where(one => one.IsEnabled).OrderBy(one => one.Id).ToList();
        var services = new List<ServiceHealth>();
        if (facts.Subscription.IsEnabled)
        {
            services.Add(Subscriptions(facts, endpoints, now));
        }

        services.AddRange(endpoints.Select(one => OfEndpoint(facts, one, now)));
        if (facts.Resolver.IsEnabled)
        {
            services.Add(OfResolver(facts.Resolver, now));
        }

        return services;
    }

    /// <summary>
    /// Lists the ports of the services the firewall of the host is asked about.
    /// </summary>
    public static IReadOnlyList<ServicePort> Exposed(IReadOnlyList<ServerConfig> configs, SubscriptionSettings subscription)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(subscription);

        var ports = new List<ServicePort>();
        foreach (var config in configs.Where(one => one.IsEnabled).OrderBy(one => one.Id))
        {
            Take(ports, new ServicePort(FirewallPlan.Udp, config.ListenPort));
            Take(ports, new ServicePort(FirewallPlan.Tcp, ConfigServices.Port(config)));
        }

        if (subscription.IsEnabled && subscription.Separate && FirewallPlan.Reached(subscription.Listen))
        {
            Take(ports, new ServicePort(FirewallPlan.Tcp, subscription.Port));
        }

        return ports;
    }

    /// <summary>
    /// Names a service as the log writes it.
    /// </summary>
    public static string Label(ServiceHealth service)
    {
        ArgumentNullException.ThrowIfNull(service);

        return service.Kind switch
        {
            Endpoint => $"endpoint {service.Name}",
            Subscription => "subscriptions",
            _ => service.Kind,
        };
    }

    /// <summary>
    /// Says what keeps a service from working as the log writes it.
    /// </summary>
    public static string Reason(ServiceHealth service)
    {
        ArgumentNullException.ThrowIfNull(service);

        return string.Join("; ", service.Faults.Select(one => one.Detail.Length > 0 ? $"{one.Code} ({one.Detail})" : one.Code));
    }

    /// <summary>
    /// Says in one line how many services work and which do not, for the menu of the host.
    /// </summary>
    public static string Line(IReadOnlyList<ServiceHealth> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var down = services.Where(one => !one.Works).ToList();
        if (down.Count == 0)
        {
            return services.Count == 0 ? "none to run" : $"all {services.Count.ToString(CultureInfo.InvariantCulture)} work";
        }

        var named = down.Select(one => $"{Label(one)} ({string.Join(", ", one.Faults.Select(Short))})");

        return $"{down.Count.ToString(CultureInfo.InvariantCulture)} of {services.Count.ToString(CultureInfo.InvariantCulture)} down: "
            + string.Join(", ", named);
    }

    private static ServiceHealth Subscriptions(ServiceFacts facts, List<ServerConfig> endpoints, DateTimeOffset now)
    {
        var settings = facts.Subscription;
        var faults = new List<ServiceFault>();
        if (!settings.Separate)
        {
            if (!endpoints.Exists(one => Served(facts, one)))
            {
                faults.Add(new ServiceFault(NoEndpoint, string.Empty));
            }

            return new ServiceHealth(Subscription, string.Empty, [], false, [], faults, now);
        }

        var port = new ServicePort(FirewallPlan.Tcp, settings.Port);
        var shared = settings.Port == facts.PanelPort;
        if (!shared && facts.SubscriptionFault.Length > 0)
        {
            faults.Add(new ServiceFault(PortRefused, facts.SubscriptionFault));
        }

        if (FirewallPlan.Reached(settings.Listen))
        {
            Wall(facts, port, faults);
        }

        return new ServiceHealth(Subscription, string.Empty, [port], shared, [], faults, now);
    }

    private static ServiceHealth OfEndpoint(ServiceFacts facts, ServerConfig config, DateTimeOffset now)
    {
        var tunnel = new ServicePort(FirewallPlan.Udp, config.ListenPort);
        var services = new ServicePort(FirewallPlan.Tcp, ConfigServices.Port(config));
        var faults = new List<ServiceFault>();
        if (string.IsNullOrWhiteSpace(config.Host))
        {
            faults.Add(new ServiceFault(NoHost, string.Empty));
        }

        if (facts.Links is { } links && !links.Contains(config.Name))
        {
            faults.Add(new ServiceFault(InterfaceDown, facts.Module ? string.Empty : NoModule));
        }

        if (!Served(facts, config))
        {
            faults.Add(new ServiceFault(PortRefused, facts.Refused[services.Port]));
        }

        if (config.WebSocket && Front(facts, config) is { } front)
        {
            faults.Add(front);
        }

        Wall(facts, tunnel, faults);
        Wall(facts, services, faults);

        return new ServiceHealth(Endpoint, config.Name, [tunnel, services], services.Port == facts.PanelPort, Parts(facts, config), faults, now);
    }

    private static ServiceHealth OfResolver(ResolverFacts resolver, DateTimeOffset now)
    {
        var faults = new List<ServiceFault>();
        if (!resolver.IsRunning)
        {
            faults.Add(new ServiceFault(ResolverDown, resolver.Fault));
        }
        else if (resolver.Way.Length > 0)
        {
            faults.Add(new ServiceFault(ResolverWay, resolver.Way));
        }

        return new ServiceHealth(Resolver, string.Empty, [new ServicePort(FirewallPlan.Udp, resolver.Port)], false, [], faults, now);
    }

    // Tells whether the TCP port of an endpoint answers: the panel serves it or the host took it.
    private static bool Served(ServiceFacts facts, ServerConfig config)
    {
        var port = ConfigServices.Port(config);

        return port == facts.PanelPort || !facts.Refused.ContainsKey(port);
    }

    // Returns what keeps the websocket front of an endpoint from working, null when it works.
    private static ServiceFault? Front(ServiceFacts facts, ServerConfig config)
    {
        if (!facts.Fronts.TryGetValue(config.Name, out var front) || !front.IsRunning)
        {
            return new ServiceFault(FrontDown, front?.Message ?? string.Empty);
        }

        if (front.Fell > 0)
        {
            return new ServiceFault(FrontFell, front.Message);
        }

        return front.Listens
            ? null
            : new ServiceFault(FrontDeaf, $"{ProxyDefaults.Loopback}:{ConfigServices.Front(config).ToString(CultureInfo.InvariantCulture)}");
    }

    private static IReadOnlyList<string> Parts(ServiceFacts facts, ServerConfig config)
    {
        var parts = new List<string> { Hello, FeatureNames.Speed };
        if (config.WebSocket)
        {
            parts.Add(FeatureNames.WebSocket);
        }

        if (facts.Subscription.IsEnabled && !facts.Subscription.Separate)
        {
            parts.Add(FeatureNames.Subscription);
        }

        return parts;
    }

    // Names a fault for the line of the menu, a closed port with its protocol and number.
    private static string Short(ServiceFault fault) =>
        fault.Code == PortClosed && fault.Detail.Length > 0 ? $"{fault.Code} {fault.Detail}" : fault.Code;

    // Notes a port the firewall of the host keeps closed.
    private static void Wall(ServiceFacts facts, ServicePort port, List<ServiceFault> faults)
    {
        if (facts.Walls.TryGetValue(port, out var state) && state == PortState.Closed)
        {
            faults.Add(new ServiceFault(PortClosed, port.ToString()));
        }
    }

    private static void Take(List<ServicePort> ports, ServicePort port)
    {
        if (port.Port is > 0 and <= 65535 && !ports.Contains(port))
        {
            ports.Add(port);
        }
    }
}
