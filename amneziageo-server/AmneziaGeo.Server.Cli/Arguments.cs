namespace AmneziaGeo.Server.Cli;

/// <summary>
/// The words of a command line split into positions, flags and options.
/// </summary>
public sealed class Arguments
{
    private readonly List<string> _positional = [];

    private readonly Dictionary<string, string?> _named = new(StringComparer.Ordinal);

    /// <summary>
    /// ctor
    /// </summary>
    public Arguments(IReadOnlyList<string> words)
    {
        for (var step = 0; step < words.Count; step++)
        {
            var word = words[step];
            if (!word.StartsWith("--", StringComparison.Ordinal))
            {
                _positional.Add(word);

                continue;
            }

            var name = word[2..];
            var next = step + 1 < words.Count ? words[step + 1] : null;
            if (next is not null && !next.StartsWith("--", StringComparison.Ordinal))
            {
                _named[name] = next;
                step++;

                continue;
            }

            _named[name] = null;
        }
    }

    /// <summary>
    /// The words that are not flags or options.
    /// </summary>
    public IReadOnlyList<string> Positional => _positional;

    /// <summary>
    /// Returns a positional word, or null when the line is shorter.
    /// </summary>
    public string? At(int index) => index < _positional.Count ? _positional[index] : null;

    /// <summary>
    /// Tells whether a flag was given.
    /// </summary>
    public bool Has(string name) => _named.ContainsKey(name);

    /// <summary>
    /// Returns the value of an option, or null when it was not given.
    /// </summary>
    public string? Value(string name) => _named.TryGetValue(name, out var found) ? found : null;
}
