using System.Diagnostics;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Tests;

public class ProxyProcessDiagnosticsTests
{
    [Theory]
    [InlineData("address-in-use")]
    [InlineData("permission-denied")]
    [InlineData("address-unavailable")]
    [InlineData("connection-refused")]
    [InlineData("missing-file")]
    [InlineData("invalid-configuration")]
    public async Task AForwardedDescriptionRetainsItsRecognizedCode(string expected)
    {
        using var reader = new StringReader("ignored secret\n" + ProxyProcessError.Description(expected) + "\n");

        Assert.Equal(expected, await ProxyProcessError.ReadAsync(reader, CancellationToken.None).ConfigureAwait(false));
    }

    [Theory]
    [InlineData("PROXY_ARGS=ws://127.0.0.1:61001 --restrict-config /etc/proxy-awg0.yaml")]
    [InlineData("PROXY_ARGS=\"ws://127.0.0.1:61001 --restrict-config /etc/proxy-awg0.yaml\"")]
    [InlineData("PROXY_ARGS='ws://127.0.0.1:61001 --restrict-config /etc/proxy-awg0.yaml'")]
    [InlineData("PROXY_ARGS=\"ws://127.0.0.1:61001 --restrict-config /etc/proxy-awg0.yaml\"  \r\n")]
    [InlineData("PROXY_ARGS='ws://127.0.0.1:61001 --restrict-config /etc/proxy-awg0.yaml'  \r\n")]
    [InlineData("PROXY_ARGS=ws://127.0.0.1:61001 --restrict-config /etc/proxy-awg0.yaml  \r\n")]
    public void LegacyEnvironmentQuotesPreserveTheArguments(string environment)
    {
        Assert.Equal(["ws://127.0.0.1:61001", "--restrict-config", "/etc/proxy-awg0.yaml"], ChildProxies.Arguments(environment));
    }

    [Theory]
    [InlineData("Address in use (os error 98)", "address-in-use")]
    [InlineData("Permission denied (os error 13)", "permission-denied")]
    [InlineData("Cannot assign requested address (os error 99)", "address-unavailable")]
    [InlineData("Connection refused (os error 111)", "connection-refused")]
    [InlineData("No such file or directory (os error 2)", "missing-file")]
    [InlineData("Failed to parse restriction", "invalid-configuration")]
    public async Task RecognizedFailuresCarryOnlyTheirCode(string error, string expected)
    {
        using var reader = new StringReader("Authorization: Bearer ag805-secret; PrivateKey=ag805-key; " + error);

        var code = await ProxyProcessError.ReadAsync(reader, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(expected, code);
        Assert.DoesNotContain("ag805", ProxyProcessError.Description(code), StringComparison.Ordinal);
        Assert.NotEmpty(ProxyProcessError.Description(code));
    }

    [Fact]
    public async Task UnknownOutputCarriesNoSecret()
    {
        using var reader = new StringReader("Authorization: Bearer ag805-secret; PrivateKey=ag805-key; https://user:password@example.test");

        Assert.Empty(await ProxyProcessError.ReadAsync(reader, CancellationToken.None).ConfigureAwait(false));
        Assert.Empty(ProxyProcessError.Description("untrusted-secret"));
    }

    [Fact]
    public async Task OutputIsDrainedAfterARecognizedFailure()
    {
        using var reader = new StringReader(new string('x', 506) + "Address in use (os error 98)" + new string('y', 500_000));

        Assert.Equal("address-in-use", await ProxyProcessError.ReadAsync(reader, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(-1, reader.Peek());
    }

    [Fact]
    public async Task AStoppedReaderObservesCancellation()
    {
        using var reader = new StringReader("running");
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProxyProcessError.ReadAsync(reader, stop.Token)).ConfigureAwait(false);
    }

    [Theory]
    [InlineData("Address in use (os error 98)", "The proxy listener address is already in use.")]
    [InlineData("ag805-unknown-secret", "")]
    public async Task AFailedChildReportsTheRecognizedReason(string nativeError, string expected)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var root = Path.Combine(Path.GetTempPath(), "ag805-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tool = Path.Combine(root, "tool");
            await File.WriteAllTextAsync(tool, "#!/bin/sh\necho 'Authorization: Bearer ag805-secret; PrivateKey=ag805-key; " + nativeError + "' >&2\nexit 134\n").ConfigureAwait(false);
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await File.WriteAllTextAsync(Path.Combine(root, ProxyFile.Arguments("awg0")), "PROXY_ARGS=ws://127.0.0.1:61001\n").ConfigureAwait(false);
            using var proxies = new ChildProxies(root, tool, TimeSpan.FromMilliseconds(100));
            await proxies.StartAsync("awg0", CancellationToken.None).ConfigureAwait(false);
            var state = await proxies.StateAsync("awg0", CancellationToken.None).ConfigureAwait(false);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (state.Falls == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20).ConfigureAwait(false);
                state = await proxies.StateAsync("awg0", CancellationToken.None).ConfigureAwait(false);
            }

            Assert.True(state.Falls > 0);
            Assert.Equal("tool ended with code 134" + (expected.Length > 0 ? ": " + expected : string.Empty), state.Message);
            Assert.DoesNotContain("ag805-", state.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AChildRetainingTheOutputPipesDoesNotDelayRestart()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        var root = Path.Combine(Path.GetTempPath(), "ag805-pipes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var proxies = default(ChildProxies);
        try
        {
            var tool = Path.Combine(root, "tool");
            await File.WriteAllTextAsync(tool, "#!/bin/sh\nsleep 30 &\necho $! >> \"$(dirname \"$0\")/children\"\necho 'Address in use (os error 98)' >&2\nexit 134\n").ConfigureAwait(false);
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await File.WriteAllTextAsync(Path.Combine(root, ProxyFile.Arguments("awg0")), "PROXY_ARGS=ws://127.0.0.1:61001\n").ConfigureAwait(false);
            proxies = new ChildProxies(root, tool, TimeSpan.FromMilliseconds(50));
            await proxies.StartAsync("awg0", CancellationToken.None).ConfigureAwait(false);
            var state = await proxies.StateAsync("awg0", CancellationToken.None).ConfigureAwait(false);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (state.Falls < 2 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20).ConfigureAwait(false);
                state = await proxies.StateAsync("awg0", CancellationToken.None).ConfigureAwait(false);
            }

            Assert.True(state.Falls >= 2);
            Assert.Contains("The proxy listener address is already in use.", state.Message, StringComparison.Ordinal);
        }
        finally
        {
            proxies?.Dispose();
            var file = Path.Combine(root, "children");
            var children = File.Exists(file) ? File.ReadAllLines(file).Select(int.Parse).ToHashSet() : [];
            foreach (var child in Process.GetProcesses())
            {
                using (child)
                {
                    if (children.Contains(child.Id) && !child.HasExited)
                    {
                        child.Kill(true);
                        child.WaitForExit(TimeSpan.FromSeconds(2));
                    }
                }
            }

            Directory.Delete(root, true);
        }
    }

}
