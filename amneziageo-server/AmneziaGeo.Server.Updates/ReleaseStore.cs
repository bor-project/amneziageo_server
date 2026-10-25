using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AmneziaGeo.Server.Updates;

/// <summary>
/// A file a release holds.
/// </summary>
/// <param name="Name">The name of the file in the release.</param>
/// <param name="Size">The size of the file, in bytes.</param>
/// <param name="Sha256">The digest of the file.</param>
/// <param name="From">The version a patch starts from, empty for an installer or a patch that names none.</param>
public sealed record ReleaseFileInfo(string Name, long Size, string Sha256, string From);

/// <summary>
/// A release as the list of releases names it.
/// </summary>
/// <param name="Tag">The folder of the release.</param>
/// <param name="Version">The version of the client in the release.</param>
/// <param name="Channel">The channel the release goes out on.</param>
/// <param name="Description">What the release says about itself.</param>
/// <param name="Created">When the release was made.</param>
/// <param name="Updated">When the release last changed.</param>
/// <param name="Files">How many files the release holds.</param>
/// <param name="Bytes">The size of the files of the release together.</param>
public sealed record ReleaseSummary(
    string Tag,
    string Version,
    string Channel,
    string Description,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    int Files,
    long Bytes);

/// <summary>
/// A release with its files.
/// </summary>
/// <param name="Release">The release.</param>
/// <param name="Files">The files the release holds, in the order of the release.</param>
/// <param name="Missing">The files a whole release carries and this one does not hold yet.</param>
public sealed record ReleaseDetails(ReleaseSummary Release, IReadOnlyList<ReleaseFileInfo> Files, IReadOnlyList<string> Missing);

/// <summary>
/// One page of the releases, the newest first.
/// </summary>
/// <param name="Items">The releases of the page.</param>
/// <param name="Page">The number of the page, from 1.</param>
/// <param name="Size">How many releases a page holds.</param>
/// <param name="Total">How many releases there are.</param>
public sealed record ReleasePage(IReadOnlyList<ReleaseSummary> Items, int Page, int Size, int Total);

/// <summary>
/// What the store of the releases says about itself.
/// </summary>
/// <param name="Directory">The directory the releases are kept in.</param>
/// <param name="Writable">Whether the directory reads and writes.</param>
/// <param name="FreeBytes">The room left on the disk of the directory.</param>
/// <param name="TotalBytes">The size of the disk of the directory.</param>
/// <param name="Releases">How many releases the store holds.</param>
/// <param name="Bytes">The size of the files of every release together.</param>
/// <param name="Fault">Why the directory does not read or write, empty when it does.</param>
public sealed record StoreStatus(string Directory, bool Writable, long FreeBytes, long TotalBytes, int Releases, long Bytes, string Fault);

/// <summary>
/// Keeps the releases of the client as folders of files, each with the manifest a client reads beside them.
/// </summary>
public sealed class ReleaseStore
{
    /// <summary>
    /// The folder under the directory the releases are kept in.
    /// </summary>
    public const string Folder = "releases";

    /// <summary>
    /// The file a release keeps its record in, beside the manifest.
    /// </summary>
    public const string RecordName = "release.json";

    /// <summary>
    /// The largest file a release takes, in bytes.
    /// </summary>
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// The longest description a release takes.
    /// </summary>
    public const int MaxDescription = 256;

    /// <summary>
    /// How many releases a page holds at most.
    /// </summary>
    public const int MaxPage = 100;

    private const int Chunk = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;

    private readonly TimeProvider _time;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// ctor
    /// </summary>
    public ReleaseStore(string directory, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory = Path.GetFullPath(directory);
        _root = Path.Combine(Directory, Folder);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// The directory the releases are kept in.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Returns what the store says about itself, probing that its directory reads and writes.
    /// </summary>
    public StoreStatus Status()
    {
        var fault = Probe();
        var releases = Tags().Select(tag => Load(tag)).ToList();
        var free = 0L;
        var total = 0L;
        try
        {
            var drive = new DriveInfo(Directory);
            free = drive.AvailableFreeSpace;
            total = drive.TotalSize;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The disk of the directory is not named: the room stays unknown.
        }

        return new StoreStatus(
            Directory,
            fault.Length == 0,
            free,
            total,
            releases.Count,
            releases.Sum(release => release.Record.Files.Sum(file => file.Size)),
            fault);
    }

    /// <summary>
    /// Returns one page of the releases, the newest first.
    /// </summary>
    public ReleasePage List(int page, int size)
    {
        var taken = Math.Clamp(size, 1, MaxPage);
        var number = Math.Max(page, 1);
        var tags = Tags().OrderDescending().ToList();
        var items = tags.Skip((number - 1) * taken).Take(taken).Select(tag => Summary(Load(tag))).ToList();

        return new ReleasePage(items, number, taken, tags.Count);
    }

    /// <summary>
    /// Returns a release with its files.
    /// </summary>
    public ReleaseDetails Read(string tag) => Details(Load(Tag(tag)));

    /// <summary>
    /// Returns the newest release that reaches the readers of a channel and holds a file, null when there is none.
    /// </summary>
    public ReleaseSummary? Latest(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var tag = Tags()
            .Where(tag => ReleaseChannel.Reaches(tag.Channel, channel))
            .OrderDescending()
            .Select(tag => Load(tag))
            .FirstOrDefault(release => release.Record.Files.Count > 0);

        return tag is null ? null : Summary(tag);
    }

    /// <summary>
    /// Returns the path of a file of the newest release of a channel, null when the channel or the file is not there.
    /// </summary>
    public string? Resolve(string channel, string name) =>
        ReleaseChannel.Known(channel) && Latest(channel) is { } latest ? ResolveIn(latest.Tag, name) : null;

    /// <summary>
    /// Returns the path of a file of a release, null when the release or the file is not there.
    /// </summary>
    public string? ResolveIn(string tag, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!ReleaseTag.TryParse(tag, out var parsed) || !System.IO.Directory.Exists(Home(parsed)))
        {
            return null;
        }

        if (name == ReleaseManifest.FileName)
        {
            var manifest = Path.Combine(Home(parsed), ReleaseManifest.FileName);

            return File.Exists(manifest) ? manifest : null;
        }

        if (ReleaseSlots.Find(parsed.Version, name) is null)
        {
            return null;
        }

        var release = Load(parsed);
        var path = Path.Combine(Home(parsed), name);

        return release.Record.Files.Any(file => file.Name == name) && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Makes a release: a stable one under its version alone, a beta or a test build under the next number of its
    /// channel.
    /// </summary>
    public async Task<ReleaseDetails> CreateAsync(string? version, string? channel, string? description, CancellationToken ct)
    {
        if (!ReleaseTag.TryVersion(version, out var parsed))
        {
            throw new ReleaseRefused("bad-version", "the version is not four numbers like 1.10.9.0");
        }

        if (!ReleaseChannel.Known(channel))
        {
            throw new ReleaseRefused("bad-channel", $"the channel is one of {string.Join(", ", ReleaseChannel.All)}");
        }

        var said = Described(description);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Ready();
            var tag = Next(parsed, channel!);
            System.IO.Directory.CreateDirectory(Home(tag));
            var now = _time.GetUtcNow();
            var release = new Release(tag, new Record(said, now, now, []));
            Save(release);

            return Details(release);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Takes a file into a release, checks that it is the file its name says, and writes the manifest anew.
    /// </summary>
    public async Task<ReleaseDetails> PutFileAsync(string tag, string name, Stream body, string? from, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var parsed = Tag(tag);
        var slot = Slot(parsed, name);
        var starts = From(slot, from);
        var home = Home(Load(parsed).Tag);
        var part = await TakeAsync(home, body, ct).ConfigureAwait(false);
        try
        {
            if (FileInspector.Check(slot, part.Path) is { } fault)
            {
                throw new ReleaseRefused("bad-file", $"{slot.Name}: {fault.Message}") { File = fault };
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var release = Load(parsed);
                File.Move(part.Path, Path.Combine(home, slot.Name), overwrite: true);
                release.Record.Files.RemoveAll(file => file.Name == slot.Name);
                release.Record.Files.Add(new ReleaseFileInfo(slot.Name, part.Size, part.Sha256, starts));
                Save(Touched(release));

                return Details(release);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            Drop(part.Path);
        }
    }

    /// <summary>
    /// Takes the files of a release out of a zip archive, the manifest in it checked against them when there is one.
    /// </summary>
    public async Task<ReleaseDetails> ImportAsync(string tag, Stream zip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zip);

        var parsed = Tag(tag);
        var home = Home(Load(parsed).Tag);
        var archive = await TakeAsync(home, zip, ct).ConfigureAwait(false);
        var parts = new List<(ReleaseSlot Slot, Taken Part)>();
        try
        {
            var manifest = default(ReleaseManifest);
            using (var opened = Open(archive.Path))
            {
                var lead = Lead(opened);
                foreach (var entry in opened.Entries.Where(entry => entry.Name.Length > 0))
                {
                    var name = entry.FullName[lead..];
                    if (name == ReleaseManifest.FileName)
                    {
                        manifest = await ManifestAsync(entry, parsed, ct).ConfigureAwait(false);

                        continue;
                    }

                    var slot = ReleaseSlots.Find(parsed.Version, name)
                        ?? throw new ReleaseRefused("bad-archive", $"the archive carries {name}, which is not a file of release {parsed}");
                    using var source = entry.Open();
                    var part = await TakeAsync(home, source, ct).ConfigureAwait(false);
                    parts.Add((slot, part));
                    if (FileInspector.Check(slot, part.Path) is { } fault)
                    {
                        throw new ReleaseRefused("bad-file", $"{slot.Name}: {fault.Message}") { File = fault };
                    }
                }
            }

            if (parts.Count == 0)
            {
                throw new ReleaseRefused("bad-archive", $"the archive carries no file of release {parsed}");
            }

            Agree(manifest, parts);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var release = Load(parsed);
                foreach (var (slot, part) in parts)
                {
                    File.Move(part.Path, Path.Combine(home, slot.Name), overwrite: true);
                    release.Record.Files.RemoveAll(file => file.Name == slot.Name);
                    release.Record.Files.Add(new ReleaseFileInfo(slot.Name, part.Size, part.Sha256, Starts(manifest, slot)));
                }

                if (release.Record.Description.Length == 0 && manifest is { Description.Length: > 0 })
                {
                    release.Record.Description = Described(manifest.Description);
                }

                Save(Touched(release));

                return Details(release);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            Drop(archive.Path);
            foreach (var (_, part) in parts)
            {
                Drop(part.Path);
            }
        }
    }

    /// <summary>
    /// Changes what a release says about itself: its channel, under which its folder is renamed, its description
    /// and the versions its patches start from.
    /// </summary>
    public async Task<ReleaseDetails> ChangeAsync(
        string tag,
        string? channel,
        string? description,
        IReadOnlyDictionary<string, string>? from,
        CancellationToken ct)
    {
        var parsed = Tag(tag);
        if (channel is not null && !ReleaseChannel.Known(channel))
        {
            throw new ReleaseRefused("bad-channel", $"the channel is one of {string.Join(", ", ReleaseChannel.All)}");
        }

        var said = description is null ? null : Described(description);
        var starts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, version) in from ?? new Dictionary<string, string>())
        {
            var slot = Slot(parsed, name);
            if (!slot.IsPatch)
            {
                throw new ReleaseRefused("bad-file-name", $"{name} is not a patch, it starts from no version");
            }

            starts[slot.Name] = From(slot, version);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var release = Load(parsed);
            if (said is not null)
            {
                release.Record.Description = said;
            }

            foreach (var (name, version) in starts)
            {
                var index = release.Record.Files.FindIndex(file => file.Name == name);
                if (index < 0)
                {
                    throw new ReleaseRefused("file-missing", $"the release holds no {name}", 404);
                }

                release.Record.Files[index] = release.Record.Files[index] with { From = version };
            }

            if (channel is not null && channel != release.Tag.Channel)
            {
                var moved = Next(release.Tag.Version, channel);
                System.IO.Directory.Move(Home(release.Tag), Home(moved));
                release = new Release(moved, release.Record);
            }

            Save(Touched(release));

            return Details(release);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Takes a file out of a release and writes the manifest anew.
    /// </summary>
    public async Task<ReleaseDetails> DropFileAsync(string tag, string name, CancellationToken ct)
    {
        var parsed = Tag(tag);
        var slot = Slot(parsed, name);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var release = Load(parsed);
            if (release.Record.Files.RemoveAll(file => file.Name == slot.Name) == 0)
            {
                throw new ReleaseRefused("file-missing", $"the release holds no {slot.Name}", 404);
            }

            Drop(Path.Combine(Home(release.Tag), slot.Name));
            Save(Touched(release));

            return Details(release);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Removes a release with its files.
    /// </summary>
    public async Task DeleteAsync(string tag, CancellationToken ct)
    {
        var parsed = Tag(tag);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var release = Load(parsed);
            System.IO.Directory.Delete(Home(release.Tag), recursive: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Lists the tags of the folders of the releases, folders named otherwise left out.
    private IEnumerable<ReleaseTag> Tags()
    {
        if (!System.IO.Directory.Exists(_root))
        {
            return [];
        }

        var tags = new List<ReleaseTag>();
        foreach (var folder in System.IO.Directory.EnumerateDirectories(_root))
        {
            if (ReleaseTag.TryParse(Path.GetFileName(folder), out var tag))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    // Reads the record of a release, scanning its folder for one made by hand.
    private Release Load(ReleaseTag tag)
    {
        var home = Home(tag);
        if (!System.IO.Directory.Exists(home))
        {
            throw ReleaseRefused.Missing(tag.ToString());
        }

        var path = Path.Combine(home, RecordName);
        if (File.Exists(path))
        {
            try
            {
                var record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path), Json);
                if (record is not null)
                {
                    return new Release(tag, record);
                }
            }
            catch (JsonException)
            {
                // The record is written anew from the files.
            }
        }

        var scanned = Scan(tag, home);
        Save(new Release(tag, scanned));

        return new Release(tag, scanned);
    }

    // Reads the files of a folder as the record of its release.
    private Record Scan(ReleaseTag tag, string home)
    {
        var now = _time.GetUtcNow();
        var manifest = Path.Combine(home, ReleaseManifest.FileName);
        var read = File.Exists(manifest) ? ReleaseManifest.Parse(File.ReadAllText(manifest)) : null;
        var files = new List<ReleaseFileInfo>();
        foreach (var slot in ReleaseSlots.Of(tag.Version))
        {
            var path = Path.Combine(home, slot.Name);
            if (!File.Exists(path))
            {
                continue;
            }

            var from = read?.Installers.Select(one => one.Patch).FirstOrDefault(patch => patch?.Name == slot.Name)?.From ?? string.Empty;
            files.Add(new ReleaseFileInfo(slot.Name, new FileInfo(path).Length, Digest(path), from));
        }

        return new Record(read?.Description ?? string.Empty, now, now, files);
    }

    // Writes the record and the manifest of a release, each whole or not at all.
    private void Save(Release release)
    {
        var home = Home(release.Tag);
        Write(Path.Combine(home, RecordName), JsonSerializer.Serialize(release.Record, Json) + "\n");
        Write(Path.Combine(home, ReleaseManifest.FileName), ReleaseManifest.Build(release.Tag, release.Record.Description, release.Record.Files).Serialize());
    }

    private static void Write(string path, string text)
    {
        var next = path + ".next";
        File.WriteAllText(next, text);
        File.Move(next, path, overwrite: true);
    }

    private Release Touched(Release release)
    {
        release.Record.Updated = _time.GetUtcNow();
        release.Record.Files.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

        return release;
    }

    // Takes a body into a hidden file of the folder of the release, counting its digest and size on the way.
    private static async Task<Taken> TakeAsync(string home, Stream body, CancellationToken ct)
    {
        var path = Path.Combine(home, "." + Guid.NewGuid().ToString("N") + ".part");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var size = 0L;
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, Chunk, useAsync: true))
            {
                var buffer = new byte[Chunk];
                int read;
                while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    size += read;
                    if (size > MaxFileBytes)
                    {
                        throw new ReleaseRefused("file-too-large", $"a file of a release is at most {MaxFileBytes / (1024 * 1024)} megabytes", 413);
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }

            if (size == 0)
            {
                throw new ReleaseRefused("bad-file", "the file is empty");
            }

            return new Taken(path, size, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch
        {
            Drop(path);
            throw;
        }
    }

    private static ZipArchive Open(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new ReleaseRefused("bad-archive", "the archive does not read as zip: " + ex.Message);
        }
    }

    // Returns the length of the one folder every entry of an archive lies under, 0 when the files lie at its root.
    private static int Lead(ZipArchive archive)
    {
        var names = archive.Entries.Where(entry => entry.Name.Length > 0).Select(entry => entry.FullName).ToList();
        if (names.Count == 0)
        {
            return 0;
        }

        var first = names[0].IndexOf('/', StringComparison.Ordinal);
        if (first <= 0)
        {
            return 0;
        }

        var folder = names[0][..(first + 1)];

        return names.All(name => name.StartsWith(folder, StringComparison.Ordinal) && name.Length > folder.Length
            && !name[folder.Length..].Contains('/', StringComparison.Ordinal))
            ? folder.Length
            : 0;
    }

    private static async Task<ReleaseManifest> ManifestAsync(ZipArchiveEntry entry, ReleaseTag tag, CancellationToken ct)
    {
        if (entry.Length > ReleaseManifest.MaxSize)
        {
            throw new ReleaseRefused("bad-manifest", "the manifest in the archive is larger than it can be");
        }

        using var source = entry.Open();
        using var reader = new StreamReader(source);
        var manifest = ReleaseManifest.Parse(await reader.ReadToEndAsync(ct).ConfigureAwait(false))
            ?? throw new ReleaseRefused("bad-manifest", "the manifest in the archive does not read");
        if (manifest.Version != tag.Version.ToString())
        {
            throw new ReleaseRefused("bad-manifest", $"the manifest in the archive names version {manifest.Version}, the release is {tag.Version}");
        }

        return manifest;
    }

    // Checks that the files of an archive are the ones its manifest names, where it names them.
    private static void Agree(ReleaseManifest? manifest, List<(ReleaseSlot Slot, Taken Part)> parts)
    {
        if (manifest is null)
        {
            return;
        }

        var taken = parts.ToDictionary(item => item.Slot.Name, item => item.Part, StringComparer.Ordinal);
        foreach (var installer in manifest.Installers)
        {
            Agree(installer.Name, installer.Size, installer.Sha256, taken);
            if (installer.Patch is { } patch)
            {
                Agree(patch.Name, patch.Size, patch.Sha256, taken);
            }
        }
    }

    private static void Agree(string name, long size, string sha256, Dictionary<string, Taken> taken)
    {
        if (!taken.TryGetValue(name, out var part))
        {
            throw new ReleaseRefused("bad-archive", $"the manifest names {name}, which the archive does not carry");
        }

        if (part.Size != size || !string.Equals(part.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseRefused("bad-archive", $"{name} is not the file the manifest names");
        }
    }

    private static string Starts(ReleaseManifest? manifest, ReleaseSlot slot) =>
        slot.IsPatch
            ? manifest?.Installers.Select(one => one.Patch).FirstOrDefault(patch => patch?.Name == slot.Name)?.From ?? string.Empty
            : string.Empty;

    // Returns the tag a new release of a version takes on a channel.
    private ReleaseTag Next(Version version, string channel)
    {
        var held = Tags().Where(tag => tag.Version == version && tag.Channel == channel).ToList();
        if (channel == ReleaseChannel.Stable)
        {
            return held.Count == 0
                ? new ReleaseTag(version, channel, 0)
                : throw new ReleaseRefused("release-exists", $"there is a stable release {version} already", 409);
        }

        return new ReleaseTag(version, channel, held.Count == 0 ? 1 : held.Max(tag => tag.Number) + 1);
    }

    private static ReleaseTag Tag(string? text) =>
        ReleaseTag.TryParse(text, out var tag) ? tag : throw ReleaseRefused.Missing(text ?? string.Empty);

    private static ReleaseSlot Slot(ReleaseTag tag, string? name) =>
        ReleaseSlots.Find(tag.Version, name)
        ?? throw new ReleaseRefused("bad-file-name", $"{name} is not a file of release {tag}");

    private static string From(ReleaseSlot slot, string? from)
    {
        if (!slot.IsPatch || string.IsNullOrWhiteSpace(from))
        {
            return string.Empty;
        }

        return ReleaseTag.TryVersion(from, out var version)
            ? version.ToString()
            : throw new ReleaseRefused("bad-version", "the version a patch starts from is four numbers like 1.10.4.0");
    }

    private static string Described(string? description)
    {
        var said = (description ?? string.Empty).Trim();
        if (said.Length > MaxDescription || said.Contains('\n', StringComparison.Ordinal) || said.Contains('\r', StringComparison.Ordinal))
        {
            throw new ReleaseRefused("bad-description", $"the description is one line of at most {MaxDescription} characters");
        }

        return said;
    }

    private void Ready()
    {
        var fault = Probe();
        if (fault.Length > 0)
        {
            throw new ReleaseRefused("directory-unwritable", fault, 503);
        }
    }

    // Makes the folder of the releases and tries a file in it.
    private string Probe()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_root);
            var probe = Path.Combine(_root, ".probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, [1]);
            File.Delete(probe);

            return string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the directory {Directory} does not take files: {ex.Message}";
        }
    }

    private static string Digest(string path)
    {
        using var file = File.OpenRead(path);

        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    private static void Drop(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that would not go stays hidden in the folder.
        }
    }

    private string Home(ReleaseTag tag) => Path.Combine(_root, tag.ToString());

    private static ReleaseSummary Summary(Release release) => new(
        release.Tag.ToString(),
        release.Tag.Version.ToString(),
        release.Tag.Channel,
        release.Record.Description,
        release.Record.Created,
        release.Record.Updated,
        release.Record.Files.Count,
        release.Record.Files.Sum(file => file.Size));

    private static ReleaseDetails Details(Release release)
    {
        var held = release.Record.Files.Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
        var missing = ReleaseSlots.Of(release.Tag.Version).Select(slot => slot.Name).Where(name => !held.Contains(name)).ToList();

        return new ReleaseDetails(Summary(release), release.Record.Files.ToList(), missing);
    }

    private sealed record Taken(string Path, long Size, string Sha256);

    private sealed record Release(ReleaseTag Tag, Record Record);

    private sealed class Record
    {
        public Record(string description, DateTimeOffset created, DateTimeOffset updated, List<ReleaseFileInfo> files)
        {
            Description = description;
            Created = created;
            Updated = updated;
            Files = files;
        }

        public string Description { get; set; }

        public DateTimeOffset Created { get; set; }

        public DateTimeOffset Updated { get; set; }

        public List<ReleaseFileInfo> Files { get; set; }
    }
}
