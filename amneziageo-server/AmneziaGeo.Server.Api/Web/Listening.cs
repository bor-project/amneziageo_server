using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Where the panel answers from when it holds no settings of its own.
/// </summary>
public sealed class WebOptions
{
    /// <summary>
    /// What the panel listens on when the configuration names nothing.
    /// </summary>
    public static readonly string[] Fallback = [$"*:{PanelDefaults.Port}"];

    /// <summary>
    /// Addresses, interface names and ports the panel listens on.
    /// </summary>
    public string[] Listen { get; set; } = Fallback;

    /// <summary>
    /// The certificate chain the panel answers under, in PEM.
    /// </summary>
    public string Certificate { get; set; } = string.Empty;

    /// <summary>
    /// The private key of the certificate, in PEM.
    /// </summary>
    public string CertificateKey { get; set; } = string.Empty;

    /// <summary>
    /// Where the certificates of the host are looked for.
    /// </summary>
    public string CertificateRoot { get; set; } = PanelDefaults.CertificateRoot;
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
    /// Binds Kestrel to what the panel holds, and to the Web section until it holds anything.
    /// </summary>
    public static WebApplicationBuilder AddListening(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new WebOptions { Listen = [] };
        builder.Configuration.GetSection("Web").Bind(options);
        if (options.Listen.Length == 0)
        {
            options.Listen = WebOptions.Fallback;
        }

        var path = builder.Configuration["Database:Path"] is { Length: > 0 } set ? set : ServerDatabase.DefaultPath();
        var held = PanelStore.Held(path);
        var settings = held ?? Draft(options);
        var entries = held is null ? options.Listen : settings.Entries.ToArray();

        var plan = Plan(entries);
        if (plan.IsEmpty)
        {
            throw new InvalidOperationException("the panel listens on no address this host carries: " + string.Join(", ", entries));
        }

        var certificate = WebCertificate.Of(Chain(options, settings), Key(options, settings));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(plan);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            foreach (var port in plan.AnyPorts)
            {
                kestrel.ListenAnyIP(port, listen => Secure(listen, certificate));
            }

            foreach (var point in plan.Points)
            {
                kestrel.Listen(point, listen => Secure(listen, certificate));
            }
        });

        return builder;
    }

    /// <summary>
    /// Writes down what the panel answers from and what it could not bind.
    /// </summary>
    public static WebApplication ReportListening(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<WebOptions>();
        var settings = app.Services.GetRequiredService<PanelSettings>();
        var plan = app.Services.GetRequiredService<ListenPlan>();

        app.Logger.LogInformation(
            "the panel answers from {Endpoints} under {Path}",
            string.Join(", ", plan.AnyPorts.Select(port => "*:" + port).Concat(plan.Points.Select(point => point.ToString()))),
            settings.Prefix);

        if (Chain(options, settings).Length > 0)
        {
            app.Logger.LogInformation("the panel answers under the certificate {Certificate}", Chain(options, settings));
        }

        if (settings.Domains.Count > 0)
        {
            app.Logger.LogInformation(
                "the panel answers to the names {Domains} only",
                string.Join(", ", settings.Domains));
        }

        foreach (var name in plan.Missing)
        {
            app.Logger.LogWarning("the host carries no interface called {Interface}, nothing is bound for it", name);
        }

        return app;
    }

    /// <summary>
    /// Returns the certificate chain the settings and the configuration name.
    /// </summary>
    public static string Chain(WebOptions options, PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Certificate.Length > 0 ? settings.Certificate : options.Certificate;
    }

    /// <summary>
    /// Returns the certificate key the settings and the configuration name.
    /// </summary>
    public static string Key(WebOptions options, PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        return settings.CertificateKey.Length > 0 ? settings.CertificateKey : options.CertificateKey;
    }

    /// <summary>
    /// Returns the settings behind a listen list the configuration names.
    /// </summary>
    public static PanelSettings Draft(WebOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var port = Split(options.Listen[0]).Port;
        var listen = new List<string>();
        foreach (var entry in options.Listen)
        {
            var (host, own) = Split(entry);
            if (own != port)
            {
                continue;
            }

            if (host is "*")
            {
                return new PanelSettings { Port = port };
            }

            if (IPAddress.TryParse(host, out var address))
            {
                listen.Add(address.ToString());

                continue;
            }

            listen.AddRange(Addresses(host).Select(item => item.ToString()));
        }

        return new PanelSettings { Listen = PanelList.Of(listen), Port = port };
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

    /// <summary>
    /// Puts a listener under the certificate, leaving it plain without one.
    /// </summary>
    internal static void Secure(ListenOptions listen, WebCertificate? certificate)
    {
        if (certificate is null)
        {
            return;
        }

        listen.UseHttps(https => https.ServerCertificateSelector = (_, _) => certificate.Current());
    }

    private static (string Host, int Port) Split(string entry)
    {
        var text = entry.Trim();
        var mark = text.LastIndexOf(':');
        if (mark <= 0 || !int.TryParse(text.AsSpan(mark + 1), CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"the panel is told to listen on {entry}, which is not an address or an interface with a port");
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
