using System.Net;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public class FrontCarryTests
{
    private const int Rounds = 300;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWebSocketIsCarriedBothWaysWhicheverThreadTheWebServerGoesOnWith(bool inline)
    {
        using var front = new Echo();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        Listening.Inline(builder.WebHost, new WebOptions { Inline = inline });
        await using var app = builder.Build();
        app.Run(async context =>
        {
            if (await FrontRelay.PassAsync(context, front.Port, CancellationToken.None) is { } refused)
            {
                context.Response.StatusCode = refused.Status;
            }
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, new Uri(address).Port);
        var stream = client.GetStream();
        await stream.WriteAsync("GET /tunnel HTTP/1.1\r\nHost: localhost\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n"u8.ToArray());
        var head = await Echo.HeadAsync(stream).WaitAsync(Patience);
        var sent = new byte[Size(Rounds)];
        var back = new byte[Size(Rounds)];
        for (var round = 0; round < Rounds; round++)
        {
            var size = Size(round);
            Array.Fill(sent, (byte)round);
            await stream.WriteAsync(sent.AsMemory(0, size));
            await stream.ReadExactlyAsync(back.AsMemory(0, size)).AsTask().WaitAsync(Patience);
            Assert.Equal(sent[..size], back[..size]);
        }

        client.Close();
        var carried = await front.Carried.WaitAsync(Patience);
        await app.StopAsync();

        Assert.StartsWith("HTTP/1.1 101", head, StringComparison.Ordinal);
        Assert.Contains("GET /tunnel HTTP/1.1\r\n", await front.Head, StringComparison.Ordinal);
        Assert.Equal(Enumerable.Range(0, Rounds).Sum(round => (long)Size(round)), carried);
    }

    private static int Size(int round) => 1 + (round * 4);

    // Stands in for the front of an endpoint: takes one upgrade, says it switches and sends back what comes after.
    private sealed class Echo : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        private readonly TaskCompletionSource<string> _head = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// ctor
        /// </summary>
        public Echo()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Carried = ServeAsync();
        }

        public int Port { get; }

        public Task<string> Head => _head.Task;

        public Task<long> Carried { get; }

        public static async Task<string> HeadAsync(Stream stream)
        {
            var head = new List<byte>();
            var one = new byte[1];
            while (head.Count < 4 || head[^4] != '\r' || head[^3] != '\n' || head[^2] != '\r' || head[^1] != '\n')
            {
                if (await stream.ReadAsync(one) == 0)
                {
                    break;
                }

                head.Add(one[0]);
            }

            return Encoding.ASCII.GetString([.. head]);
        }

        public void Dispose() => _listener.Dispose();

        private async Task<long> ServeAsync()
        {
            using var peer = await _listener.AcceptTcpClientAsync();
            var stream = peer.GetStream();
            _head.TrySetResult(await HeadAsync(stream));
            await stream.WriteAsync("HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n"u8.ToArray());
            var buffer = new byte[4096];
            var carried = 0L;
            try
            {
                while (await stream.ReadAsync(buffer) is var read and > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read));
                    carried += read;
                }
            }
            catch (IOException)
            {
            }

            return carried;
        }
    }
}
