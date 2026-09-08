namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// What the resolver is doing right now.
/// </summary>
public sealed class DnsState
{
    private long _questions;

    private long _cached;

    private long _failed;

    private string[] _listening = [];

    private string? _fault;

    /// <summary>
    /// Whether the resolver takes questions.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// When the resolver started taking questions.
    /// </summary>
    public DateTimeOffset? StartedUtc { get; private set; }

    /// <summary>
    /// The addresses the resolver takes questions on.
    /// </summary>
    public IReadOnlyList<string> Listening => Volatile.Read(ref _listening);

    /// <summary>
    /// Why the resolver is not running, or null.
    /// </summary>
    public string? Fault => Volatile.Read(ref _fault);

    /// <summary>
    /// How many questions the clients asked.
    /// </summary>
    public long Questions => Interlocked.Read(ref _questions);

    /// <summary>
    /// How many questions were answered out of what is held back.
    /// </summary>
    public long Cached => Interlocked.Read(ref _cached);

    /// <summary>
    /// How many questions no name server answered.
    /// </summary>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>
    /// Marks the resolver as running on the addresses it took.
    /// </summary>
    public void Started(IReadOnlyList<string> listening, DateTimeOffset at)
    {
        Volatile.Write(ref _listening, [.. listening]);
        Volatile.Write(ref _fault, null);
        StartedUtc = at;
        IsRunning = true;
    }

    /// <summary>
    /// Marks the resolver as not taking questions any more.
    /// </summary>
    public void Stopped(string? fault = null)
    {
        Volatile.Write(ref _listening, []);
        Volatile.Write(ref _fault, fault);
        StartedUtc = null;
        IsRunning = false;
    }

    /// <summary>
    /// Counts a question the resolver took.
    /// </summary>
    public void Asked() => Interlocked.Increment(ref _questions);

    /// <summary>
    /// Counts a question answered out of what is held back.
    /// </summary>
    public void Held() => Interlocked.Increment(ref _cached);

    /// <summary>
    /// Counts a question no name server answered.
    /// </summary>
    public void Lost() => Interlocked.Increment(ref _failed);
}
