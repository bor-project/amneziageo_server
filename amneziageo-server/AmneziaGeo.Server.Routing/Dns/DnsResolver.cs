using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Answers one question of a client and feeds the sets of the rules.
/// </summary>
public sealed class DnsResolver
{
    private readonly IDnsUpstream _upstream;

    private readonly DnsCache _cache;

    private readonly DnsSets _sets;

    private readonly Func<RoutePlan?> _plan;

    private readonly DnsSettings _settings;

    private readonly DnsState _state;

    private readonly Func<CancellationToken, Task> _land;

    private readonly Lock _sync = new();

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
        Func<CancellationToken, Task> land)
    {
        _upstream = upstream;
        _cache = cache;
        _sets = sets;
        _plan = plan;
        _settings = settings;
        _state = state;
        _land = land;
    }

    /// <summary>
    /// Returns the answer to send back, or null when the question is not one.
    /// </summary>
    public async Task<byte[]?> AnswerAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
    {
        var asked = DnsMessage.Read(question.Span);
        if (asked is null || asked.IsResponse)
        {
            return null;
        }

        _state.Asked();
        var held = _cache.Take(asked.Question, asked.Type, asked.Id);
        if (held is not null)
        {
            _state.Held();
            await LandAsync(DnsMessage.Read(held), ct).ConfigureAwait(false);

            return held;
        }

        var answer = await _upstream.AskAsync(question, stream, ct).ConfigureAwait(false);
        if (answer is null)
        {
            _state.Lost();

            return DnsMessage.Refusal(question.Span, DnsMessage.ServerFailure);
        }

        var read = DnsMessage.Read(answer);
        if (read is { Code: 0 })
        {
            _cache.Keep(read.Question, read.Type, answer, Lifetime(read));
            await LandAsync(read, ct).ConfigureAwait(false);
        }

        return answer;
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

    private async Task LandAsync(DnsMessage? answer, CancellationToken ct)
    {
        if (!Spread(answer))
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
