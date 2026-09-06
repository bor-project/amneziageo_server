using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Awg.Uapi;

namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// Reads the AmneziaWG interfaces of the host.
/// </summary>
public interface IAwgDevices
{
    /// <summary>
    /// Returns the names of the interfaces the host carries.
    /// </summary>
    IReadOnlyList<string> Names();

    /// <summary>
    /// Reads one interface, returning null when the host does not carry it.
    /// </summary>
    AwgDevice? Find(string name);

    /// <summary>
    /// Reads every interface the host carries.
    /// </summary>
    IReadOnlyList<AwgDevice> List();

    /// <summary>
    /// Puts a change on an interface.
    /// </summary>
    void Apply(AwgUpdate update);
}

/// <summary>
/// Reads the AmneziaWG interfaces of the host over generic netlink.
/// </summary>
public sealed class AwgDevices : IAwgDevices, IDisposable
{
    private const string NetworkClass = "/sys/class/net";
    private const string DeviceType = "DEVTYPE=";
    private const ushort FlagRequest = 1;
    private const ushort FlagDump = 0x300;
    private const int NotAllowed = 1;
    private const int NoSuchDevice = 19;
    private const int WrongKind = 95;

    private readonly Lock _gate = new();

    private NetlinkSocket? _socket;
    private ushort _family;
    private bool _closed;

    /// <summary>
    /// Returns the names of the interfaces the host carries.
    /// </summary>
    public IReadOnlyList<string> Names()
    {
        if (!Directory.Exists(NetworkClass))
        {
            return [];
        }

        var found = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(NetworkClass))
        {
            if (string.Equals(Kind(path), WgUapi.FamilyName, StringComparison.Ordinal))
            {
                found.Add(Path.GetFileName(path));
            }
        }

        found.Sort(StringComparer.Ordinal);

        return found;
    }

    /// <summary>
    /// Reads one interface, returning null when the host does not carry it.
    /// </summary>
    public AwgDevice? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            var socket = Open();
            var writer = new NetlinkWriter();
            writer.Begin(_family, FlagRequest | FlagDump, socket.NextSequence(), (byte)WgCmd.GetDevice, WgUapi.FamilyVersion);
            writer.PutString((ushort)WgDeviceAttribute.Ifname, name);

            try
            {
                return DeviceReader.Read(socket.Request(writer.ToArray()));
            }
            catch (NetlinkException ex) when (ex.Error is NoSuchDevice or WrongKind)
            {
                return null;
            }
            catch (NetlinkException ex) when (ex.Error == NotAllowed)
            {
                throw new NetlinkException($"reading '{name}' needs the CAP_NET_ADMIN right", NotAllowed);
            }
        }
    }

    /// <summary>
    /// Reads every interface the host carries.
    /// </summary>
    public IReadOnlyList<AwgDevice> List() => [.. Names().Select(Find).OfType<AwgDevice>()];

    /// <summary>
    /// Puts a change on an interface.
    /// </summary>
    public void Apply(AwgUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Name);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            var socket = Open();
            foreach (var request in DeviceWriter.Requests(update, _family, socket.NextSequence))
            {
                try
                {
                    socket.Request(request);
                }
                catch (NetlinkException ex) when (ex.Error == NotAllowed)
                {
                    throw new NetlinkException($"changing '{update.Name}' needs the CAP_NET_ADMIN right", NotAllowed);
                }
            }
        }
    }

    /// <summary>
    /// Closes the netlink socket.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _socket?.Dispose();
            _socket = null;
        }
    }

    private static string? Kind(string path)
    {
        var file = Path.Combine(path, "uevent");
        if (!File.Exists(file))
        {
            return null;
        }

        foreach (var line in File.ReadLines(file))
        {
            if (line.StartsWith(DeviceType, StringComparison.Ordinal))
            {
                return line[DeviceType.Length..];
            }
        }

        return null;
    }

    private NetlinkSocket Open()
    {
        if (_socket is not null)
        {
            return _socket;
        }

        var socket = new NetlinkSocket();
        var family = GenericNetlink.Resolve(socket, WgUapi.FamilyName);
        if (family is null)
        {
            socket.Dispose();

            throw new NetlinkException($"the kernel does not carry the '{WgUapi.FamilyName}' family");
        }

        _socket = socket;
        _family = family.Value;

        return socket;
    }
}
