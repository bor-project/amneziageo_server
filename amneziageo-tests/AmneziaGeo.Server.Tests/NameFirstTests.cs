using System.Net;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Routing.Names;

namespace AmneziaGeo.Server.Tests;

public class NameFirstTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);

    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(10);

    private static readonly byte[] Request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: late.example\r\n\r\n");

    [Fact]
    public async Task WhatAClientStartedToSayIsReadThoughItsRestComesLaterThanTheFirstWait()
    {
        using var link = await Link.MakeAsync();
        var reading = NameFirst.SaidAsync(link.Taken, new byte[NameDefaults.MostSaid], Quiet, Whole, CancellationToken.None);

        await link.Client.SendAsync(Request.AsMemory(0, 18));
        await Task.Delay(Quiet * 4);
        await link.Client.SendAsync(Request.AsMemory(18));
        var said = await reading;

        Assert.Equal((Request.Length, "late.example", false, false), (said.Held, said.Name, said.Ended, said.IsSilent));
    }

    [Fact]
    public async Task AClientThatSaysNothingIsSilentAndOneThatLeftIsNot()
    {
        using var link = await Link.MakeAsync();

        var silent = await NameFirst.SaidAsync(link.Taken, new byte[NameDefaults.MostSaid], Quiet, Whole, CancellationToken.None);
        link.Client.Shutdown(SocketShutdown.Send);
        var gone = await NameFirst.SaidAsync(link.Taken, new byte[NameDefaults.MostSaid], Quiet, Whole, CancellationToken.None);

        Assert.Equal((0, string.Empty, false, true), (silent.Held, silent.Name, silent.Ended, silent.IsSilent));
        Assert.Equal((0, string.Empty, true, false), (gone.Held, gone.Name, gone.Ended, gone.IsSilent));
    }

    [Fact]
    public async Task TheFarEndThatSpeaksFirstIsHeardAndTheClientKeepsItsWord()
    {
        using var link = await Link.MakeAsync();
        using var far = await Link.MakeAsync();
        var waiting = NameFirst.SpokeAsync(link.Taken, far.Client, Task.FromResult(string.Empty), Whole, CancellationToken.None);

        await Task.Delay(Quiet);
        await far.Taken.SendAsync(Encoding.ASCII.GetBytes("220 ready\r\n").AsMemory());
        var spoke = await waiting;
        await link.Client.SendAsync(Request.AsMemory());
        var said = await NameFirst.SaidAsync(link.Taken, new byte[NameDefaults.MostSaid], Whole, Whole, CancellationToken.None);
        var banner = new byte[64];
        var read = await far.Client.ReceiveAsync(banner.AsMemory());

        Assert.Equal(NameSpoke.Far, spoke);
        Assert.Equal((Request.Length, "late.example"), (said.Held, said.Name));
        Assert.Equal("220 ready\r\n", Encoding.ASCII.GetString(banner, 0, read));
    }

    [Fact]
    public async Task AFarEndThatLeavesWithoutAWordDidNotSpeak()
    {
        using var link = await Link.MakeAsync();
        using var far = await Link.MakeAsync();
        using var cut = await Link.MakeAsync();

        var ending = NameFirst.SpokeAsync(link.Taken, far.Client, Task.FromResult(string.Empty), Whole, CancellationToken.None);
        far.Taken.Shutdown(SocketShutdown.Send);
        var ended = await ending;
        var cutting = NameFirst.SpokeAsync(link.Taken, cut.Client, Task.FromResult(string.Empty), Whole, CancellationToken.None);
        cut.Taken.Close(0);
        var reset = await cutting;

        Assert.Equal((NameSpoke.Nobody, NameSpoke.Nobody), (ended, reset));
    }

    [Fact]
    public async Task AClientThatSpeaksBeforeTheFarEndIsTheFirst()
    {
        using var link = await Link.MakeAsync();
        using var far = await Link.MakeAsync();
        var dialing = new TaskCompletionSource<string>();
        var reached = NameFirst.SpokeAsync(link.Taken, far.Client, Task.FromResult(string.Empty), Whole, CancellationToken.None);

        await link.Client.SendAsync(Request.AsMemory());
        var first = await reached;
        var second = await NameFirst.SpokeAsync(link.Taken, far.Client, dialing.Task, Whole, CancellationToken.None);
        var said = await NameFirst.SaidAsync(link.Taken, new byte[NameDefaults.MostSaid], Quiet, Whole, CancellationToken.None);

        Assert.Equal((NameSpoke.Client, NameSpoke.Client), (first, second));
        Assert.Equal((Request.Length, "late.example"), (said.Held, said.Name));
    }

    [Fact]
    public async Task WithNoFarEndNobodySpeaksOnceTheWaitRunsOut()
    {
        using var link = await Link.MakeAsync();
        using var far = await Link.MakeAsync();

        var none = await NameFirst.SpokeAsync(link.Taken, null, null, Quiet, CancellationToken.None);
        var failed = await NameFirst.SpokeAsync(link.Taken, far.Client, Task.FromResult("unreachable"), Quiet, CancellationToken.None);
        link.Client.Shutdown(SocketShutdown.Send);
        var left = await NameFirst.SpokeAsync(link.Taken, null, null, Whole, CancellationToken.None);

        Assert.Equal((NameSpoke.Nobody, NameSpoke.Nobody, NameSpoke.Client), (none, failed, left));
    }

    private sealed class Link : IDisposable
    {
        private Link(Socket client, Socket taken)
        {
            Client = client;
            Taken = taken;
        }

        public Socket Client { get; }

        public Socket Taken { get; }

        public static async Task<Link> MakeAsync()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await client.ConnectAsync(listener.LocalEndPoint!);
            var taken = await listener.AcceptAsync();

            return new Link(client, taken);
        }

        public void Dispose()
        {
            Client.Dispose();
            Taken.Dispose();
        }
    }
}
