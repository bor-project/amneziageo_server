using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public sealed class HostCommandsTests
{
    [Fact]
    public async Task Missing_tool_returns_host_refusal_with_original_error()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ag714-missing-{Guid.NewGuid():N}");
        var error = await Assert.ThrowsAsync<HostNetworkException>(() =>
            new HostCommands().RunAsync(file, [], null, CancellationToken.None)).ConfigureAwait(false);

        Assert.Contains(file, error.Message, StringComparison.Ordinal);
        Assert.IsType<Win32Exception>(error.InnerException);
        Assert.Equal(2, ((Win32Exception)error.InnerException!).NativeErrorCode);
    }

    [Fact]
    public async Task Nonexecutable_tool_returns_host_refusal()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var file = Path.Combine(Path.GetTempPath(), $"ag714-not-executable-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(file, "#!/bin/sh\nexit 0\n").ConfigureAwait(false);
        try
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var error = await Assert.ThrowsAsync<HostNetworkException>(() =>
                new HostCommands().RunAsync(file, [], null, CancellationToken.None)).ConfigureAwait(false);
            Assert.Contains(file, error.Message, StringComparison.Ordinal);
            Assert.Equal(13, Assert.IsType<Win32Exception>(error.InnerException).NativeErrorCode);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Tool_exit_failure_keeps_output_and_exit_code()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var result = await new HostCommands().RunAsync("/bin/sh",
            ["-c", "printf out; printf err >&2; exit 7"], null, CancellationToken.None).ConfigureAwait(false);
        Assert.False(result.IsOk);
        Assert.Equal(7, result.Code);
        Assert.Equal("out", result.Output);
        Assert.Equal("err", result.Error);
    }

    [Fact]
    public async Task Endpoint_reports_missing_tool_and_accepts_later_attempts()
    {
        var network = DispatchProxy.Create<IHostNetwork, CommandNetwork>();
        var host = new EndpointHost(network, new Kernel());
        var config = new ServerConfig { Name = "ag714", IsEnabled = true };

        var refused = await host.ApplyAsync(config, CancellationToken.None).ConfigureAwait(false);
        Assert.False(refused.IsDone);
        Assert.Equal(EndpointFault.Failed, refused.Fault);
        Assert.Contains("ag714-missing", refused.Message, StringComparison.Ordinal);
        var next = await host.ApplyAsync(config, CancellationToken.None).ConfigureAwait(false);
        Assert.False(next.IsDone);
        Assert.Equal(2, ((CommandNetwork)(object)network).Attempts);
    }

    [Fact]
    public async Task Background_startup_refusal_keeps_host_running()
    {
        var endpoint = new EndpointHost(DispatchProxy.Create<IHostNetwork, CommandNetwork>(), new Kernel());
        var result = new TaskCompletionSource<EndpointSync>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => new RefusalBoot(endpoint, result));
        using var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        try
        {
            var refused = await result.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            Assert.False(refused.IsDone);
            Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
        }
    }

    private sealed class RefusalBoot : BackgroundService
    {
        private readonly EndpointHost _endpoint;
        private readonly TaskCompletionSource<EndpointSync> _result;

        /// <summary>
        /// ctor
        /// </summary>
        public RefusalBoot(EndpointHost endpoint, TaskCompletionSource<EndpointSync> result)
        {
            _endpoint = endpoint;
            _result = result;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var refused = await _endpoint.ApplyAsync(
                new ServerConfig { Name = "ag714-boot", IsEnabled = true }, stoppingToken).ConfigureAwait(false);
            _result.SetResult(refused);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
    }

    public class CommandNetwork : DispatchProxy
    {
        public int Attempts { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(IHostNetwork.ForwardingAsync))
            {
                Attempts++;
                return new HostCommands().RunAsync(
                    Path.Combine(Path.GetTempPath(), "ag714-missing-" + Guid.NewGuid().ToString("N")),
                    [], null, (CancellationToken)args![0]!);
            }

            throw new InvalidOperationException("Unexpected network operation: " + method?.Name);
        }
    }
}
