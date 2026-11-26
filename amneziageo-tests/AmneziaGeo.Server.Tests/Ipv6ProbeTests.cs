using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Routing.Host;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class Ipv6ProbeTests
{
    private const string Source = "2a01:4f8:1:2::5";

    [Theory]
    [InlineData("fe80::1")]
    [InlineData("fd00:1::5")]
    [InlineData("fc00::1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("192.0.2.1")]
    [InlineData("::ffff:192.0.2.1")]
    [InlineData("not-an-address")]
    [InlineData("")]
    public async Task NonGlobalSourcesNeverStartAConnection(string source)
    {
        var connected = 0;
        var result = await Probe(new Commands(Route(source)), (_, _, _) =>
        {
            Interlocked.Increment(ref connected);
            return Task.FromResult(true);
        }).CheckAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(new Ipv6Reach(false, "no-source"), result);
        Assert.Equal(0, connected);
    }

    [Fact]
    public async Task AGlobalSourceConnectsToOnlyTheFixedTargets()
    {
        var seen = new System.Collections.Concurrent.ConcurrentBag<(IPAddress Source, IPAddress Target)>();
        var commands = new Commands(Route(Source));
        var host = new Ledger();
        var result = await Probe(commands, (source, target, _) =>
        {
            seen.Add((source, target));
            return Task.FromResult(true);
        }, host: host).CheckAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(new Ipv6Reach(true, "available"), result);
        Assert.Equal(2, seen.Count);
        Assert.All(seen, pair => Assert.Equal(IPAddress.Parse(Source), pair.Source));
        Assert.Equal(["2001:4860:4860::8888", "2606:4700:4700::1111"], seen.Select(pair => pair.Target.ToString()).Order().ToArray());
        Assert.All(commands.Calls, words => Assert.Equal(["-j", "-6", "route", "get"], words.Take(4)));
        Assert.Empty(host.Steps);
    }

    [Fact]
    public async Task ASrcFieldIsAccepted()
    {
        var result = await Probe(new Commands(Route(Source).Replace("prefsrc", "src")))
            .CheckAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.True(result.Available);
    }

    [Fact]
    public async Task OneReachableTargetIsEnough()
    {
        var result = await Probe(new Commands(Route(Source)), (_, target, _) => Task.FromResult(target.ToString().StartsWith("2606:", StringComparison.Ordinal)))
            .CheckAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.True(result.Available);
    }

    [Theory]
    [InlineData("[]", "no-route")]
    [InlineData("{}", "probe-failed")]
    [InlineData("[null]", "probe-failed")]
    [InlineData("not-json", "probe-failed")]
    [InlineData("[{\"dev\":\"eth0\"}]", "no-source")]
    [InlineData("[{\"prefsrc\":\"2a01:4f8:1:2::5\"}]", "no-route")]
    [InlineData("[{\"type\":\"unreachable\",\"dev\":\"eth0\",\"prefsrc\":\"2a01:4f8:1:2::5\"}]", "no-route")]
    [InlineData("[{\"type\":\"blackhole\",\"dev\":\"eth0\",\"prefsrc\":\"2a01:4f8:1:2::5\"}]", "no-route")]
    [InlineData("[{\"type\":\"prohibit\",\"dev\":\"eth0\",\"prefsrc\":\"2a01:4f8:1:2::5\"}]", "no-route")]
    public async Task UnusableRoutesAreIdentified(string json, string reason)
    {
        var result = await Probe(new Commands(json)).CheckAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(new Ipv6Reach(false, reason), result);
    }

    [Theory]
    [InlineData(2, "RTNETLINK answers: Network is unreachable", "no-route")]
    [InlineData(2, "RTNETLINK answers: No route to host", "no-route")]
    [InlineData(2, "RTNETLINK answers: Operation not permitted", "probe-failed")]
    [InlineData(127, "ip: not found", "unsupported")]
    public async Task ToolRefusalsAreIdentified(int code, string error, string reason)
    {
        var commands = new Commands(_ => Task.FromResult(new CommandResult(code, "", error)));
        Assert.Equal(new Ipv6Reach(false, reason), await Probe(commands).CheckAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task DifferentIpv6UplinkCannotUseTheCurrentNatRules()
    {
        var called = false;
        var result = await Probe(new Commands(Route(Source, "six0")), (_, _, _) =>
        {
            called = true;
            return Task.FromResult(true);
        }).CheckAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(new Ipv6Reach(false, "uplink-mismatch"), result);
        Assert.False(called);
    }

    [Fact]
    public async Task FailedConnectionsDoNotAdvertiseIpv6()
    {
        var result = await Probe(new Commands(Route(Source)), (_, _, _) => Task.FromResult(false))
            .CheckAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(new Ipv6Reach(false, "unreachable"), result);
    }

    [Fact]
    public async Task ARefusedConnectionIsUnreachable()
    {
        var result = await Probe(new Commands(Route(Source)), (_, _, _) => Task.FromException<bool>(new SocketException((int)SocketError.ConnectionRefused)))
            .CheckAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(new Ipv6Reach(false, "unreachable"), result);
    }

    [Fact]
    public async Task TheCommonDeadlineBoundsBothTargets()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = Probe(new Commands(Route(Source)), (_, _, _) => pending.Task, TimeSpan.FromMilliseconds(50));
        var result = await probe.CheckAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Assert.Equal(new Ipv6Reach(false, "unreachable"), result);
        pending.SetResult(false);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var cancel = new CancellationTokenSource();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = Probe(new Commands(Route(Source)), async (_, _, ct) =>
        {
            connected.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            return true;
        });
        var check = probe.CheckAsync(cancel.Token);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check).ConfigureAwait(false);
    }

    [Fact]
    public async Task ACommandExceptionReturnsAReason()
    {
        var commands = new Commands(_ => Task.FromException<CommandResult>(new HostNetworkException("tool missing")));
        Assert.Equal(new Ipv6Reach(false, "probe-failed"), await Probe(commands).CheckAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task ADisabledIpv6StackDoesNotRunHostCommands()
    {
        var commands = new Commands(Route(Source));
        var probe = new Ipv6Probe(commands, new Ledger(), NullLogger<Ipv6Probe>.Instance, (_, _, _) => Task.FromResult(true), TimeSpan.FromSeconds(3), false);
        Assert.Equal(new Ipv6Reach(false, "unsupported"), await probe.CheckAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Empty(commands.Calls);
    }

    private static Ipv6Probe Probe(Commands commands, Func<IPAddress, IPAddress, CancellationToken, Task<bool>>? connect = null, TimeSpan? limit = null, Ledger? host = null) =>
        new(commands, host ?? new Ledger(), NullLogger<Ipv6Probe>.Instance, connect ?? ((_, _, _) => Task.FromResult(true)), limit ?? TimeSpan.FromSeconds(3), true);

    private static string Route(string source, string device = "eth0") =>
        JsonSerializer.Serialize(new[] { new { dev = device, prefsrc = source } });

    private sealed class Commands : IHostCommands
    {
        private readonly Func<CancellationToken, Task<CommandResult>> _run;

        /// <summary>
        /// ctor
        /// </summary>
        public Commands(string json) : this(_ => Task.FromResult(new CommandResult(0, json, "")))
        {
        }

        /// <summary>
        /// ctor
        /// </summary>
        public Commands(Func<CancellationToken, Task<CommandResult>> run)
        {
            _run = run;
        }

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<CommandResult> RunAsync(string file, IReadOnlyList<string> arguments, string? input, CancellationToken ct)
        {
            Assert.Equal("ip", file);
            Assert.Null(input);
            Calls.Add(arguments);
            return _run(ct);
        }
    }
}
