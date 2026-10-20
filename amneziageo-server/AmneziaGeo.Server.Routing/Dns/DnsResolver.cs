using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Answers one question of a client and feeds the sets of the rules.
/// </summary>
public sealed class DnsResolver
{
    private const int QuietLimit = 4096;

    private readonly IDnsUpstream _upstream;

    private readonly DnsCache _cache;

    private readonly DnsSets _sets;

    private readonly Func<RoutePlan?> _plan;

    private readonly DnsSettings _settings;

    private readonly DnsState _state;

    private readonly Func<CancellationToken, Task> _land;

    private readonly Action<IPAddress?, DnsMessage>? _heard;

    private readonly DnsStandIns? _standIns;

    private readonly Func<IPAddress, DomainMatcher?>? _standing;

    private readonly IDnsRelay? _relay;

    private readonly Lock _sync = new();

    private readonly Dictionary<IPAddress, long> _quiet = [];

    private RoutePlan? _seen;

    private DnsNames _names = DnsNames.Build(null);

    /// <summary>
    /// ctor
    /// </summary>
    public DnsResolver(
        IDnsUpstream upstream,
        DnsCache cache,
        DnsSets sets,
        Func<RoutePlan?> plan,
        DnsSettings settings,
        DnsState state,
        Func<CancellationToken, Task> land,
        Action<IPAddress?, DnsMessage>? heard = null,
        DnsStandIns? standIns = null,
        Func<IPAddress, DomainMatcher?>? standing = null,
        IDnsRelay? relay = null)
    {
        _relay = relay;
        _upstream = upstream;
        _cache = cache;
        _sets = sets;
        _plan = plan;
        _settings = settings;
        _state = state;
        _land = land;
        _heard = heard;
        _standIns = standIns;
        _standing = standing;
    }

    /// <summary>
    /// Returns the answer to send back, or null when the question is not one.
    /// </summary>
    public Task<byte[]?> AnswerAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct) =>
        AnswerAsync(question, stream, null, ct);

    /// <summary>
    /// Returns the answer to send back to a client, or null when the question is not one.
    /// </summary>
    public Task<byte[]?> AnswerAsync(ReadOnlyMemory<byte> question, bool stream, IPAddress? client, CancellationToken ct) =>
        AnswerAsync(question, stream, client, null, ct);

    /// <summary>
    /// Returns the answer to send back to a client, the one of the private name server it asked when it asked one, or
    /// null when the question is not one.
    /// </summary>
    public async Task<byte[]?> AnswerAsync(
        ReadOnlyMemory<byte> question,
        bool stream,
        IPAddress? client,
        IPAddress? server,
        CancellationToken ct)
    {
        var asked = DnsMessage.Read(question.Span);
        if (asked is null || asked.IsResponse)
        {
            return null;
        }

        _state.Asked();
        if (server is not null && _relay is { } relay && !Quiet(server))
        {
            var relayed = await RelayAsync(relay, question, stream, client, server, asked, ct).ConfigureAwait(false);
            if (relayed is not null)
            {
                return relayed;
            }
        }

        var held = _cache.Take(asked.Question, asked.Type, asked.Id);
        if (held is not null)
        {
            _state.Held();

            return await TellAsync(question, stream, client, asked, held, DnsMessage.Read(held), ct).ConfigureAwait(false);
        }

        var answer = await _upstream.AskAsync(question, stream, ct).ConfigureAwait(false);
        if (answer is null)
        {
            _state.Lost();

            return DnsMessage.Refusal(question.Span, DnsMessage.ServerFailure);
        }

        var read = DnsMessage.Read(answer);
        if (read is not { Code: 0 })
        {
            return Carried(question, stream, answer);
        }

        _cache.Keep(read.Question, read.Type, answer, Lifetime(read));

        return await TellAsync(question, stream, client, asked, answer, read, ct).ConfigureAwait(false);
    }

    // Returns the answer of the private name server the client asked, kept out of the cache and handed on as an answer
    // of the upstreams is, or null when it gave none.
    private async Task<byte[]?> RelayAsync(
        IDnsRelay relay,
        ReadOnlyMemory<byte> question,
        bool stream,
        IPAddress? client,
        IPAddress server,
        DnsMessage asked,
        CancellationToken ct)
    {
        var answer = await relay.AskAsync(server, question, stream, ct).ConfigureAwait(false);
        if (answer is null)
        {
            Hush(server);

            return null;
        }

        var read = DnsMessage.Read(answer);

        return read is { Code: 0 }
            ? await TellAsync(question, stream, client, asked, answer, read, ct).ConfigureAwait(false)
            : Carried(question, stream, answer);
    }

    // Tells whether a private name server that gave no answer is passed over still.
    private bool Quiet(IPAddress server)
    {
        var now = Environment.TickCount64;
        lock (_sync)
        {
            if (!_quiet.TryGetValue(server, out var until))
            {
                return false;
            }

            if (now < until)
            {
                return true;
            }

            _quiet.Remove(server);

            return false;
        }
    }

    // Passes over a private name server that gave no answer for a while.
    private void Hush(IPAddress server)
    {
        var now = Environment.TickCount64;
        lock (_sync)
        {
            if (_quiet.Count >= QuietLimit)
            {
                foreach (var gone in _quiet.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                {
                    _quiet.Remove(gone);
                }
            }

            if (_quiet.Count < QuietLimit)
            {
                _quiet[server] = now + (DnsDefaults.QuietSeconds * 1000L);
            }
        }
    }

    // Returns the answer as the asking side takes it: whole over a stream, within the size of a datagram otherwise.
    private static byte[] Carried(ReadOnlyMemory<byte> question, bool stream, byte[] answer) =>
        stream ? answer : DnsMessage.Fit(question.Span, answer);

    // Hands the answer to the journal and the sets and returns it the way the client is answered: with the stand-in
    // address where its template takes stand-ins for the name.
    private async Task<byte[]> TellAsync(
        ReadOnlyMemory<byte> question,
        bool stream,
        IPAddress? client,
        DnsMessage asked,
        byte[] answer,
        DnsMessage? read,
        CancellationToken ct)
    {
        Heard(client, read);
        var stood = Stood(question.Span, client, asked, read, out var waiting);
        await LandAsync(read, waiting, ct).ConfigureAwait(false);

        return stood ?? Carried(question, stream, answer);
    }

    // Returns the answer with the stand-in address of the name, an empty one for the other kinds of addresses, or
    // null when the client is answered as it is.
    private byte[]? Stood(ReadOnlySpan<byte> question, IPAddress? client, DnsMessage asked, DnsMessage? read, out bool waiting)
    {
        waiting = false;
        if (_standIns is null
            || client is null
            || read is not { Code: 0 }
            || asked.Type is not (DnsRecordType.A or DnsRecordType.Aaaa or DnsRecordType.Https)
            || _standing?.Invoke(client) is not { } names
            || !Named(names, read))
        {
            return null;
        }

        if (asked.Type != DnsRecordType.A)
        {
            return DnsMessage.Empty(question);
        }

        var real = read.Addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork).ToArray();
        if (real.Length == 0)
        {
            return null;
        }

        var address = _standIns.Take(asked.Question, real, out waiting);

        return DnsMessage.Single(question, address, StandInLife(read));
    }

    private static bool Named(DomainMatcher names, DnsMessage answer) =>
        names.Matches(answer.Question) || answer.Answers.Any(record => names.Matches(record.Name));

    private uint StandInLife(DnsMessage answer) =>
        Math.Min(Math.Clamp(answer.Lifetime, (uint)_settings.MinTtl, (uint)_settings.MaxTtl), DnsDefaults.StandInSeconds);

    private void Heard(IPAddress? client, DnsMessage? answer)
    {
        if (answer is not null && _heard is not null)
        {
            _heard(client, answer);
        }
    }

    private TimeSpan Lifetime(DnsMessage answer)
    {
        if (answer.Answers.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var seconds = Math.Clamp(answer.Lifetime, (uint)_settings.MinTtl, (uint)_settings.MaxTtl);

        return TimeSpan.FromSeconds(seconds);
    }

    private async Task LandAsync(DnsMessage? answer, bool waiting, CancellationToken ct)
    {
        if (!Spread(answer) && !waiting)
        {
            return;
        }

        try
        {
            await _land(ct).WaitAsync(DnsDefaults.Landing, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
    }

    private bool Spread(DnsMessage? answer)
    {
        if (answer is null || answer.Answers.Count == 0)
        {
            return false;
        }

        var names = Names();
        if (names.Count == 0)
        {
            return false;
        }

        var addresses = answer.Addresses.ToArray();
        if (addresses.Length == 0)
        {
            return false;
        }

        var queued = false;
        var rules = Rules(names, answer);
        foreach (var rule in rules)
        {
            foreach (var address in addresses)
            {
                queued |= _sets.Add(rule, address, _settings.NameLifetime);
            }
        }

        return queued;
    }

    private static IReadOnlyList<long> Rules(DnsNames names, DnsMessage answer)
    {
        var asked = new List<string> { answer.Question };
        asked.AddRange(answer.Answers.Select(record => record.Name));

        return [.. asked.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(names.Match).Distinct()];
    }

    private DnsNames Names()
    {
        var plan = _plan();
        lock (_sync)
        {
            if (!ReferenceEquals(plan, _seen))
            {
                _names = DnsNames.Build(plan);
                _seen = plan;
            }

            return _names;
        }
    }
}
