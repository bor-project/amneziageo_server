using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Proxies;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Routing.Udp;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class SharedUdpHostTests
{
    [Fact]
    public async Task APanelRestartPreservesThePublicAddressesOfExistingPeers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ag793-restart-" + Guid.NewGuid().ToString("N"));
        var config = ConfigDefaults.Fresh("awg1") with { ListenPort = Free() };
        var client = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 41234);
        var kernel = new Devices();
        kernel.Hold(new AwgDevice { Name = config.Name, ListenPort = (ushort)config.ListenPort,
            Peers = [new AwgPeer { PublicKey = Curve25519.Create().PublicKey, Endpoint = client }] });
        try
        {
            await using (var first = new SharedUdpHost(new UdpPorts(), new Ledger(), kernel, NullLogger<SharedUdpHost>.Instance, directory))
            {
                await first.PrepareAsync([config], new HashSet<int> { config.ListenPort }, default).ConfigureAwait(false);
                var relay = kernel.Find(config.Name)!.Peers[0].Endpoint!;
                Assert.True(IPAddress.IsLoopback(relay.Address));
                Assert.Equal(client, first.Resolve(relay));
                Assert.Equal(relay, first.KernelEndpoint(config.Name, client));
                Assert.Equal(relay, first.KernelEndpoint(config.Name, relay));
            }

            var file = Path.Combine(directory, "peers.json");
            Assert.True(File.Exists(file));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }

            await using var second = new SharedUdpHost(new UdpPorts(), new Ledger(), kernel, NullLogger<SharedUdpHost>.Instance, directory);
            await second.PrepareAsync([config], new HashSet<int> { config.ListenPort }, default).ConfigureAwait(false);
            Assert.Equal(client, second.Resolve(kernel.Find(config.Name)!.Peers[0].Endpoint!));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task InterfacesAndQuicReceiveDistinctPrivatePortsWithoutChangingPublicSettings()
    {
        var port = Free();
        var first = ConfigDefaults.Fresh("quic") with { ListenPort = port };
        var second = ConfigDefaults.Fresh("awg2") with { ListenPort = port };
        var kernel = new Devices();
        kernel.Hold(first.Name, port);
        kernel.Hold(second.Name, port);
        var map = new UdpPorts();
        var network = new Ledger();
        await using var host = new SharedUdpHost(map, network, kernel, NullLogger<SharedUdpHost>.Instance);
        await host.PrepareAsync([first, second], new HashSet<int> { port }, default).ConfigureAwait(false);
        Assert.Equal(3, map.Vpn.Values.Concat(map.Quic.Values).Distinct().Count());
        Assert.DoesNotContain(port, map.Vpn.Values);
        Assert.DoesNotContain(port, map.Quic.Values);
        Assert.Equal(port, first.ListenPort);
        Assert.True(map.Runtime(first).UdpPrivate);
        Assert.Equal(first.PrivateKey, map.Runtime(first).PrivateKey);
        Assert.Equal(map.Vpn[first.Name], kernel.Find(first.Name)!.ListenPort);
        var before = map.Vpn[first.Name];
        await host.PrepareAsync([first, second], new HashSet<int> { port }, default).ConfigureAwait(false);
        Assert.Equal(before, map.Vpn[first.Name]);
    }

    [Fact]
    public async Task AnExternalListenerRefusalRestoresTheKernelPortAndMap()
    {
        using var occupied = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        occupied.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        var port = ((IPEndPoint)occupied.LocalEndPoint!).Port;
        var config = ConfigDefaults.Fresh("awg1") with { ListenPort = port };
        var kernel = new Devices();
        kernel.Hold(config.Name, port);
        var map = new UdpPorts();
        var network = new Ledger();
        await using var host = new SharedUdpHost(map, network, kernel, NullLogger<SharedUdpHost>.Instance);
        await Assert.ThrowsAsync<SocketException>(() => host.PrepareAsync([config], new HashSet<int> { port }, default)).ConfigureAwait(false);
        Assert.Empty(map.Vpn);
        Assert.Empty(map.Quic);
        Assert.Equal(port, kernel.Find(config.Name)!.ListenPort);
        Assert.DoesNotContain("counter drop", network.Ruleset);
    }

    [Fact]
    public async Task AmbiguousKeysAreRejectedBeforeMovingAnInterface()
    {
        var first = ConfigDefaults.Fresh("awg1") with { ListenPort = Free() };
        var second = first with { Name = "awg2" };
        var kernel = new Devices();
        var map = new UdpPorts();
        await using var host = new SharedUdpHost(map, new Ledger(), kernel, NullLogger<SharedUdpHost>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.PrepareAsync([first, second], new HashSet<int>(), default)).ConfigureAwait(false);
        Assert.Equal("udp-identity-taken", error.Message);
        Assert.Empty(map.Vpn);
        Assert.Empty(kernel.Updates);
    }

    [Fact]
    public async Task RestoringAProxyPreservesItsTokensSecretAndAccessVersion()
    {
        using var bench = new Bench();
        var store = new AmneziaGeo.Server.Dal.TransportProxyStore(bench.Db);
        var saved = await store.SaveAsync(0, new()
        {
            Name = "first", Host = "example.net", Port = 443, Path = "first", AllowAnonymous = true,
        }, default).ConfigureAwait(false);
        await store.MintAsync(saved.Id, "phone", default).ConfigureAwait(false);
        var previous = (await store.FindAsync(saved.Id, default).ConfigureAwait(false))!;
        await store.SaveAsync(saved.Id, new() { Name = "changed", Host = "example.net", Port = 443, Path = "second", AllowAnonymous = false }, default).ConfigureAwait(false);
        await store.RestoreAsync(saved.Id, previous, null, null, default).ConfigureAwait(false);
        var restored = (await store.FindAsync(saved.Id, default).ConfigureAwait(false))!;
        Assert.Equal(previous.Name, restored.Name);
        Assert.Equal(previous.Secret, restored.Secret);
        Assert.Equal(previous.TokensJson, restored.TokensJson);
        Assert.Equal(previous.AccessVersion, restored.AccessVersion);
        Assert.True(restored.AllowAnonymous);
    }

    [Fact]
    public async Task AFailedListenerIsRecreatedWithTheExistingPublicPeer()
    {
        var config = ConfigDefaults.Fresh("vpn") with { ListenPort = Free() };
        var client = new IPEndPoint(IPAddress.Parse("203.0.113.18"), 42345);
        var kernel = new Devices();
        kernel.Hold(new AwgDevice { Name = config.Name, ListenPort = (ushort)config.ListenPort,
            Peers = [new AwgPeer { PublicKey = Curve25519.Create().PublicKey, Endpoint = client }] });
        await using var host = new SharedUdpHost(new UdpPorts(), new Ledger(), kernel, NullLogger<SharedUdpHost>.Instance);
        var quic = new HashSet<int> { config.ListenPort };
        await host.PrepareAsync([config], quic, default).ConfigureAwait(false);
        var fronts = (Dictionary<int, UdpFront>)typeof(SharedUdpHost).GetField("_fronts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
        var failed = fronts[config.ListenPort];
        var socket = (Socket)typeof(UdpFront).GetField("_socket", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(failed)!;
        socket.Dispose();
        for (var attempt = 0; attempt < 100 && failed.IsRunning; attempt++)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.False(failed.IsRunning);
        await host.PrepareAsync([config], quic, default).ConfigureAwait(false);
        Assert.NotSame(failed, fronts[config.ListenPort]);
        Assert.True(fronts[config.ListenPort].IsRunning);
        Assert.Equal(client, host.Resolve(kernel.Find(config.Name)!.Peers[0].Endpoint!));
    }

    [Fact]
    public void QuicCertificateSharingComparesTheIdentityAcrossFilePaths()
    {
        using var first = new ProxyTlsFixture("proxy.example.net");
        using var second = new ProxyTlsFixture("proxy.example.net");
        var alias = first.Chain + ".copy";
        var key = first.Key + ".copy";
        try
        {
            File.Copy(first.Chain, alias);
            File.Copy(first.Key, key);
            Assert.True(QuicHost.SameCertificate(first.Chain, first.Key, alias, key));
            Assert.False(QuicHost.SameCertificate(first.Chain, first.Key, second.Chain, second.Key));
            Assert.False(QuicHost.SameCertificate(first.Chain, second.Key, alias, key));
        }
        finally
        {
            File.Delete(alias);
            File.Delete(key);
        }
    }

    private static int Free()
    {
        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private sealed class Devices : IAwgDevices
    {
        private readonly Dictionary<string, AwgDevice> _held = [];
        public List<AwgUpdate> Updates { get; } = [];
        public void Hold(string name, int port) => _held[name] = new() { Name = name, ListenPort = (ushort)port };
        public void Hold(AwgDevice device) => _held[device.Name] = device;
        public IReadOnlyList<string> Names() => [.. _held.Keys];
        public AwgDevice? Find(string name) => _held.GetValueOrDefault(name);
        public IReadOnlyList<AwgDevice> List() => [.. _held.Values];
        public void Apply(AwgUpdate update)
        {
            Updates.Add(update);
            var held = _held[update.Name];
            _held[update.Name] = held with { ListenPort = update.ListenPort ?? held.ListenPort,
                Peers = held.Peers.Select(one => update.Peers.FirstOrDefault(change => change.PublicKey == one.PublicKey)?.Endpoint is { } endpoint
                    ? one with { Endpoint = endpoint } : one).ToArray() };
        }
    }
}
