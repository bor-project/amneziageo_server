using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// What putting a package together from pieces took.
/// </summary>
/// <param name="Files">How many files the package holds.</param>
/// <param name="Fetched">How many of them came from the pack.</param>
/// <param name="Bytes">How many bytes of the pack were downloaded.</param>
public sealed record PieceCount(int Files, int Fetched, long Bytes);

/// <summary>
/// Puts the package of a release together from the files the host holds and the files it lacks, fetched from the
/// pack of the release in parts.
/// </summary>
public sealed class PackagePieces
{
    /// <summary>
    /// The share of the package past which the whole package is downloaded instead.
    /// </summary>
    public const double MostShare = 0.75;

    /// <summary>
    /// The most parts of the pack an update asks for.
    /// </summary>
    public const int MostParts = 64;

    /// <summary>
    /// The largest stretch of the pack between two files the host lacks that is downloaded along with them.
    /// </summary>
    public const long Gap = 256 * 1024;

    private const UnixFileMode Folder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;

    /// <summary>
    /// ctor
    /// </summary>
    public PackagePieces(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Puts the package into a folder from the files found under the folders held and the pack under the address of
    /// the files of the release, checking every file against the list of the package.
    /// </summary>
    public async Task<PieceCount> StageAsync(
        Uri files,
        UpdatePackage package,
        IReadOnlyList<string> held,
        string target,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(held);

        var list = package.Files ?? throw new InvalidDataException("the release lists no files of its package");
        var pack = package.Pack ?? throw new InvalidDataException("the release carries no pack");
        var text = await ReadListAsync(new Uri(files, list.Name), list, ct).ConfigureAwait(false);
        var entries = PackageList.Parse(text, pack.Size);
        var found = Holdings(held, entries);
        var parts = Parts(entries.Where(entry => !found.ContainsKey(entry.Sha256)));
        var bytes = parts.Sum(part => part.Length);
        if (bytes > package.Size * MostShare)
        {
            throw new InvalidDataException("most of the package changed");
        }

        if (parts.Count > MostParts)
        {
            throw new InvalidDataException($"the changed files lie in {parts.Count} parts of the pack");
        }

        foreach (var entry in entries)
        {
            if (found.TryGetValue(entry.Sha256, out var source))
            {
                await CopyAsync(source, entry, Place(target, entry.Path), ct).ConfigureAwait(false);
            }
        }

        var address = new Uri(files, pack.Name);
        foreach (var part in parts)
        {
            await FetchAsync(address, part, target, ct).ConfigureAwait(false);
        }

        return new PieceCount(entries.Count, parts.Sum(part => part.Entries.Count), bytes);
    }

    private static Dictionary<string, string> Holdings(IReadOnlyList<string> held, IReadOnlyList<PackageEntry> entries)
    {
        var sizes = entries.Select(entry => entry.Size).ToHashSet();
        var wanted = entries.Select(entry => entry.Sha256).ToHashSet(StringComparer.Ordinal);
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var folder in held.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                if (!sizes.Contains(new FileInfo(path).Length))
                {
                    continue;
                }

                var sha = Digest(path);
                if (wanted.Contains(sha))
                {
                    found.TryAdd(sha, path);
                }
            }
        }

        return found;
    }

    private static string Digest(string path)
    {
        using var file = File.OpenRead(path);

        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    private static List<Part> Parts(IEnumerable<PackageEntry> lacking)
    {
        var parts = new List<Part>();
        foreach (var entry in lacking.OrderBy(entry => entry.Offset))
        {
            if (parts.Count > 0 && entry.Offset - parts[^1].End <= Gap)
            {
                parts[^1].Add(entry);
            }
            else
            {
                parts.Add(new Part(entry));
            }
        }

        return parts;
    }

    private async Task<byte[]> ReadListAsync(Uri address, UpdateFile list, CancellationToken ct)
    {
        if (list.Size > PackageList.MaxSize)
        {
            throw new InvalidDataException($"{list.Name} is larger than {PackageList.MaxSize} bytes");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(StallLimit);
        using var response = await _http
            .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, limit.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{address} answered {(int)response.StatusCode}");
        }

        if (response.Content.Headers.ContentLength is { } length && length != list.Size)
        {
            throw new InvalidDataException($"{list.Name} is not the size the manifest names");
        }

        using var source = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
        var text = new byte[list.Size];
        await FillAsync(source, text, limit).ConfigureAwait(false);
        if (await source.ReadAsync(new byte[1], limit.Token).ConfigureAwait(false) > 0)
        {
            throw new InvalidDataException($"{list.Name} is larger than the manifest says");
        }

        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(text)), list.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{list.Name} does not match the digest the manifest names");
        }

        return text;
    }

    private async Task FetchAsync(Uri address, Part part, string target, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(StallLimit);
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Range = new RangeHeaderValue(part.Start, part.End - 1);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new InvalidDataException($"{address} answered {(int)response.StatusCode} to a request for a part");
        }

        var range = response.Content.Headers.ContentRange;
        if (range is null || range.From != part.Start || range.To != part.End - 1)
        {
            throw new InvalidDataException($"{address} answered with another part than the one asked for");
        }

        using var source = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
        var at = part.Start;
        foreach (var entry in part.Entries)
        {
            await SkipAsync(source, entry.Offset - at, limit).ConfigureAwait(false);
            var packed = new byte[entry.Length];
            await FillAsync(source, packed, limit).ConfigureAwait(false);
            at = entry.Offset + entry.Length;
            await UnpackAsync(packed, entry, Place(target, entry.Path), ct).ConfigureAwait(false);
        }
    }

    private static async Task SkipAsync(Stream source, long count, CancellationTokenSource limit)
    {
        var chunk = new byte[81920];
        var left = count;
        while (left > 0)
        {
            var read = await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(left, chunk.Length)), limit.Token)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("the part of the pack ended early");
            }

            limit.CancelAfter(StallLimit);
            left -= read;
        }
    }

    private static async Task FillAsync(Stream source, byte[] buffer, CancellationTokenSource limit)
    {
        var done = 0;
        while (done < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(done), limit.Token).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("the download ended early");
            }

            limit.CancelAfter(StallLimit);
            done += read;
        }
    }

    private static async Task UnpackAsync(byte[] packed, PackageEntry entry, string path, CancellationToken ct)
    {
        MakeFolder(Path.GetDirectoryName(path)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var gzip = new GZipStream(new MemoryStream(packed, writable: false), CompressionMode.Decompress))
        using (var file = File.Create(path))
        {
            var chunk = new byte[81920];
            var total = 0L;
            var read = 0;
            while ((read = await gzip.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > entry.Size)
                {
                    throw new InvalidDataException($"{entry.Path} unpacks larger than the list says");
                }

                hash.AppendData(chunk, 0, read);
                await file.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }

        Seal(path, entry, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static async Task CopyAsync(string source, PackageEntry entry, string path, CancellationToken ct)
    {
        MakeFolder(Path.GetDirectoryName(path)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var from = File.OpenRead(source))
        using (var file = File.Create(path))
        {
            var chunk = new byte[81920];
            var read = 0;
            while ((read = await from.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(chunk, 0, read);
                await file.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }

        Seal(path, entry, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static void Seal(string path, PackageEntry entry, string digest)
    {
        if (!string.Equals(digest, entry.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{entry.Path} does not match the digest the list names");
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, entry.Mode);
        }
    }

    private static void MakeFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        if (Directory.Exists(path))
        {
            return;
        }

        if (Path.GetDirectoryName(path) is { Length: > 0 } parent)
        {
            MakeFolder(parent);
        }

        Directory.CreateDirectory(path, Folder);
    }

    private static string Place(string target, string path)
    {
        var root = Path.GetFullPath(target);
        var place = Path.GetFullPath(Path.Combine(root, path));
        if (!place.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path} lies outside the package");
        }

        return place;
    }

    private sealed class Part
    {
        private readonly List<PackageEntry> _entries = [];

        /// <summary>
        /// ctor
        /// </summary>
        public Part(PackageEntry first)
        {
            Start = first.Offset;
            Add(first);
        }

        public long Start { get; }

        public long End { get; private set; }

        public long Length => End - Start;

        public IReadOnlyList<PackageEntry> Entries => _entries;

        public void Add(PackageEntry entry)
        {
            _entries.Add(entry);
            End = entry.Offset + entry.Length;
        }
    }
}
