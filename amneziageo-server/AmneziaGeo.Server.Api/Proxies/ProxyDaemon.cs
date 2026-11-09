using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Runs the isolated proxy processes.
/// </summary>
public static class ProxyDaemon
{
    public const string SocketPath = "/run/amneziageo-proxy/control.sock";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("The proxy service requires Linux.");
            return 2;
        }
        if (args is ["credential"])
        {
            Console.WriteLine(Guid.NewGuid().ToString("D"));
            return 0;
        }

        if (args is ["get-server", "--help"])
        {
            Console.WriteLine("get-server -c <profile>");
            return 0;
        }

        if (args.Length > 0 && args[0] is "get-server" or "server")
        {
            return await ForwardAsync(args, Environment.GetEnvironmentVariable("PROXY_CONTROL_SOCKET") ?? SocketPath).ConfigureAwait(false);
        }

        if (args is ["init", var file])
        {
            await ProxyCatalog.WriteAsync(file, new ProxyCatalog(1, []), CancellationToken.None).ConfigureAwait(false);
            return 0;
        }

        if (args.Length == 0 || args[0] != "serve")
        {
            Console.Error.WriteLine("usage: AmneziaGeo.Server.Proxy.Host serve [--config file] [--socket path] [--state directory]");
            return 2;
        }

        var settings = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args[1..]).Build();
        var socket = settings["socket"] ?? SocketPath;
        var config = settings["config"] ?? "/etc/amneziageo-proxy/proxies.json";
        var state = settings["state"] ?? "/var/lib/amneziageo-proxy";
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSystemd();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        using var host = builder.Build();
        var life = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ProxyService");
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(socket))!;
            Directory.CreateDirectory(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (File.Exists(socket))
            {
                try
                {
                    using var check = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await check.ConnectAsync(new UnixDomainSocketEndPoint(socket), life.ApplicationStopping).ConfigureAwait(false);
                    throw new InvalidOperationException("The proxy control socket is already active.");
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionRefused or SocketError.AddressNotAvailable)
                {
                    logger.LogInformation(ex, "Removing the inactive proxy control socket");
                    File.Delete(socket);
                }
            }

            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socket));
            File.SetUnixFileMode(socket, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(128);
            await host.StartAsync().ConfigureAwait(false);
            var standalone = new StandaloneProxyService(state, logger);
            await using var disposing = standalone.ConfigureAwait(false);
            var serving = AcceptAsync(listener, logger, life.ApplicationStopping);
            var watching = standalone.WatchAsync(config, life.ApplicationStopping);
            await Task.WhenAny(serving, watching).ConfigureAwait(false);
            life.StopApplication();
            await Task.WhenAll(serving, watching).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (life.ApplicationStopping.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The proxy service stopped");
            return 1;
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
        }
    }

    private static async Task AcceptAsync(Socket listener, ILogger logger, CancellationToken ct)
    {
        using var slots = new SemaphoreSlim(128);
        var active = new HashSet<Task>();
        try
        {
            while (true)
            {
                await slots.WaitAsync(ct).ConfigureAwait(false);
                var connection = await listener.AcceptAsync(ct).ConfigureAwait(false);
                var task = ServeAsync(connection, logger, ct);
                active.RemoveWhere(task => task.IsCompleted);
                active.Add(task);
                _ = task.ContinueWith(_ => slots.Release(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        finally
        {
            await Task.WhenAll(active).ConfigureAwait(false);
        }
    }

    private static async Task ServeAsync(Socket socket, ILogger logger, CancellationToken ct)
    {
        using var connection = socket;
        var stream = new NetworkStream(connection, false);
        await using var disposing = stream.ConfigureAwait(false);
        using var process = new Process();
        try
        {
            using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            var bytes = await ReadAsync(stream, readDeadline.Token).ConfigureAwait(false);
            var arguments = JsonSerializer.Deserialize<string[]>(bytes, Json) ?? [];
            var command = NativeCommand(arguments);
            process.StartInfo = new ProcessStartInfo(command.File)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            foreach (var argument in command.Arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                throw new IOException("The proxy process did not start.");
            }

            var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, ct);
            var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);
            var ended = process.WaitForExitAsync(ct);
            var disconnected = stream.ReadAsync(new byte[1], ct).AsTask();
            var finished = await Task.WhenAny(ended, disconnected).ConfigureAwait(false);
            if (finished != ended)
            {
                return;
            }

            await ended.ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            logger.LogInformation("A proxy process ended with code {Code}", process.ExitCode);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            if (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A proxy control request failed");
            }
        }
        finally
        {
            try
            {
                if (process.Id > 0 && !process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException ex)
            {
                logger.LogDebug(ex, "The proxy process has ended");
            }
        }
    }

    public static AmneziaGeo.Server.Routing.Proxy.ProxyCommand NativeCommand(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments.Count > 32 || arguments.Any(value => value.Length > 4096 || value.Contains('\0')))
        {
            throw new ArgumentException("Invalid proxy command.");
        }

        if (arguments[0] == "get-server" && arguments.Count == 3 && arguments[1] == "-c")
        {
            return new(Path.Combine(AppContext.BaseDirectory, "wstunnel-get"), arguments);
        }

        if (arguments[0] != "server" || !arguments.Contains("--restrict-config"))
        {
            throw new ArgumentException("The proxy command requires destination restrictions.");
        }

        var address = arguments.Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(uri => uri?.Scheme is "ws" or "wts");
        if (address is null || address.Scheme == "ws" && !address.IsLoopback)
        {
            throw new ArgumentException("Invalid proxy listener.");
        }

        return new(Path.Combine(AppContext.BaseDirectory, address.Scheme == "wts" ? "wstunnel-quic" : "wstunnel"), arguments);
    }

    private static async Task<int> ForwardAsync(string[] args, string path)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), deadline.Token).ConfigureAwait(false);
            var stream = new NetworkStream(socket, false);
            await using var disposing = stream.ConfigureAwait(false);
            await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(args, Json), deadline.Token).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), deadline.Token).ConfigureAwait(false);
            var answer = await ReadAsync(stream, CancellationToken.None).ConfigureAwait(false);
            return int.TryParse(Encoding.UTF8.GetString(answer), out var code) ? code : 1;
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine("The independent proxy service is unavailable.");
            return 1;
        }
    }

    private static async Task<byte[]> ReadAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (buffer.Length < 16384)
        {
            var count = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (count == 0)
            {
                throw new IOException("The proxy control connection ended.");
            }

            if (one[0] == '\n')
            {
                return buffer.ToArray();
            }

            buffer.WriteByte(one[0]);
        }

        throw new IOException("The proxy control request is too large.");
    }
}
