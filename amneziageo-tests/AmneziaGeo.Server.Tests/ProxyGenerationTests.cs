using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Proxy;
using AmneziaGeo.Server.Routing.XHttp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServicePoint = AmneziaGeo.Server.Api.Services.ServicePoint;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ProxyGenerationTests
{
    private static readonly ProxyTargets Restricted = new(true, [new("127.0.0.1", 51820)]);

    [Fact]
    public async Task HttpBearerPolicyFollowsTheActiveChildGeneration()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var place = new Place();
        await using var host = place.Host(process => process.Kill(true));
        var original = place.Proxy;
        var credential = Assert.Single(original.Credentials);
        var identity = ProxyAccess.HttpIdentity(credential);
        var authenticated = original with { BearerIds = [identity] };
        Assert.Empty(await host.ApplyAsync([authenticated], default).ConfigureAwait(false));
        Assert.False(host.AllowsHeaders(original.Id, "", identity));
        Assert.True(host.AllowsHeaders(original.Id, "Bearer " + credential, identity));
        Assert.False(host.AllowsHeaders(original.Id, "Bearer " + Guid.NewGuid().ToString("D"), identity));
        Assert.True(host.TryLease(original.Id, out var old));
        using var held = old;
        Assert.Empty(await host.ApplyAsync([authenticated with { AllowAnonymous = true }], default).ConfigureAwait(false));
        Assert.True(old.Stopped.IsCancellationRequested);
        Assert.True(host.AllowsHeaders(original.Id, "", identity));
        await host.ApplyAsync([], default).ConfigureAwait(false);
        Assert.False(host.AllowsHeaders(original.Id, "Bearer " + credential, identity));
    }

    [Fact]
    public void RetirementCancelsExistingUsersAndAllowsLateLinkedRegistration()
    {
        using var lifetime = new ProxyLifetime();
        Assert.True(lifetime.TryLease(out var first));
        Assert.True(lifetime.TryLease(out var second));
        using var held = first;
        using var another = second;
        lifetime.Dispose();
        Assert.False(lifetime.IsActive);
        Assert.False(lifetime.TryLease(out _));
        Assert.True(first.Stopped.IsCancellationRequested);
        using var late = CancellationTokenSource.CreateLinkedTokenSource(first.Stopped, CancellationToken.None);
        Assert.True(late.IsCancellationRequested);
        second.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task FailedKillRevokesBeforeStopCompletesAndRevertRestartsTheChild()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var place = new Place();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var refuse = true;
        var kills = 0;
        await using var host = place.Host(process =>
        {
            Interlocked.Increment(ref kills);
            if (refuse)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                throw new Win32Exception("owned termination failure");
            }

            process.Kill(true);
        });
        var original = place.Proxy;
        Assert.Empty(await host.ApplyAsync([original], default).ConfigureAwait(false));
        Assert.True(host.TryLease(original.Id, out var old));
        using var held = old;
        var applying = Task.Run(() => host.ApplyAsync([original with { Targets = Restricted }], default));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(host.State(original.Id).IsRunning);
            Assert.False(host.CanRelay(original.Id));
            Assert.False(host.TryLease(original.Id, out _));
            Assert.True(old.Stopped.IsCancellationRequested);
            await RefusesFrontAsync(host, original).ConfigureAwait(false);
        }
        finally
        {
            release.Set();
        }

        Assert.NotEmpty(await applying.ConfigureAwait(false));
        Assert.True(host.State(original.Id).IsRunning);
        Assert.Contains("could not stop", host.State(original.Id).Fault);
        Assert.False(host.CanRelay(original.Id));
        refuse = false;
        Assert.Empty(await host.ApplyAsync([original], default).ConfigureAwait(false));
        Assert.True(host.CanRelay(original.Id));
        Assert.True(host.TryLease(original.Id, out var current));
        using var renewed = current;
        Assert.False(current.Stopped.IsCancellationRequested);
        Assert.True(kills >= 2);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("duplicate")]
    [InlineData("removed")]
    [InlineData("credentials")]
    public async Task RejectedAndRemovedPlansRevokeAChildThatCannotStop(string change)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var place = new Place();
        var refuse = true;
        var host = place.Host(process =>
        {
            if (refuse)
            {
                throw new Win32Exception("owned termination failure");
            }

            process.Kill(true);
        });
        try
        {
            var original = place.Proxy;
            Assert.Empty(await host.ApplyAsync([original], default).ConfigureAwait(false));
            Assert.True(host.TryLease(original.Id, out var lease));
            using var held = lease;
            var next = change switch
            {
                "invalid" => new[] { original with { Path = "invalid/path" } },
                "duplicate" => new[] { original, original },
                "credentials" => new[] { original with { Credentials = [Guid.NewGuid().ToString()] } },
                _ => Array.Empty<XHttpProxy>(),
            };
            Assert.NotEmpty(await host.ApplyAsync(next, default).ConfigureAwait(false));
            Assert.True(host.State(original.Id).IsRunning);
            Assert.False(host.CanRelay(original.Id));
            Assert.True(lease.Stopped.IsCancellationRequested);
            await RefusesFrontAsync(host, original).ConfigureAwait(false);
        }
        finally
        {
            refuse = false;
            await host.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task ProbeDoesNotConfirmOldAuthorizationAfterGenerationReplacement()
    {
        using var lifetime = new ProxyLifetime();
        Assert.True(lifetime.TryLease(out var lease));
        using var held = lease;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = Get();
        context.Request.QueryString = new QueryString("?host=127.0.0.1&port=51820");
        context.Response.Body = new MemoryStream();
        var answer = XHttpProbe.AnswerAsync(context, Guid.NewGuid().ToString("N"), async () =>
        {
            entered.SetResult();
            await resume.Task.ConfigureAwait(false);
            return true;
        }, true, () => Restricted, "xhttp", () => !held.Stopped.IsCancellationRequested);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        lifetime.Dispose();
        resume.SetResult();
        await answer.ConfigureAwait(false);
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task RevokedGenerationCancelsAnOpenXhttpRelay()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var lifetime = new ProxyLifetime();
        Assert.True(lifetime.TryLease(out var lease));
        using var held = lease;
        var context = Get();
        context.Request.Path = "/relay/session";
        var relay = XHttpRelay.PassAsync(context, ((IPEndPoint)listener.LocalEndpoint).Port,
            NullLogger.Instance, held.Stopped);
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var bytes = new byte[4096];
        Assert.True(await accepted.GetStream().ReadAsync(bytes).ConfigureAwait(false) > 0);
        lifetime.Dispose();
        await relay.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task RevokedGenerationAbortsAStartedXhttpResponseWithoutChangingItsStatus()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var lifetime = new ProxyLifetime();
        Assert.True(lifetime.TryLease(out var lease));
        using var held = lease;
        var context = Get();
        var response = new StartedResponse();
        var ending = new ObservedEnding();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Response.Body = new ObservedBody(response.Flushed);
        context.Features.Set<IHttpRequestLifetimeFeature>(ending);
        context.Request.Path = "/relay/session";
        var relay = XHttpRelay.PassAsync(context, ((IPEndPoint)listener.LocalEndpoint).Port,
            NullLogger.Instance, held.Stopped);
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var bytes = new byte[4096];
        Assert.True(await accepted.GetStream().ReadAsync(bytes).ConfigureAwait(false) > 0);
        await accepted.GetStream().WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 100000\r\n\r\nx"u8.ToArray()).ConfigureAwait(false);
        await response.Flushed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        lifetime.Dispose();
        await relay.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.True(ending.Aborted);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task WsFailedRestartCannotReauthorizeTheOldChildOrExistingLease()
    {
        using var place = new Place();
        var runner = new Runner();
        var host = new ProxyHost(new Ledger(), runner, place.Root);
        Assert.True((await host.ApplyAsync("catalog", 61001, 0, "relay", default).ConfigureAwait(false)).IsRunning);
        Assert.True(host.TryLease("catalog", out var old));
        using var held = old;
        runner.Fail = true;
        var failed = await host.ApplyAsync("catalog", 61001, 0, "relay", default, Restricted).ConfigureAwait(false);
        Assert.True(failed.IsRunning);
        Assert.NotEmpty(failed.Message);
        Assert.True(old.Stopped.IsCancellationRequested);
        Assert.False(host.TryLease("catalog", out _));
        Assert.True((await host.StateAsync("catalog", default).ConfigureAwait(false)).IsRunning);
        var starts = runner.Starts;
        runner.Fail = false;
        Assert.True((await host.ApplyAsync("catalog", 61001, 0, "relay", default, Restricted).ConfigureAwait(false)).IsRunning);
        Assert.Equal(starts + 1, runner.Starts);
        Assert.True(host.TryLease("catalog", out var current));
        using var renewed = current;
        Assert.False(current.Stopped.IsCancellationRequested);
        runner.Fail = true;
        await host.WithdrawAsync("catalog", default).ConfigureAwait(false);
        Assert.True(current.Stopped.IsCancellationRequested);
        Assert.False(host.CanRelay("catalog"));
    }

    [Fact]
    public async Task CorruptModernPolicyRevokesOldGenerationAndKeepsLegacyFixedTarget()
    {
        using var place = new Place();
        var runner = new Runner();
        var host = new ProxyHost(new Ledger(), runner, place.Root);
        await host.ApplyAsync("legacy", 61002, 51820, "old", default).ConfigureAwait(false);
        await host.ApplyAsync("catalog", 61001, 0, "relay", default).ConfigureAwait(false);
        Assert.True(host.TryLease("catalog", out var current));
        using var held = current;
        var result = await host.ApplyAsync("catalog", 61001, 0, "relay", default, new ProxyTargets(true, [])).ConfigureAwait(false);
        Assert.Empty(result.Message);
        Assert.True(current.Stopped.IsCancellationRequested);
        Assert.Contains("allow: []", await File.ReadAllTextAsync(host.RulesPath("catalog")).ConfigureAwait(false));
        Assert.True((await host.StateAsync("legacy", default).ConfigureAwait(false)).IsRunning);
        Assert.Contains("127", await File.ReadAllTextAsync(host.RulesPath("legacy")).ConfigureAwait(false));
        Assert.Contains("51820", await File.ReadAllTextAsync(host.RulesPath("legacy")).ConfigureAwait(false));
    }

    [Fact]
    public async Task MalformedWsPlanRetiresTheLeaseBeforeBackendWork()
    {
        using var place = new Place();
        var runner = new Runner();
        var host = new ProxyHost(new Ledger(), runner, place.Root);
        await host.ApplyAsync("catalog", 61001, 0, "relay", default).ConfigureAwait(false);
        Assert.True(host.TryLease("catalog", out var old));
        using var held = old;
        var starts = runner.Starts;
        var failed = await host.ApplyAsync("catalog", 61001, 0, null!, default).ConfigureAwait(false);
        Assert.NotEmpty(failed.Message);
        Assert.True(old.Stopped.IsCancellationRequested);
        Assert.False(host.CanRelay("catalog"));
        Assert.Equal(starts, runner.Starts);
    }

    [Fact]
    public void OneThrowingPolicyCallbackDoesNotLeaveOtherRetiredFlowsActive()
    {
        using var place = new Place();
        var log = new Warnings<ProxyHost>();
        var host = new ProxyHost(new Ledger(), new Runner(), place.Root, log);
        host.ApplyLegacyPolicies(new Dictionary<long, (ProxyTargets?, int)> { [1] = (null, 51820), [2] = (null, 51821) });
        Assert.True(host.TryLegacyLease(1, out var first));
        Assert.True(host.TryLegacyLease(2, out var second));
        using var one = first;
        using var two = second;
        using var throwing = first.Stopped.Register(() => throw new InvalidOperationException("owned callback failure"));
        host.ApplyLegacyPolicies(new Dictionary<long, (ProxyTargets?, int)>());
        Assert.True(first.Stopped.IsCancellationRequested);
        Assert.True(second.Stopped.IsCancellationRequested);
        Assert.False(host.TryLegacyLease(1, out _));
        Assert.False(host.TryLegacyLease(2, out _));
        Assert.IsType<AggregateException>(Assert.Single(log.Exceptions));
    }

    [Fact]
    public async Task OneThrowingXhttpCallbackDoesNotPreventOtherChildrenFromBeingRevoked()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var place = new Place();
        var log = new Warnings<XHttpHost>();
        await using var host = place.Host(process => process.Kill(true), log);
        var other = place.Proxy with { Id = 8, Port = 18081 };
        Assert.Empty(await host.ApplyAsync([place.Proxy, other], default).ConfigureAwait(false));
        Assert.True(host.TryLease(7, out var first));
        Assert.True(host.TryLease(8, out var second));
        using var one = first;
        using var two = second;
        using var throwing = first.Stopped.Register(() => throw new InvalidOperationException("owned callback failure"));
        Assert.Empty(await host.ApplyAsync([], default).ConfigureAwait(false));
        Assert.True(first.Stopped.IsCancellationRequested);
        Assert.True(second.Stopped.IsCancellationRequested);
        Assert.False(host.State(7).IsRunning);
        Assert.False(host.State(8).IsRunning);
        Assert.IsType<AggregateException>(Assert.Single(log.Exceptions));
    }

    [Fact]
    public async Task KnownCurrentWsGenerationSurvivesAutomaticRecoveryWithHistoricalMessage()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var place = new Place();
        var tool = Path.Combine(place.Root, "recovering-ws");
        File.WriteAllText(tool, "#!/bin/sh\necho run >> \"$(dirname \"$0\")/runs\"\nif [ ! -f \"$(dirname \"$0\")/first\" ]; then touch \"$(dirname \"$0\")/first\"; sleep 1; exit 3; fi\nexec sleep 60\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        using var children = new ChildProxies(place.Root, tool, TimeSpan.FromMilliseconds(50));
        var host = new ProxyHost(new Ledger(), children, place.Root);
        Assert.True((await host.ApplyAsync("catalog", 61001, 0, "relay", default).ConfigureAwait(false)).IsRunning);
        Assert.True(host.TryLease("catalog", out var lease));
        using var held = lease;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var state = await host.StateAsync("catalog", default).ConfigureAwait(false);
        while ((!state.IsRunning || state.Message.Length == 0) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25).ConfigureAwait(false);
            state = await host.StateAsync("catalog", default).ConfigureAwait(false);
        }

        Assert.True(state.IsRunning);
        Assert.NotEmpty(state.Message);
        while (File.ReadAllLines(Path.Combine(place.Root, "runs")).Length < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25).ConfigureAwait(false);
        }

        var runs = File.ReadAllLines(Path.Combine(place.Root, "runs")).Length;
        Assert.True(runs >= 2);
        var unchanged = await host.ApplyAsync("catalog", 61001, 0, "relay", default).ConfigureAwait(false);
        Assert.True(unchanged.IsRunning);
        Assert.Equal(state.Message, unchanged.Message);
        Assert.Equal(runs, File.ReadAllLines(Path.Combine(place.Root, "runs")).Length);
        Assert.False(lease.Stopped.IsCancellationRequested);
        Assert.True(host.CanRelay("catalog"));
    }

    private static DefaultHttpContext Get()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        return context;
    }

    private static async Task RefusesFrontAsync(XHttpHost host, XHttpProxy proxy)
    {
        using var bench = new Bench();
        using var identity = new ServiceTlsFixture();
        var registered = identity.Secure(new AmneziaGeo.Server.Dal.TransportProxyEntity
        {
            Id = proxy.Id, Name = "catalog", Host = "localhost", Port = proxy.Port, Path = proxy.Path,
            Transport = "http", ProtocolVersion = 2,
        });
        bench.Db.TransportProxies.Add(registered);
        await bench.Db.SaveChangesAsync(default).ConfigureAwait(false);
        var desk = new ServiceDesk(bench.Scopes, new SpeedTickets(), [], new SubscriptionState(),
            new Life(), Options.Create(new JsonOptions()), NullLogger<ServiceDesk>.Instance, xhttp: host);
        var endpoint = new ServiceEndpoint(0, "catalog", false, proxy.Port, 0, proxy.Path)
        {
            ProxyId = proxy.Id, XHttp = true, UseCertificate = true,
        };
        var context = Get();
        ServiceTlsFixture.SecureConnection(context);
        context.Request.Scheme = "https";
        context.Request.Path = "/" + proxy.Path + "/session/0";
        await desk.AnswerAsync(context, new ServicePoint(80, [endpoint], "", "")).ConfigureAwait(false);
        Assert.Equal(404, context.Response.StatusCode);
    }

    private sealed class Place : IDisposable
    {
        private readonly HashSet<Process> _children = [];
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "proxy-generation-" + Guid.NewGuid().ToString("N"));
        public XHttpProxy Proxy { get; } = new(7, 18080, "relay", [Guid.NewGuid().ToString()]);

        /// <summary>
        /// ctor
        /// </summary>
        public Place() => Directory.CreateDirectory(Root);

        public XHttpHost Host(Action<Process> kill, ILogger<XHttpHost>? logger = null)
        {
            var file = Path.Combine(Root, "fake-get");
            File.WriteAllText(file, "#!/bin/sh\nif [ \"$2\" = --help ]; then echo 'get-server'; exit 0; fi\nexec sleep 60\n");
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            return new XHttpHost(Root, file, logger ?? NullLogger<XHttpHost>.Instance, process =>
            {
                _children.Add(process);
                kill(process);
            });
        }

        public void Dispose()
        {
            foreach (var process in _children)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            Directory.Delete(Root, true);
        }
    }

    private sealed class Runner : IProxyRunner
    {
        public bool Fail { get; set; }
        public int Starts { get; private set; }
        public Task<ProxyState> EnableAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Up);
        public Task<ProxyState> StateAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Up);
        public Task<ProxyState> StartAsync(string name, CancellationToken ct)
        {
            Starts++;
            return Task.FromResult(Fail ? new ProxyState(true, "owned restart failure") : ProxyState.Up);
        }

        public Task<ProxyState> StopAsync(string name, CancellationToken ct) =>
            Task.FromResult(Fail ? new ProxyState(true, "owned stop failure") : ProxyState.Down);
    }

    private sealed class StartedResponse : HttpResponseFeature
    {
        public TaskCompletionSource Flushed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool HasStarted => true;

        /// <summary>
        /// ctor
        /// </summary>
        public StartedResponse() => Body = new ObservedBody(Flushed);
    }

    private sealed class ObservedBody(TaskCompletionSource flushed) : MemoryStream
    {
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            flushed.TrySetResult();
            return base.FlushAsync(cancellationToken);
        }
    }

    private sealed class ObservedEnding : IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }
        public CancellationToken RequestAborted { get; set; }
        public void Abort() => Aborted = true;
    }

    private sealed class Warnings<T> : ILogger<T>
    {
        public List<Exception> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Warning && exception is not null)
            {
                Exceptions.Add(exception);
            }
        }
    }

    private sealed class Life : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => default;
        public CancellationToken ApplicationStopping => default;
        public CancellationToken ApplicationStopped => default;
        public void StopApplication()
        {
        }
    }
}
