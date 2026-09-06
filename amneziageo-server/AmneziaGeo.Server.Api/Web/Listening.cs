using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Where the panel answers from.
/// </summary>
public sealed class WebOptions
{
    /// <summary>
    /// What the panel listens on when the configuration names nothing.
    /// </summary>
    public static readonly string[] Fallback = ["*:5080"];

    /// <summary>
    /// Addresses, interface names and ports the panel listens on.
    /// </summary>
    public string[] Listen { get; set; } = Fallback;
}

/// <summary>
/// The endpoints a listen list resolved to.
/// </summary>
public sealed record ListenPlan(
    IReadOnlyList<int> AnyPorts,
    IReadOnlyList<IPEndPoint> Points,
    IReadOnlyList<string> Missing)
{
    /// <summary>
    /// Tells whether anything at all is bound.
    /// </summary>
    public bool IsEmpty => AnyPorts.Count == 0 && Points.Count == 0;
}

/// <summary>
/// Turns the listen list into the endpoints Kestrel binds.
/// </summary>
public static class Listening
{
    /// <summary>
    /// Binds Kestrel to the addresses and interfaces the Web section names.
    /// </summary>
    public static WebApplicationBuilder AddListening(this WebApplicationBuilder builder)
    {
        var options = new WebOptions { Listen = [] };
        builder.Configuration.GetSection("Web").Bind(options);
        if (options.Listen.Length == 0)
        {
            options.Listen = WebOptions.Fallback;
        }

        var plan = Plan(options.Listen);
        if (plan.IsEmpty)
        {
            throw new InvalidOperationException("Web:Listen names no address this host carries: " + string.Join(", ", options.Listen));
        }

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(plan);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            foreach (var port in plan.AnyPorts)
            {
                kestrel.ListenAnyIP(port);
            }

            foreach (var point in plan.Points)
            {
                kestrel.Listen(point);
            }
        });

        return builder;
    }

    /// <summary>
    /// Writes down the interfaces the listen list named and the host does not carry.
    /// </summary>
    public static WebApplication ReportListening(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<WebOptions>();
        var plan = app.Services.GetRequiredService<ListenPlan>();

        app.Logger.LogInformation(
            "Web:Listen holds {Entries} and resolves to {Endpoints}",
            string.Join(", ", options.Listen),
            string.Join(", ", plan.AnyPorts.Select(port => "*:" + port).Concat(plan.Points.Select(point => point.ToString()))));

        foreach (var name in plan.Missing)
        {
            app.Logger.LogWarning("the host carries no interface called {Interface}, nothing is bound for it", name);
        }

        return app;
    }

    /// <summary>
    /// Resolves a listen list into the endpoints behind it.
    /// </summary>
    public static ListenPlan Plan(IEnumerable<string> entries)
    {
        var any = new List<int>();
        var points = new List<IPEndPoint>();
        var missing = new List<string>();

        foreach (var entry in entries)
        {
            var (host, port) = Split(entry);
            if (host is "*")
            {
                any.Add(port);
                continue;
            }

            if (IPAddress.TryParse(host, out var address))
            {
                points.Add(new IPEndPoint(address, port));
                continue;
            }

            var found = Addresses(host).Select(a => new IPEndPoint(a, port)).ToArray();
            if (found.Length == 0)
            {
                missing.Add(host);
                continue;
            }

            points.AddRange(found);
        }

        return new ListenPlan([.. any.Distinct()], [.. points.Distinct()], missing);
    }

    private static (string Host, int Port) Split(string entry)
    {
        var text = entry.Trim();
        var mark = text.LastIndexOf(':');
        if (mark <= 0 || !int.TryParse(text.AsSpan(mark + 1), CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"Web:Listen holds {entry}, which is not an address or an interface with a port");
        }

        return (text[..mark].Trim('[', ']'), port);
    }

    private static IEnumerable<IPAddress> Addresses(string name)
    {
        var found = Array.Find(
            NetworkInterface.GetAllNetworkInterfaces(),
            item => string.Equals(item.Name, name, StringComparison.Ordinal));

        if (found is null)
        {
            return [];
        }

        return found.GetIPProperties().UnicastAddresses
            .Select(item => item.Address)
            .Where(item => item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Where(item => !item.IsIPv6LinkLocal);
    }
}
