using AmneziaGeo.Server.Api.Auth;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// What the panel knows of its releases and of the update started last.
/// </summary>
/// <param name="Current">The version of the panel that runs.</param>
/// <param name="Channel">The channel releases are taken from.</param>
/// <param name="Mode">How the panel was put on the host.</param>
/// <param name="Blocker">Why the panel cannot put a release on by itself, empty when it can.</param>
/// <param name="Checked">When the releases were looked over last.</param>
/// <param name="Latest">The release newer than the panel, null when there is none.</param>
/// <param name="Stage">What the panel does with its releases now.</param>
/// <param name="Fault">Why the last look at the releases failed.</param>
/// <param name="Run">The update started last.</param>
public sealed record UpdateResponse(
    string Current,
    string Channel,
    string Mode,
    string Blocker,
    DateTimeOffset? Checked,
    UpdateLatestResponse? Latest,
    string Stage,
    Failure? Fault,
    UpdateRunResponse? Run);

/// <summary>
/// A release newer than the panel.
/// </summary>
/// <param name="Version">The version of the release.</param>
/// <param name="Published">When the release was built.</param>
/// <param name="Notes">The page of the release.</param>
public sealed record UpdateLatestResponse(string Version, DateTimeOffset? Published, string Notes);

/// <summary>
/// The update started last.
/// </summary>
/// <param name="From">The version the panel ran.</param>
/// <param name="To">The version the panel moves to.</param>
/// <param name="State">Whether it runs, went through or failed.</param>
/// <param name="Started">When it started.</param>
/// <param name="Log">The last lines of its log.</param>
public sealed record UpdateRunResponse(string From, string To, string State, DateTimeOffset Started, IReadOnlyList<string> Log);

/// <summary>
/// The release the panel is told to move to.
/// </summary>
/// <param name="Version">The version of the release.</param>
public sealed record UpdateApplyRequest(string? Version);
