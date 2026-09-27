namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// The addresses and ports that tell one connection of a client from another.
/// </summary>
/// <param name="Protocol">The number of the transport protocol.</param>
/// <param name="Client">The address of the client.</param>
/// <param name="ClientPort">The port the client went from, 0 when the protocol carries none.</param>
/// <param name="Target">The address the client went to.</param>
/// <param name="TargetPort">The port the client went to, 0 when the protocol carries none.</param>
public readonly record struct AccessKey(int Protocol, string Client, int ClientPort, string Target, int TargetPort)
{
    /// <summary>
    /// Returns the connection a record was made for.
    /// </summary>
    public static AccessKey Of(AccessRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new AccessKey(record.Protocol, record.Source, record.SourcePort, record.Target, record.Port);
    }

    /// <summary>
    /// Returns the connection a packet of the client went out on.
    /// </summary>
    public static AccessKey Forth(AccessFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);

        return new AccessKey(flow.Protocol, flow.Source.ToString(), flow.SourcePort, flow.Target.ToString(), flow.Port);
    }

    /// <summary>
    /// Returns the connection a packet came back to the client on.
    /// </summary>
    public static AccessKey Back(AccessFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);

        return new AccessKey(flow.Protocol, flow.Target.ToString(), flow.Port, flow.Source.ToString(), flow.SourcePort);
    }
}

/// <summary>
/// What came back to a connection of a client.
/// </summary>
/// <param name="Key">The connection.</param>
/// <param name="Outcome">What the connection came to, or null when the other side only took it.</param>
public sealed record AccessAnswer(AccessKey Key, string? Outcome)
{
    private const int Icmp = 1;

    private const int Icmp6 = 58;

    private const int Flags = 13;

    private const byte Fin = 0x01;

    private const byte Rst = 0x04;

    private const byte Psh = 0x08;

    private const int ControlHead = 8;

    /// <summary>
    /// Reads a packet that came back to a client, or returns null when it tells nothing about a connection.
    /// </summary>
    public static AccessAnswer? Read(ReadOnlySpan<byte> packet)
    {
        if (AccessFlow.Read(packet) is not { } flow)
        {
            return null;
        }

        var transport = packet[AccessFlow.Transport(packet)..];

        return flow.Protocol switch
        {
            AccessFlow.Tcp => transport.Length > Flags ? new AccessAnswer(AccessKey.Back(flow), Tcp(transport[Flags])) : null,
            Icmp or Icmp6 => Control(flow, transport),
            _ => new AccessAnswer(AccessKey.Back(flow), AccessOutcome.Ok),
        };
    }

    private static string? Tcp(byte flags)
    {
        if ((flags & Rst) != 0)
        {
            return AccessOutcome.Reset;
        }

        if ((flags & Psh) != 0)
        {
            return AccessOutcome.Ok;
        }

        return (flags & Fin) != 0 ? AccessOutcome.Reset : null;
    }

    private static AccessAnswer? Control(AccessFlow flow, ReadOnlySpan<byte> transport)
    {
        if (transport.Length < ControlHead)
        {
            return null;
        }

        var six = flow.Protocol == Icmp6;
        var type = transport[0];
        if (type == (six ? 129 : 0))
        {
            return new AccessAnswer(AccessKey.Back(flow), AccessOutcome.Ok);
        }

        var error = six ? type is 1 or 3 or 4 : type is 3 or 11 or 12;

        return error && AccessFlow.Read(transport[ControlHead..]) is { } inner
            ? new AccessAnswer(AccessKey.Forth(inner), AccessOutcome.Unreachable)
            : null;
    }
}
