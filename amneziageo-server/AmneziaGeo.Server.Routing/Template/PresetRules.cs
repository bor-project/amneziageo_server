using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Routing.Template;

/// <summary>
/// Shapes a routing preset has to take, and the rules a client reads it as.
/// </summary>
public static class PresetRules
{
    /// <summary>
    /// The longest name a preset takes.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// How long the identifier of a preset is.
    /// </summary>
    public const int UidLength = 36;

    /// <summary>
    /// The word a rule that goes through the tunnel starts with.
    /// </summary>
    public const string Proxy = "proxy";

    /// <summary>
    /// The word a rule that goes past the tunnel starts with.
    /// </summary>
    public const string Direct = "direct";

    /// <summary>
    /// The word a rule the client blocks starts with.
    /// </summary>
    public const string Block = "block";

    /// <summary>
    /// Returns why a preset is unusable, or null when it holds.
    /// </summary>
    public static ClientFault? Check(RoutingPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return CheckName(preset.Name)
            ?? TemplateList.Check(preset.Proxy)
            ?? TemplateList.Check(preset.Direct)
            ?? TemplateList.Check(preset.Block);
    }

    /// <summary>
    /// Returns why the name of a preset is unusable, or null when it holds.
    /// </summary>
    public static ClientFault? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ClientFault("bad-preset-name", "the name is empty");
        }

        return name.Length > MaxNameLength
            ? new ClientFault("bad-preset-name", $"the name is longer than {MaxNameLength} characters")
            : null;
    }

    /// <summary>
    /// Returns an identifier no other preset carries.
    /// </summary>
    public static string FreshUid() => Guid.NewGuid().ToString("D");

    /// <summary>
    /// Tells whether two presets hand out the same list.
    /// </summary>
    public static bool SameList(RoutingPreset one, RoutingPreset other)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(one.Name, other.Name, StringComparison.Ordinal)
            && one.AllUdp == other.AllUdp
            && one.Full == other.Full
            && Rules(one).SequenceEqual(Rules(other), StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the rules a client routes the preset by, each led by what it does with the traffic.
    /// </summary>
    public static IReadOnlyList<string> Rules(RoutingPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return
        [
            .. preset.Proxy.Select(entry => $"{Proxy}|{entry}"),
            .. preset.Direct.Select(entry => $"{Direct}|{entry}"),
            .. preset.Block.Select(entry => $"{Block}|{entry}"),
        ];
    }
}
