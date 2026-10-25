using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace AmneziaGeo.Server.Updates;

/// <summary>
/// Looks inside an uploaded file to see that it is the file of the release it is named as.
/// </summary>
public static class FileInspector
{
    private const string PatchHead = "# amneziageo patch 1";

    private const string PatchList = "list";

    private static readonly byte[] ArchiveMagic = "!<arch>\n"u8.ToArray();

    private static readonly IReadOnlyDictionary<string, string[]> Abis = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["universal"] = ["arm64-v8a", "armeabi-v7a", "x86_64"],
        ["arm64"] = ["arm64-v8a"],
        ["arm"] = ["armeabi-v7a"],
        ["x64"] = ["x86_64"],
    };

    /// <summary>
    /// Returns why a file is not the file of the release its slot names, null when it is.
    /// </summary>
    public static FileFault? Check(ReleaseSlot slot, string path)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return slot.Extension switch
            {
                ".exe" => Executable(slot, path),
                ".deb" => Package(slot, path),
                ".apk" => Apk(slot, path),
                ReleaseSlots.PatchExtension => Patch(slot, path),
                _ => Fault(slot, FileFault.Unchecked, string.Empty, $"files like {slot.Name} are not checked"),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Fault(slot, FileFault.Unreadable, ex.Message, $"the file does not read as {Kind(slot)}: {ex.Message}");
        }
    }

    // Reads the machine of a portable executable and matches it to the architecture of the slot.
    private static FileFault? Executable(ReleaseSlot slot, string path)
    {
        using var file = File.OpenRead(path);
        var head = new byte[64];
        if (file.Read(head, 0, head.Length) < head.Length || head[0] != (byte)'M' || head[1] != (byte)'Z')
        {
            return NotFormat(slot, "a Windows executable");
        }

        var offset = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(0x3C));
        if (offset > file.Length - 6)
        {
            return NotFormat(slot, "a Windows executable");
        }

        file.Position = offset;
        var signature = new byte[6];
        if (file.Read(signature, 0, signature.Length) < signature.Length
            || signature[0] != (byte)'P' || signature[1] != (byte)'E' || signature[2] != 0 || signature[3] != 0)
        {
            return NotFormat(slot, "a Windows executable");
        }

        var machine = BinaryPrimitives.ReadUInt16LittleEndian(signature.AsSpan(4)) switch
        {
            0x8664 => "x64",
            0xAA64 => "arm64",
            0x014C => "x86",
            var other => $"0x{other:X4}",
        };

        return machine == slot.Arch
            ? null
            : new FileFault(slot.Name, FileFault.Arch, machine, slot.Arch, $"the installer is built for {machine}, not {slot.Arch}");
    }

    // Reads the control file of a Debian package and matches its package name and architecture to the slot.
    private static FileFault? Package(ReleaseSlot slot, string path)
    {
        using var file = File.OpenRead(path);
        var magic = new byte[ArchiveMagic.Length];
        if (file.Read(magic, 0, magic.Length) < magic.Length || !magic.AsSpan().SequenceEqual(ArchiveMagic))
        {
            return NotFormat(slot, "a Debian package");
        }

        var control = default(byte[]);
        var header = new byte[60];
        while (file.Read(header, 0, header.Length) == header.Length)
        {
            var name = Encoding.ASCII.GetString(header, 0, 16).Trim();
            if (!long.TryParse(Encoding.ASCII.GetString(header, 48, 10).Trim(), out var size) || size < 0)
            {
                return NotFormat(slot, "a Debian package");
            }

            if (name == "control.tar.gz")
            {
                control = new byte[size];
                file.ReadExactly(control);
                break;
            }

            if (name.StartsWith("control.tar", StringComparison.Ordinal))
            {
                // The control of a package packed another way is not read: the name stands for the file.
                return null;
            }

            file.Position += size + (size & 1);
        }

        var fields = control is null ? null : Control(control);
        if (fields is null)
        {
            return Fault(slot, FileFault.Control, string.Empty, "the package carries no control file");
        }

        if (!fields.TryGetValue("Package", out var package) || package != slot.Variant)
        {
            return new FileFault(slot.Name, FileFault.Package, package ?? string.Empty, slot.Variant, $"the package is {package ?? "unnamed"}, not {slot.Variant}");
        }

        if (!fields.TryGetValue("Architecture", out var arch) || arch != slot.Arch)
        {
            return new FileFault(slot.Name, FileFault.Arch, arch ?? string.Empty, slot.Arch, $"the package is built for {arch ?? "no architecture"}, not {slot.Arch}");
        }

        return null;
    }

    // Reads the fields of the control file out of the control archive of a package.
    private static Dictionary<string, string>? Control(byte[] packed)
    {
        using var gzip = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.Name.TrimStart('.', '/') != "control" || entry.DataStream is null)
            {
                continue;
            }

            using var reader = new StreamReader(entry.DataStream, Encoding.UTF8);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.ReadLine() is { } line)
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0 && !char.IsWhiteSpace(line[0]))
                {
                    fields[line[..colon]] = line[(colon + 1)..].Trim();
                }
            }

            return fields;
        }

        return null;
    }

    // Lists the native libraries of an Android package and matches them to the architecture of the slot.
    private static FileFault? Apk(ReleaseSlot slot, string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry("AndroidManifest.xml") is null || zip.GetEntry("classes.dex") is null)
        {
            return NotFormat(slot, "an Android package");
        }

        var abis = zip.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith("lib/", StringComparison.Ordinal))
            .Select(name => name.Split('/'))
            .Where(parts => parts.Length > 2)
            .Select(parts => parts[1])
            .ToHashSet(StringComparer.Ordinal);
        var wanted = Abis[slot.Arch];
        if (abis.SetEquals(wanted))
        {
            return null;
        }

        if (abis.Count == 0)
        {
            return Fault(slot, FileFault.Libraries, string.Empty, "the package carries no native libraries");
        }

        var found = string.Join(", ", abis.Order(StringComparer.Ordinal));
        var named = string.Join(", ", wanted);

        return new FileFault(slot.Name, FileFault.Arch, found, named, $"the package is built for {found}, not {named}");
    }

    // Reads the head of the list of a patch.
    private static FileFault? Patch(ReleaseSlot slot, string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry(PatchList) is not { } list)
        {
            return NotFormat(slot, "a patch of AmneziaGeo");
        }

        using var reader = new StreamReader(list.Open(), Encoding.UTF8);

        return reader.ReadLine() == PatchHead ? null : NotFormat(slot, "a patch of AmneziaGeo");
    }

    private static FileFault NotFormat(ReleaseSlot slot, string what) =>
        Fault(slot, FileFault.Format, string.Empty, $"the file is not {what}");

    private static FileFault Fault(ReleaseSlot slot, string reason, string found, string message) =>
        new(slot.Name, reason, found, Kind(slot), message);

    private static string Kind(ReleaseSlot slot) => slot.Extension.TrimStart('.');
}
