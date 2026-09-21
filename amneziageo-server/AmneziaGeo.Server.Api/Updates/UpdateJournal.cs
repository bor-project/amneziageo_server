using System.Text.Json;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// An update the panel started.
/// </summary>
/// <param name="From">The version the panel ran.</param>
/// <param name="To">The version the panel moves to.</param>
/// <param name="Mode">How the panel was put on the host.</param>
/// <param name="Started">When the update started.</param>
public sealed record UpdateRecord(string From, string To, string Mode, DateTimeOffset Started);

/// <summary>
/// How the update started last went.
/// </summary>
/// <param name="Record">The update.</param>
/// <param name="State">Whether it runs, went through or failed.</param>
/// <param name="Log">The last lines of its log.</param>
public sealed record UpdateRun(UpdateRecord Record, string State, IReadOnlyList<string> Log);

/// <summary>
/// Keeps the record of the update started last, which the tool that puts the release on finishes.
/// </summary>
public sealed class UpdateJournal
{
    /// <summary>
    /// The update runs.
    /// </summary>
    public const string Running = "running";

    /// <summary>
    /// The panel runs the version the update moved to.
    /// </summary>
    public const string Done = "done";

    /// <summary>
    /// The update ended without the panel on the version it moved to.
    /// </summary>
    public const string Failed = "failed";

    private const string PendingFile = "pending.json";

    private const int TailLines = 40;

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// ctor
    /// </summary>
    public UpdateJournal(string folder)
    {
        Folder = folder;
    }

    /// <summary>
    /// The directory the record, the logs and the downloads lie in.
    /// </summary>
    public string Folder { get; }

    /// <summary>
    /// Returns the log of the update to a version.
    /// </summary>
    public string LogOf(string version) => Path.Combine(Folder, version + ".log");

    /// <summary>
    /// Returns the file the exit code of the update to a version is written to.
    /// </summary>
    public string ExitOf(string version) => Path.Combine(Folder, version + ".rc");

    /// <summary>
    /// Writes down an update that starts, dropping what an earlier try at the same version left.
    /// </summary>
    public void Begin(UpdateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(Folder);
        File.Delete(LogOf(record.To));
        File.Delete(ExitOf(record.To));
        File.WriteAllText(Path.Combine(Folder, PendingFile), JsonSerializer.Serialize(record, Json));
    }

    /// <summary>
    /// Adds a line to the log of the update to a version.
    /// </summary>
    public void Note(string version, string line) =>
        File.AppendAllText(LogOf(version), line + "\n");

    /// <summary>
    /// Ends the update to a version as failed, with the reason in its log.
    /// </summary>
    public void Refuse(string version, string reason)
    {
        Note(version, reason);
        File.WriteAllText(ExitOf(version), "1\n");
    }

    /// <summary>
    /// Returns how the update started last went, or null when there was none.
    /// </summary>
    public UpdateRun? Read(Version current, DateTimeOffset now)
    {
        var record = Load();

        return record is null ? null : new UpdateRun(record, StateOf(record, current, now), Tail(LogOf(record.To)));
    }

    private UpdateRecord? Load()
    {
        var path = Path.Combine(Folder, PendingFile);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UpdateRecord>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private string StateOf(UpdateRecord record, Version current, DateTimeOffset now)
    {
        var exit = File.Exists(ExitOf(record.To)) ? File.ReadAllText(ExitOf(record.To)).Trim() : null;
        if (exit is null)
        {
            return now - record.Started < Patience ? Running : Failed;
        }

        return exit == "0" && Version.TryParse(record.To, out var to) && to == current ? Done : Failed;
    }

    private static List<string> Tail(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return [.. File.ReadLines(path).TakeLast(TailLines)];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
