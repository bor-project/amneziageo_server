namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Stores the default client template.
/// </summary>
public sealed class DefaultTemplateEntity
{
    public long Id { get; set; }

    public string Payload { get; set; } = string.Empty;
}
