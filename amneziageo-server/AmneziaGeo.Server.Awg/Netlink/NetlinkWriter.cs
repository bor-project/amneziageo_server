using System.Buffers.Binary;
using System.Text;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// Lays out a generic netlink request in the wire format.
/// </summary>
public sealed class NetlinkWriter
{
    private const int HeaderLength = 16;
    private const int GenericLength = 4;

    private byte[] _buffer = new byte[4096];
    private int _length;

    /// <summary>
    /// Starts a request and reserves room for its headers.
    /// </summary>
    public void Begin(ushort family, ushort flags, uint sequence, byte command, byte version)
    {
        _length = 0;
        Room(HeaderLength + GenericLength);

        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(4), family);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(6), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(8), sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(12), 0);
        _buffer[HeaderLength] = command;
        _buffer[HeaderLength + 1] = version;
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(HeaderLength + 2), 0);
        _length = HeaderLength + GenericLength;
    }

    /// <summary>
    /// Appends an attribute holding one byte.
    /// </summary>
    public void PutU8(ushort type, byte value) => PutBytes(type, [value]);

    /// <summary>
    /// Appends an attribute holding a 16 bit number.
    /// </summary>
    public void PutU16(ushort type, ushort value)
    {
        Span<byte> value2 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(value2, value);
        PutBytes(type, value2);
    }

    /// <summary>
    /// Appends an attribute holding a 32 bit number.
    /// </summary>
    public void PutU32(ushort type, uint value)
    {
        Span<byte> value4 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(value4, value);
        PutBytes(type, value4);
    }

    /// <summary>
    /// Appends an attribute holding a 64 bit number.
    /// </summary>
    public void PutU64(ushort type, ulong value)
    {
        Span<byte> value8 = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(value8, value);
        PutBytes(type, value8);
    }

    /// <summary>
    /// Appends an attribute holding a null terminated string.
    /// </summary>
    public void PutString(ushort type, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> terminated = stackalloc byte[bytes.Length + 1];
        bytes.CopyTo(terminated);
        terminated[^1] = 0;
        PutBytes(type, terminated);
    }

    /// <summary>
    /// Appends an attribute holding raw bytes.
    /// </summary>
    public void PutBytes(ushort type, ReadOnlySpan<byte> value)
    {
        var length = 4 + value.Length;
        Room(Align(length));
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length), (ushort)length);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length + 2), type);
        value.CopyTo(_buffer.AsSpan(_length + 4));
        _length += Align(length);
    }

    /// <summary>
    /// Opens a nested attribute and returns the mark its end needs.
    /// </summary>
    public int BeginNested(ushort type)
    {
        var mark = _length;
        Room(4);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length + 2), (ushort)(type | 0x8000));
        _length += 4;
        return mark;
    }

    /// <summary>
    /// Closes the nested attribute opened at a mark.
    /// </summary>
    public void EndNested(int mark)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(mark), (ushort)(_length - mark));
    }

    /// <summary>
    /// Seals the request with its final length and hands out the bytes.
    /// </summary>
    public byte[] ToArray()
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(0), (uint)_length);
        return _buffer.AsSpan(0, _length).ToArray();
    }

    private static int Align(int length) => (length + 3) & ~3;

    private void Room(int extra)
    {
        if (_length + extra <= _buffer.Length)
        {
            return;
        }

        var grown = _buffer.Length;
        while (grown < _length + extra)
        {
            grown *= 2;
        }

        Array.Resize(ref _buffer, grown);
    }
}
