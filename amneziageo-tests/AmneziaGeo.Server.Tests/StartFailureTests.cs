using AmneziaGeo.Server.Api.Web;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public class StartFailureTests
{
    [Fact]
    public void AnAddressAnotherProgramHoldsIsToldFromOtherFailures()
    {
        var taken = new IOException(
            "Failed to bind to address http://127.0.0.1:8443: address already in use.",
            new AddressInUseException("Address already in use"));

        Assert.True(StartFailure.IsPortTaken(taken));
        Assert.False(StartFailure.IsPortTaken(new IOException("the disk is full")));
        Assert.False(StartFailure.IsPortTaken(new InvalidOperationException("no service", new AddressInUseException("x"))));
    }

    [Fact]
    public void TheLineNamesTheAddressAndTheCommandThatMovesThePanel()
    {
        var taken = new IOException(
            "Failed to bind to address http://127.0.0.1:8443: address already in use.",
            new AddressInUseException("Address already in use"));

        var line = StartFailure.Line(taken);

        Assert.Equal(
            "the panel did not start: Failed to bind to address http://127.0.0.1:8443: address already in use; "
            + "free the port or move the panel with 'amneziageo-server panel set --port <port>'",
            line);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(1, StartFailure.PortTaken);
    }

    [Fact]
    public async Task AFailedStartStopsWhatItSetGoingWhileTheServicesAreStillThere()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<Settler>();
        builder.Services.AddHostedService(services => services.GetRequiredService<Settler>());
        builder.Services.AddHostedService<TakenPort>();
        var host = builder.Build();
        var settler = host.Services.GetRequiredService<Settler>();
        var heard = new Heard();

        var failure = await Assert.ThrowsAsync<IOException>(() => host.StartAsync());
        var code = await StartFailure.LeaveAsync(host, heard, failure);

        Assert.Equal(StartFailure.PortTaken, code);
        Assert.Equal("laid on the way out", await settler.Left.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.StartsWith("Critical the panel did not start: ", heard.Lines[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(heard.Lines, line => line.StartsWith("Warning", StringComparison.Ordinal));
    }

    // Lays something through the services on its way out, as the queue of names and the relay do.
    private sealed class Settler : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;

        private readonly TaskCompletionSource<string> _left = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _running = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// ctor
        /// </summary>
        public Settler(IServiceScopeFactory scopes)
        {
            _scopes = scopes;
        }

        public Task<string> Left => _left.Task;

        public Task Running => _running.Task;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            stoppingToken.Register(() =>
            {
                Leave();
                stopped.TrySetResult();
            });
            _running.TrySetResult();

            return stopped.Task;
        }

        // Takes a scope of the services on the way out, at the moment the host tells the service to stop.
        private void Leave()
        {
            try
            {
                using var scope = _scopes.CreateScope();
                _left.TrySetResult("laid on the way out");
            }
            catch (ObjectDisposedException)
            {
                _left.TrySetResult("the services were gone");
            }
        }
    }

    // Fails to start as the web server does when another program holds its port, once the service before it runs.
    private sealed class TakenPort : IHostedService
    {
        private readonly Settler _settler;

        /// <summary>
        /// ctor
        /// </summary>
        public TakenPort(Settler settler)
        {
            _settler = settler;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _settler.Running.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            throw new IOException(
                "Failed to bind to address http://127.0.0.1:8443: address already in use.",
                new AddressInUseException("Address already in use"));
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Keeps what is written to the log, one line a record.
    private sealed class Heard : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{logLevel} {formatter(state, exception)}");
    }
}
