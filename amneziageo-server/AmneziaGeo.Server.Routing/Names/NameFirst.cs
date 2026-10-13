using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// What a client said first over a connection the panel took.
/// </summary>
/// <param name="Held">How many bytes of it were read.</param>
/// <param name="Name">The name read out of them, empty when there is none.</param>
/// <param name="Ended">Whether the client ended the connection.</param>
public readonly record struct NameWord(int Held, string Name, bool Ended)
{
    /// <summary>
    /// Tells whether the client said nothing so far and is still there.
    /// </summary>
    public bool IsSilent => Held == 0 && !Ended;
}

/// <summary>
/// Who spoke first over a connection the panel took.
/// </summary>
public enum NameSpoke
{
    /// <summary>
    /// The client spoke or ended the connection.
    /// </summary>
    Client = 0,

    /// <summary>
    /// The far end spoke.
    /// </summary>
    Far = 1,

    /// <summary>
    /// Nobody spoke: there is no far end to wait for, or it ended the connection without a word.
    /// </summary>
    Nobody = 2,
}

/// <summary>
/// Waits for the first word over a connection the panel took.
/// </summary>
public static class NameFirst
{
    /// <summary>
    /// Reads what a client says first: until its name is read, it is clear there is none, the client ends, the wait
    /// for the first byte runs out or the wait for the rest does.
    /// </summary>
    public static async Task<NameWord> SaidAsync(Socket client, byte[] buffer, TimeSpan quiet, TimeSpan whole, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(buffer);

        var most = Math.Min(buffer.Length, NameDefaults.MostSaid);
        var held = 0;
        using var start = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var rest = CancellationTokenSource.CreateLinkedTokenSource(ct);
        start.CancelAfter(quiet);
        try
        {
            while (held < most)
            {
                var wait = held == 0 ? start.Token : rest.Token;
                var read = await client.ReceiveAsync(buffer.AsMemory(held, most - held), SocketFlags.None, wait).ConfigureAwait(false);
                if (read == 0)
                {
                    return new NameWord(held, string.Empty, true);
                }

                if (held == 0)
                {
                    rest.CancelAfter(whole);
                }

                held += read;
                var seen = StreamHello.Read(buffer.AsSpan(0, held));
                if (seen.Reading != NameReading.More)
                {
                    return new NameWord(held, seen.Name, false);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }

        return new NameWord(held, string.Empty, false);
    }

    /// <summary>
    /// Tells who speaks first over a connection whose client is silent: the client, the far end once it is reached,
    /// or nobody when the far end ends the connection without a word, or there is none and the wait runs out.
    /// </summary>
    public static async Task<NameSpoke> SpokeAsync(Socket client, Socket? far, Task<string>? dial, TimeSpan wait, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var word = ReadyAsync(client, stop.Token);
        var heard = Task.CompletedTask;
        var end = Task.Delay(wait, stop.Token);
        try
        {
            if (far is not null && dial is not null
                && await Task.WhenAny(word, dial).ConfigureAwait(false) == dial
                && (await dial.ConfigureAwait(false)).Length == 0)
            {
                heard = ReadyAsync(far, stop.Token);
                if (await Task.WhenAny(word, heard).ConfigureAwait(false) == word)
                {
                    return NameSpoke.Client;
                }

                return heard.IsCompletedSuccessfully && far.Available > 0 ? NameSpoke.Far : NameSpoke.Nobody;
            }

            return await Task.WhenAny(word, end).ConfigureAwait(false) == word ? NameSpoke.Client : NameSpoke.Nobody;
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(QuietAsync(word), QuietAsync(heard), QuietAsync(end)).ConfigureAwait(false);
        }
    }

    // Ends once a socket has bytes to read or its far side ended, taking none of them.
    private static async Task ReadyAsync(Socket socket, CancellationToken ct) =>
        await socket.ReceiveAsync(Memory<byte>.Empty, SocketFlags.None, ct).ConfigureAwait(false);

    // Waits for a wait that was called off.
    private static async Task QuietAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }
}
