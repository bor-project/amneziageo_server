using System.Collections.Concurrent;
using System.Security.Cryptography;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Api.Hello;

/// <summary>
/// A pass a client measures its speed with.
/// </summary>
/// <param name="Value">The pass itself.</param>
/// <param name="ClientId">The client the pass was minted for.</param>
/// <param name="Expires">When the pass stops answering.</param>
public sealed record SpeedTicket(string Value, long ClientId, DateTimeOffset Expires);

/// <summary>
/// Holds the challenges the server handed out and the passes it minted for the answered ones.
/// </summary>
public sealed class SpeedTickets
{
    /// <summary>
    /// How long a challenge waits for its answer.
    /// </summary>
    public static readonly TimeSpan ChallengeLife = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a pass measures with.
    /// </summary>
    public static readonly TimeSpan TicketLife = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The most bytes one leg of a measurement carries.
    /// </summary>
    public const long MaxBytes = 100L * 1024 * 1024;

    /// <summary>
    /// The bytes a leg carries when the caller names none.
    /// </summary>
    public const long DefaultBytes = 25_000_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _challenges = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, SpeedTicket> _tickets = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public SpeedTickets(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns a fresh challenge and remembers it.
    /// </summary>
    public string Challenge()
    {
        var now = _time.GetUtcNow();
        Sweep(now);
        var challenge = PeerProof.Challenge();
        _challenges[challenge] = now + ChallengeLife;

        return challenge;
    }

    /// <summary>
    /// Takes a challenge once, telling whether the server handed it out and it still stands.
    /// </summary>
    public bool Takes(string? challenge) =>
        challenge is { Length: > 0 }
        && _challenges.TryRemove(challenge, out var until)
        && until > _time.GetUtcNow();

    /// <summary>
    /// Mints a pass for a client, dropping the one it held before.
    /// </summary>
    public SpeedTicket Mint(long clientId)
    {
        var now = _time.GetUtcNow();
        Sweep(now);
        foreach (var held in _tickets.Where(pair => pair.Value.ClientId == clientId))
        {
            _tickets.TryRemove(held.Key, out _);
        }

        var ticket = new SpeedTicket(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('='),
            clientId,
            now + TicketLife);
        _tickets[ticket.Value] = ticket;

        return ticket;
    }

    /// <summary>
    /// Returns the pass behind a value, or null when there is none that still stands.
    /// </summary>
    public SpeedTicket? Find(string? value)
    {
        if (value is not { Length: > 0 } || !_tickets.TryGetValue(value, out var ticket))
        {
            return null;
        }

        if (ticket.Expires > _time.GetUtcNow())
        {
            return ticket;
        }

        _tickets.TryRemove(value, out _);

        return null;
    }

    /// <summary>
    /// Takes the one measurement a pass is allowed to run at a time.
    /// </summary>
    public bool Enter(string value) => _running.TryAdd(value, 0);

    /// <summary>
    /// Gives the measurement of a pass back.
    /// </summary>
    public void Leave(string value) => _running.TryRemove(value, out _);

    private void Sweep(DateTimeOffset now)
    {
        foreach (var pair in _challenges.Where(one => one.Value <= now))
        {
            _challenges.TryRemove(pair.Key, out _);
        }

        foreach (var pair in _tickets.Where(one => one.Value.Expires <= now))
        {
            _tickets.TryRemove(pair.Key, out _);
        }
    }
}
