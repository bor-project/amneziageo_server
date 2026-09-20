namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Shapes the settings of an endpoint template have to take.
/// </summary>
public static class InterfaceTemplateRules
{
    /// <summary>
    /// The longest name a template takes.
    /// </summary>
    public const int MaxNameLength = 64;

    private const string Probe = "awg0";

    /// <summary>
    /// Returns why the settings of a template are unusable, or null when they hold.
    /// </summary>
    public static ConfigFault? Check(InterfaceTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return CheckName(template.Name) ?? ConfigRules.Check(template.Fresh(Probe));
    }

    /// <summary>
    /// Returns why the name of a template is unusable, or null when it holds.
    /// </summary>
    public static ConfigFault? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ConfigFault("bad-template-name", "the name is empty");
        }

        return name.Length > MaxNameLength
            ? new ConfigFault("bad-template-name", $"the name is longer than {MaxNameLength} characters")
            : null;
    }
}
