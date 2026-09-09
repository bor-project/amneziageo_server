using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Probe;

/// <summary>
/// Asks a name server through one way out of the host.
/// </summary>
public interface IProbeLink
{
    /// <summary>
    /// Returns how the question sent under a mark ended.
    /// </summary>
    Task<ProbeOutcome> ReachAsync(IPEndPoint server, uint mark, string name, TimeSpan wait, CancellationToken ct);
}

/// <summary>
/// Asks a name server over UDP, marking the packet so it leaves through the outbound.
/// </summary>
public sealed class ProbeLink : IProbeLink
{
    private const int SocketLevel = 1;

    private const int MarkOption = 36;

    private const int MaxAnswer = 1500;

    /// <inheritdoc/>
    public async Task<ProbeOutcome> ReachAsync(
        IPEndPoint server, uint mark, string name, TimeSpan wait, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (!Mark(socket, mark))
        {
            return ProbeOutcome.Skipped;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(wait);
        var id = ProbeQuestion.Id();
        try
        {
            await socket.SendToAsync(ProbeQuestion.Packet(name, id), server, deadline.Token).ConfigureAwait(false);

            return await AnswerAsync(socket, server, id, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProbeOutcome.Missed;
        }
        catch (SocketException)
        {
            return ProbeOutcome.Missed;
        }
    }

    private static async Task<ProbeOutcome> AnswerAsync(
        Socket socket, IPEndPoint server, ushort id, CancellationToken ct)
    {
        var answer = new byte[MaxAnswer];
        while (!ct.IsCancellationRequested)
        {
            var read = await socket.ReceiveFromAsync(answer, server, ct).ConfigureAwait(false);
            if (ProbeQuestion.Answers(answer.AsSpan(0, read.ReceivedBytes), id))
            {
                return ProbeOutcome.Reached;
            }
        }

        return ProbeOutcome.Missed;
    }

    private static bool Mark(Socket socket, uint mark)
    {
        if (mark == 0 || !OperatingSystem.IsLinux())
        {
            return true;
        }

        try
        {
            socket.SetRawSocketOption(SocketLevel, MarkOption, BitConverter.GetBytes(mark));

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
