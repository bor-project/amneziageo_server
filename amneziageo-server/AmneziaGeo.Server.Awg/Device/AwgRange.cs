namespace AmneziaGeo.Server.Awg.Device;

/// <summary>
/// A span of numbers the kernel picks one value out of.
/// </summary>
public readonly record struct AwgRange(uint Low, uint High)
{
    /// <summary>
    /// ctor
    /// </summary>
    public AwgRange(uint value)
        : this(value, value)
    {
    }

    /// <summary>
    /// Whether the span holds a single value.
    /// </summary>
    public bool IsOne => Low == High;

    /// <summary>
    /// Whether the span holds nothing but zero, which the kernel takes as its own default.
    /// </summary>
    public bool IsZero => Low == 0 && High == 0;

    /// <summary>
    /// Reads a span the kernel packed into eight bytes.
    /// </summary>
    public static AwgRange Wide(ulong packed) => new((uint)packed, (uint)(packed >> 32));

    /// <summary>
    /// Reads a span the kernel packed into four bytes.
    /// </summary>
    public static AwgRange Narrow(uint packed) => new((ushort)packed, (ushort)(packed >> 16));

    /// <summary>
    /// Packs the span into the eight bytes the kernel takes.
    /// </summary>
    public ulong ToWide() => ((ulong)High << 32) | Low;

    /// <summary>
    /// Packs the span into the four bytes the kernel takes.
    /// </summary>
    public uint ToNarrow() => ((uint)(ushort)High << 16) | (ushort)Low;

    /// <summary>
    /// Writes the span as one number, or as a pair separated by a dash.
    /// </summary>
    public override string ToString() => IsOne ? $"{Low}" : $"{Low}-{High}";
}
