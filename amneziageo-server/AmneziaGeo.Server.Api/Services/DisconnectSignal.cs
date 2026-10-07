using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// What a signal to disconnect came to.
/// </summary>
/// <param name="IsTaken">Whether the application of the client took the signal.</param>
/// <param name="Error">The code of the failure, empty when the signal was taken.</param>
/// <param name="Message">What went wrong, in words.</param>
public sealed record SignalOutcome(bool IsTaken, string Error, string Message)
{
    /// <summary>
    /// The outcome of a signal the application took.
    /// </summary>
    public static readonly SignalOutcome Taken = new(true, string.Empty, string.Empty);

    /// <summary>
    /// Returns the outcome of a signal the application did not take.
    /// </summary>
    public static SignalOutcome No(string error, string message) => new(false, error, message);
}

/// <summary>
/// Tells the application of a client to take its tunnel down, over the tunnel itself.
/// </summary>
public sealed class DisconnectSignal
{
    /// <summary>
    /// The TCP port the application takes the signal on, at its address inside the tunnel.
    /// </summary>
    public const int Port = 28561;

    /// <summary>
    /// The word the signal carries.
    /// </summary>
    public const string Word = "disconnect";

    /// <summary>
    /// The client carries no address or key to reach its application with.
    /// </summary>
    public const string NoAddress = "no-address";

    /// <summary>
    /// Nothing answered within the time the signal waits.
    /// </summary>
    public const string NoAnswer = "no-answer";

    /// <summary>
    /// Nothing listens on the port of the signal.
    /// </summary>
    public const string Refused = "refused";

    /// <summary>
    /// The address of the client was not reached.
    /// </summary>
    public const string Unreachable = "unreachable";

    /// <summary>
    /// What answered is not the application holding the keys of the client.
    /// </summary>
    public const string BadAnswer = "bad-answer";

    /// <summary>
    /// How long the signal waits for the connection and then for each line of the application.
    /// </summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    private const int GreetingLimit = 256;

    private const int AnswerLimit = 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Func<IPAddress, int, CancellationToken, ValueTask<Stream>> _dial;

    private readonly TimeSpan _patience;

    /// <summary>
    /// ctor
    /// </summary>
    public DisconnectSignal(Func<IPAddress, int, CancellationToken, ValueTask<Stream>>? dial = null, TimeSpan? patience = null)
    {
        _dial = dial ?? DialAsync;
        _patience = patience ?? Patience;
    }

    /// <summary>
    /// Returns the address the signal reaches a client at: its first IPv4 address ahead of the rest.
    /// </summary>
    public static IPAddress? Target(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var addresses = Hosts(client.Address);

        return addresses.FirstOrDefault(one => one.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
    }

    /// <summary>
    /// Returns the addresses of an endpoint the signal comes from.
    /// </summary>
    public static IReadOnlyList<string> Sources(ServerConfig endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return [.. Hosts(endpoint.Address).Select(one => one.ToString()).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Sends the signal to the application of a client and returns what it came to.
    /// </summary>
    public async Task<SignalOutcome> SendAsync(ServerConfig endpoint, TunnelClient client, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(client);

        if (Target(client) is not { } address || !Curve25519.IsKey(endpoint.PrivateKey) || !Curve25519.IsKey(client.PublicKey))
        {
            return SignalOutcome.No(NoAddress, "the client carries no address or key to reach its application with");
        }

        var place = Place(address);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            limit.CancelAfter(_patience);
            var stream = await _dial(address, Port, limit.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await TalkAsync(stream, PeerToken.Shared(endpoint.PrivateKey, client.PublicKey), place, limit).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return SignalOutcome.No(NoAnswer, $"nothing answered at {place} within {_patience.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return SignalOutcome.No(Refused, $"nothing listens at {place}");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return SignalOutcome.No(Unreachable, $"{place} was not reached: {ex.Message}");
        }
    }

    // Takes the nonce of the application, hands it the sealed signal and reads whether it took it.
    private async Task<SignalOutcome> TalkAsync(Stream stream, byte[] shared, string place, CancellationTokenSource limit)
    {
        limit.CancelAfter(_patience);
        var greeting = Read<Greeting>(await LineAsync(stream, GreetingLimit, limit.Token).ConfigureAwait(false));
        if (greeting?.Nonce is not { } nonce || !PeerToken.IsNonce(nonce))
        {
            return SignalOutcome.No(BadAnswer, $"what listens at {place} is not the application");
        }

        var word = JsonSerializer.SerializeToUtf8Bytes(new Said(Word, false), Json);
        await WriteAsync(stream, PeerToken.Seal(shared, nonce, word, PeerToken.SignalContext), limit.Token).ConfigureAwait(false);

        limit.CancelAfter(_patience);
        var answer = Read<SealedAnswer>(await LineAsync(stream, AnswerLimit, limit.Token).ConfigureAwait(false));
        var opened = answer is { Iv: not null, Data: not null }
            ? PeerToken.Open(shared, nonce, answer, PeerToken.SignalContext)
            : null;

        return Read<Said>(opened) is { Taken: true, Signal: Word }
            ? SignalOutcome.Taken
            : SignalOutcome.No(BadAnswer, $"what listens at {place} does not hold the keys of the client");
    }

    private static async ValueTask<Stream> DialAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }

    // Reads one line, or null when the stream ends or the line outgrows its limit.
    private static async Task<byte[]?> LineAsync(Stream stream, int limit, CancellationToken ct)
    {
        var buffer = new byte[limit];
        var held = 0;
        while (held < limit)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(held), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            var end = Array.IndexOf(buffer, (byte)'\n', held, read);
            if (end >= 0)
            {
                return buffer[..end];
            }

            held += read;
        }

        return null;
    }

    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        byte[] line = [.. JsonSerializer.SerializeToUtf8Bytes(value, Json), (byte)'\n'];
        await stream.WriteAsync(line, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static T? Read<T>(byte[]? line)
        where T : class
    {
        if (line is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(line, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Place(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6
            ? string.Create(CultureInfo.InvariantCulture, $"[{address}]:{Port}")
            : string.Create(CultureInfo.InvariantCulture, $"{address}:{Port}");

    private static List<IPAddress> Hosts(IReadOnlyList<string> ranges) =>
    [
        .. ranges
            .Select(range => IPAddress.TryParse(range.Split('/')[0].Trim(), out var found) ? found : null)
            .OfType<IPAddress>()
    ];

    // The line the application opens the exchange with.
    private sealed record Greeting(string? Nonce);

    // What a sealed line of the exchange says.
    private sealed record Said(string? Signal, bool Taken);
}
