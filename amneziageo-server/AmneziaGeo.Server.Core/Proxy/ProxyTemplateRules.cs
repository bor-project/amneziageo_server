namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// Shapes the settings of a proxy template have to take.
/// </summary>
public static class ProxyTemplateRules
{
    /// <summary>
    /// The longest name a template takes.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Returns why the settings of a template are unusable, or null when they hold.
    /// </summary>
    public static ProxyFault? Check(ProxyTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return CheckName(template.Name) ?? ProxyRules.Check(template.Fresh(ProxyDefaults.FirstName));
    }

    /// <summary>
    /// Returns why the name of a template is unusable, or null when it holds.
    /// </summary>
    public static ProxyFault? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ProxyFault("bad-template-name", "the name is empty");
        }

        return name.Length > MaxNameLength
            ? new ProxyFault("bad-template-name", $"the name is longer than {MaxNameLength} characters")
            : null;
    }
}
