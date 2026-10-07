using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public class SignalTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ASignalIsSealedUnderAKeyOfItsOwn()
    {
        var server = Curve25519.Create();
        var client = Curve25519.Create();
        var shared = PeerToken.Shared(server.PrivateKey, client.PublicKey);
        var nonce = PeerToken.Nonce();
        var body = "{\"signal\":\"disconnect\"}"u8.ToArray();

        var signal = PeerToken.Seal(shared, nonce, body, PeerToken.SignalContext);

        Assert.Equal(body, PeerToken.Open(PeerToken.Shared(client.PrivateKey, server.PublicKey), nonce, signal, PeerToken.SignalContext));
        Assert.Null(PeerToken.Open(shared, nonce, signal));
        Assert.Null(PeerToken.Open(shared, nonce, PeerToken.Seal(shared, nonce, body), PeerToken.SignalContext));
    }

    [Fact]
    public async Task AnApplicationThatListensTakesTheSignal()
    {
        var (endpoint, client) = Pair();
        await using var application = new Application(client.PrivateKey, endpoint.PublicKey);
        var signal = new DisconnectSignal(application.DialAsync);

        var outcome = await signal.SendAsync(endpoint, client, CancellationToken.None);

        Assert.True(outcome.IsTaken, outcome.Message);
        Assert.Equal("disconnect", application.Heard);
        Assert.Equal(IPAddress.Parse("10.8.0.2"), application.Dialled?.Address);
        Assert.Equal(DisconnectSignal.Port, application.Dialled?.Port);
    }

    [Fact]
    public async Task TheSignalReachesAClientOfIpv6AloneAtItsAddress()
    {
        var (endpoint, client) = Pair();
        client = client with { Address = ["fd00::2/128"] };
        await using var application = new Application(client.PrivateKey, endpoint.PublicKey);
        var signal = new DisconnectSignal(application.DialAsync);

        var outcome = await signal.SendAsync(endpoint, client, CancellationToken.None);

        Assert.True(outcome.IsTaken, outcome.Message);
        Assert.Equal(IPAddress.Parse("fd00::2"), application.Dialled?.Address);
    }

    [Fact]
    public async Task APortNothingListensOnIsNamedSo()
    {
        var (endpoint, client) = Pair();
        var signal = new DisconnectSignal(Application.DialNowhereAsync);

        var outcome = await signal.SendAsync(endpoint, client, CancellationToken.None);

        Assert.False(outcome.IsTaken);
        Assert.Equal(DisconnectSignal.Refused, outcome.Error);
        Assert.Contains("10.8.0.2:" + DisconnectSignal.Port, outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnApplicationThatStaysSilentIsNotWaitedForLong()
    {
        var (endpoint, client) = Pair();
        await using var application = new Application(client.PrivateKey, endpoint.PublicKey) { Silent = true };
        var signal = new DisconnectSignal(application.DialAsync, TimeSpan.FromMilliseconds(200));

        var outcome = await signal.SendAsync(endpoint, client, CancellationToken.None);

        Assert.False(outcome.IsTaken);
        Assert.Equal(DisconnectSignal.NoAnswer, outcome.Error);
    }

    [Fact]
    public async Task WhatDoesNotHoldTheKeysOfTheClientIsNotTaken()
    {
        var (endpoint, client) = Pair();
        await using var stranger = new Application(Curve25519.Create().PrivateKey, endpoint.PublicKey);
        var signal = new DisconnectSignal(stranger.DialAsync);

        var outcome = await signal.SendAsync(endpoint, client, CancellationToken.None);

        Assert.False(outcome.IsTaken);
        Assert.Equal(DisconnectSignal.BadAnswer, outcome.Error);
        Assert.Null(stranger.Heard);
    }

    [Fact]
    public async Task WhatOpensWithAnythingButANonceIsNotTheApplication()
    {
        var (endpoint, client) = Pair();
        await using var other = new Application(client.PrivateKey, endpoint.PublicKey) { Greeting = "SSH-2.0-OpenSSH_9.6\n" };
        var signal = new DisconnectSignal(other.DialAsync);

        var outcome = await signal.SendAsync(endpoint, client, CancellationToken.None);

        Assert.False(outcome.IsTaken);
        Assert.Equal(DisconnectSignal.BadAnswer, outcome.Error);
    }

    [Fact]
    public async Task AClientWithoutAnAddressIsNotDialled()
    {
        var (endpoint, client) = Pair();
        await using var application = new Application(client.PrivateKey, endpoint.PublicKey);
        var signal = new DisconnectSignal(application.DialAsync);

        var outcome = await signal.SendAsync(endpoint, client with { Address = [] }, CancellationToken.None);

        Assert.False(outcome.IsTaken);
        Assert.Equal(DisconnectSignal.NoAddress, outcome.Error);
        Assert.Null(application.Dialled);
    }

    [Fact]
    public void TheSignalComesFromTheAddressesOfTheEndpoint()
    {
        var (endpoint, client) = Pair();

        Assert.Equal(["10.8.0.1", "fd00::1"], DisconnectSignal.Sources(endpoint with { Address = ["10.8.0.1/24", "fd00::1/64"] }));
        Assert.Equal(IPAddress.Parse("10.8.0.2"), DisconnectSignal.Target(client with { Address = ["fd00::2/128", "10.8.0.2/32"] }));
    }

    [Fact]
    public async Task OnlyTheClientsThatAreOnAndConnectedAreSignalled()
    {
        var (endpoint, client) = Pair();
        var away = client with { Id = 8, Name = "away" };
        var off = client with { Id = 9, Name = "off", IsEnabled = false };
        var elsewhere = client with { Id = 10, Name = "elsewhere", ConfigId = 99 };
        await using var application = new Application(client.PrivateKey, endpoint.PublicKey);
        var signals = Signals(new DisconnectSignal(application.DialAsync), (_, one) => one.Id != away.Id);

        await signals.SendAsync([client, away, off, elsewhere], [endpoint], CancellationToken.None);

        Assert.Equal(1, application.Calls);
        Assert.Null(signals.Missed(client.Id));
        Assert.Null(signals.Missed(away.Id));
        Assert.Null(signals.Missed(off.Id));
    }

    [Fact]
    public async Task TheClientsOfAnEndpointThatIsOffAreNotSignalled()
    {
        var (endpoint, client) = Pair();
        await using var application = new Application(client.PrivateKey, endpoint.PublicKey);
        var signals = Signals(new DisconnectSignal(application.DialAsync), (_, _) => true);

        await signals.SendAsync([client], [endpoint with { IsEnabled = false }], CancellationToken.None);

        Assert.Equal(0, application.Calls);
    }

    [Fact]
    public async Task ASignalThatWasNotTakenIsKeptUntilItIsForgotten()
    {
        var (endpoint, client) = Pair();
        var clock = new Clock(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var signals = Signals(new DisconnectSignal(Application.DialNowhereAsync), (_, _) => true, clock);

        await signals.SendAsync([client], [endpoint], CancellationToken.None);
        var missed = signals.Missed(client.Id);
        signals.Forget(client.Id);

        Assert.Equal(DisconnectSignal.Refused, missed?.Error);
        Assert.Equal(clock.Now, missed?.At);
        Assert.Null(signals.Missed(client.Id));
    }

    [Fact]
    public async Task ASignalThatIsTakenDropsTheOneThatWasNot()
    {
        var (endpoint, client) = Pair();
        await using var application = new Application(client.PrivateKey, endpoint.PublicKey);
        var reached = false;
        var signals = Signals(
            new DisconnectSignal((address, port, ct) => reached ? application.DialAsync(address, port, ct) : Application.DialNowhereAsync(address, port, ct)),
            (_, _) => true);

        await signals.SendAsync([client], [endpoint], CancellationToken.None);
        var first = signals.Missed(client.Id);
        reached = true;
        await signals.SendAsync([client], [endpoint], CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(signals.Missed(client.Id));
    }

    private static ClientSignals Signals(DisconnectSignal signal, Func<ServerConfig, TunnelClient, bool> connected, Clock? clock = null) =>
        new(signal, connected, clock ?? new Clock(DateTimeOffset.UnixEpoch), NullLogger<ClientSignals>.Instance);

    private static (ServerConfig Endpoint, TunnelClient Client) Pair()
    {
        var server = Curve25519.Create();
        var device = Curve25519.Create();
        var endpoint = new ServerConfig
        {
            Id = 1,
            Name = "awg1",
            ListenPort = 51820,
            Address = ["10.8.0.1/24"],
            PrivateKey = server.PrivateKey,
            PublicKey = server.PublicKey,
        };
        var client = new TunnelClient
        {
            Id = 7,
            ConfigId = 1,
            Name = "milena",
            Address = ["10.8.0.2/32"],
            PrivateKey = device.PrivateKey,
            PublicKey = device.PublicKey,
        };

        return (endpoint, client);
    }

    // The application of a client on the loopback: it takes the signal the way the device does.
    private sealed class Application : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        private readonly byte[] _shared;

        private readonly Task _serving;

        private int _calls;

        /// <summary>
        /// ctor
        /// </summary>
        public Application(string privateKey, string serverKey)
        {
            _shared = PeerToken.Shared(privateKey, serverKey);
            _listener.Start();
            _serving = ServeAsync();
        }

        public bool Silent { get; init; }

        public string? Greeting { get; init; }

        public string? Heard { get; private set; }

        public IPEndPoint? Dialled { get; private set; }

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask<Stream> DialAsync(IPAddress address, int port, CancellationToken ct)
        {
            Dialled = new IPEndPoint(address, port);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(_listener.LocalEndpoint, ct);

            return new NetworkStream(socket, ownsSocket: true);
        }

        public static async ValueTask<Stream> DialNowhereAsync(IPAddress address, int port, CancellationToken ct)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var free = probe.LocalEndpoint;
            probe.Stop();
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(free, ct);
            }
            catch
            {
                socket.Dispose();

                throw;
            }

            return new NetworkStream(socket, ownsSocket: true);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _serving;
        }

        private async Task ServeAsync()
        {
            while (true)
            {
                try
                {
                    using var caller = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _calls);
                    await TakeAsync(caller.GetStream());
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
                catch (IOException)
                {
                }
            }
        }

        private async Task TakeAsync(NetworkStream stream)
        {
            if (Silent)
            {
                await stream.CopyToAsync(Stream.Null);

                return;
            }

            var nonce = PeerToken.Nonce();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(Greeting ?? JsonSerializer.Serialize(new { nonce }, Web) + "\n"));
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            if (await reader.ReadLineAsync() is not { } line
                || JsonSerializer.Deserialize<SealedAnswer>(line, Web) is not { } signal
                || PeerToken.Open(_shared, nonce, signal, PeerToken.SignalContext) is not { } opened)
            {
                return;
            }

            using var word = JsonDocument.Parse(opened);
            Heard = word.RootElement.GetProperty("signal").GetString();
            var answer = PeerToken.Seal(
                _shared,
                nonce,
                JsonSerializer.SerializeToUtf8Bytes(new { signal = Heard, taken = true }, Web),
                PeerToken.SignalContext);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(answer, Web) + "\n"));
        }
    }
}
