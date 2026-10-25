using System.Text.Json.Serialization;

namespace AmneziaGeo.Server.Updates;

/// <summary>
/// Why an uploaded file is not the file of the release it is named as.
/// </summary>
/// <param name="Name">The name of the file in the release.</param>
/// <param name="Reason">What is wrong with the file, one of the reasons of this record.</param>
/// <param name="Found">What the file turned out to hold, empty when nothing was read.</param>
/// <param name="Wanted">What the file of the release holds.</param>
/// <param name="Message">What is wrong with the file, in words.</param>
public sealed record FileFault(string Name, string Reason, string Found, string Wanted, [property: JsonIgnore] string Message)
{
    /// <summary>
    /// The file is not of the format of its name.
    /// </summary>
    public const string Format = "format";

    /// <summary>
    /// The file is built for another architecture.
    /// </summary>
    public const string Arch = "arch";

    /// <summary>
    /// The package is another package.
    /// </summary>
    public const string Package = "package";

    /// <summary>
    /// The package carries no control file.
    /// </summary>
    public const string Control = "control";

    /// <summary>
    /// The package carries no native libraries.
    /// </summary>
    public const string Libraries = "libraries";

    /// <summary>
    /// The file does not read.
    /// </summary>
    public const string Unreadable = "unreadable";

    /// <summary>
    /// The file is of a kind that is not looked into.
    /// </summary>
    public const string Unchecked = "unchecked";
}
