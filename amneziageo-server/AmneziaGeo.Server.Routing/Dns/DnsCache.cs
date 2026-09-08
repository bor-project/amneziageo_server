using System.Collections.Concurrent;
using System.Globalization;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Holds back the answers the resolver already has.
/// </summary>
public sealed class DnsCache
{
    private readonly ConcurrentDictionary<string, Held> _held = new(StringComparer.Ordinal);

    private readonly TimeProvider _time;

    private readonly int _size;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsCache(int size, TimeProvider? time = null)
    {
        _size = size;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// How many answers are held back.
    /// </summary>
    public int Count => _held.Count;

    /// <summary>
    /// Returns the answer to a question marked with the number the asking side used, or null.
    /// </summary>
    public byte[]? Take(string question, DnsRecordType type, ushort id)
    {
        if (_size == 0 || !_held.TryGetValue(Key(question, type), out var found))
        {
            return null;
        }

        if (found.Until <= _time.GetUtcNow())
        {
            _held.TryRemove(Key(question, type), out _);

            return null;
        }

        var answer = found.Answer.ToArray();
        DnsMessage.Stamp(answer, id);

        return answer;
    }

    /// <summary>
    /// Holds an answer back for as long as it stays good.
    /// </summary>
    public void Keep(string question, DnsRecordType type, ReadOnlySpan<byte> answer, TimeSpan lifetime)
    {
        if (_size == 0 || lifetime <= TimeSpan.Zero || question.Length == 0)
        {
            return;
        }

        if (_held.Count >= _size)
        {
            Sweep();
        }

        _held[Key(question, type)] = new Held(answer.ToArray(), _time.GetUtcNow() + lifetime);
    }

    /// <summary>
    /// Drops every answer held back.
    /// </summary>
    public void Clear() => _held.Clear();

    private void Sweep()
    {
        var now = _time.GetUtcNow();
        foreach (var pair in _held)
        {
            if (pair.Value.Until <= now)
            {
                _held.TryRemove(pair.Key, out _);
            }
        }

        foreach (var pair in _held)
        {
            if (_held.Count < _size)
            {
                break;
            }

            _held.TryRemove(pair.Key, out _);
        }
    }

    private static string Key(string question, DnsRecordType type) =>
        ((int)type).ToString(CultureInfo.InvariantCulture) + "|" + question.ToLowerInvariant();

    private readonly record struct Held(byte[] Answer, DateTimeOffset Until);
}
