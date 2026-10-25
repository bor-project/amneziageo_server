namespace AmneziaGeo.Server.Updates;

/// <summary>
/// What a new release is made with.
/// </summary>
/// <param name="Version">The version of the client in the release, in four numbers.</param>
/// <param name="Channel">The channel the release goes out on.</param>
/// <param name="Description">What the release says about itself.</param>
public sealed record ReleaseDraft(string? Version, string? Channel, string? Description);

/// <summary>
/// What changes about a release.
/// </summary>
/// <param name="Channel">The channel the release moves to, null to keep it.</param>
/// <param name="Description">What the release says about itself, null to keep it.</param>
/// <param name="From">The version each named patch starts from.</param>
public sealed record ReleaseChange(string? Channel, string? Description, Dictionary<string, string>? From);

/// <summary>
/// Why the releases refused a request.
/// </summary>
/// <param name="Error">The code of the refusal.</param>
/// <param name="Message">What went wrong, in words.</param>
/// <param name="File">Why the file of the refusal is not the file of the release, null for a refusal of something else.</param>
public sealed record UpdateFailure(string Error, string Message, FileFault? File = null);
