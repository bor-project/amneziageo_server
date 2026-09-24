using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Services;
using Xunit;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// A connection of the services runs BBR when the kernel lets it, and keeps what it had otherwise.
/// </summary>
public sealed class TcpCongestionTests
{
    [Fact]
    public void Bbr_IsTakenOrTheSocketKeepsItsOwn()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect((IPEndPoint)listener.LocalEndpoint);
        using var served = listener.AcceptSocket();
        var before = TcpCongestion.Of(served);

        var took = TcpCongestion.TryBbr(served);

        Assert.NotEmpty(before);
        Assert.Equal(took ? TcpCongestion.Bbr : before, TcpCongestion.Of(served));
    }

    [Fact]
    public void AConnectionWithoutASocket_IsLeftAlone()
    {
        Assert.False(TcpCongestion.TryBbr(new Microsoft.AspNetCore.Connections.DefaultConnectionContext()));
    }
}
