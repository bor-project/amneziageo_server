using System.Buffers.Binary;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// One segment of TCP as its header tells it.
/// </summary>
/// <param name="Sequence">The number of the first byte the segment carries.</param>
/// <param name="Flags">The flags of the segment.</param>
/// <param name="Data">Where the bytes of the segment start in the packet.</param>
public readonly record struct StreamPiece(uint Sequence, byte Flags, int Data)
{
    private const int Head = 20;

    /// <summary>
    /// Reads the header of TCP that starts at a place of a packet, or returns null when the packet ends before the
    /// header does.
    /// </summary>
    public static StreamPiece? Read(ReadOnlySpan<byte> packet, int at)
    {
        if (at < 0 || packet.Length < at + Head)
        {
            return null;
        }

        var head = (packet[at + 12] >> 4) * 4;
        if (head < Head || packet.Length < at + head)
        {
            return null;
        }

        return new StreamPiece(BinaryPrimitives.ReadUInt32BigEndian(packet[(at + 4)..]), packet[at + 13], at + head);
    }
}

/// <summary>
/// Puts together what a client says first over TCP out of the segments that carry it.
/// </summary>
public sealed class StreamHello
{
    private const int MostLater = 8;

    private const byte Handshake = 0x16;

    private readonly List<(int Offset, byte[] Data)> _later = [];

    private byte[] _said = [];

    private int _whole;

    private uint? _start;

    /// <summary>
    /// How many bytes from the start of the stream were put together.
    /// </summary>
    public int Held => _whole;

    /// <summary>
    /// Reads the name out of what a client sent first over TCP: the first message of TLS or the head of a request.
    /// </summary>
    public static SeenName Read(ReadOnlySpan<byte> said)
    {
        if (said.Length == 0)
        {
            return SeenName.More;
        }

        return said[0] == Handshake ? TlsHello.Read(said) : HttpHead.Read(said);
    }

    /// <summary>
    /// Notes where the stream starts, as a segment that carries no bytes tells it.
    /// </summary>
    public void Start(uint sequence) => _start ??= sequence;

    /// <summary>
    /// Takes the bytes of one more segment and returns what the stream tells so far.
    /// </summary>
    public SeenName Take(uint sequence, ReadOnlySpan<byte> data)
    {
        _start ??= sequence;
        var offset = unchecked((int)(sequence - _start.Value));
        if (data.Length > 0 && offset >= 0 && offset < NameDefaults.MostSaid)
        {
            Place(offset, data);
        }

        var seen = Read(_said.AsSpan(0, _whole));

        return seen.Reading == NameReading.More && _whole >= NameDefaults.MostSaid ? SeenName.None : seen;
    }

    // Puts a segment where it stands, and after it the segments that came before their turn.
    private void Place(int offset, ReadOnlySpan<byte> data)
    {
        if (offset > _whole)
        {
            if (_later.Count < MostLater)
            {
                _later.Add((offset, data.ToArray()));
            }

            return;
        }

        Append(offset, data);
        var joined = true;
        while (joined)
        {
            joined = false;
            for (var at = 0; at < _later.Count; at++)
            {
                if (_later[at].Offset <= _whole)
                {
                    var (place, bytes) = _later[at];
                    _later.RemoveAt(at);
                    Append(place, bytes);
                    joined = true;
                    break;
                }
            }
        }
    }

    // Adds what a segment brings past the bytes already held.
    private void Append(int offset, ReadOnlySpan<byte> data)
    {
        var known = _whole - offset;
        if (known >= data.Length)
        {
            return;
        }

        var fresh = data[known..];
        var room = Math.Min(fresh.Length, NameDefaults.MostSaid - _whole);
        if (room <= 0)
        {
            return;
        }

        if (_said.Length < _whole + room)
        {
            Array.Resize(ref _said, Math.Min(NameDefaults.MostSaid, Math.Max(_whole + room, _said.Length * 2)));
        }

        fresh[..room].CopyTo(_said.AsSpan(_whole));
        _whole += room;
    }
}
