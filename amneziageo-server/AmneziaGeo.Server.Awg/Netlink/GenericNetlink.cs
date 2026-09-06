namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// The control family every other generic netlink family is looked up through.
/// </summary>
public static class GenericNetlink
{
    private const ushort ControlFamily = 16;
    private const byte GetFamily = 3;
    private const ushort AttributeFamilyId = 1;
    private const ushort AttributeFamilyName = 2;

    private const ushort FlagRequest = 1;

    /// <summary>
    /// Looks up the identifier of a family, returning null when the kernel does not carry it.
    /// </summary>
    public static ushort? Resolve(NetlinkSocket socket, string name)
    {
        var writer = new NetlinkWriter();
        writer.Begin(ControlFamily, FlagRequest, socket.NextSequence(), GetFamily, 1);
        writer.PutString(AttributeFamilyName, name);

        var answer = Answer(socket, writer);
        if (answer is null)
        {
            return null;
        }

        foreach (var message in answer)
        {
            var found = NetlinkAttributes.U16(NetlinkAttributes.Map(message.Payload), AttributeFamilyId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static List<NetlinkMessage>? Answer(NetlinkSocket socket, NetlinkWriter writer)
    {
        try
        {
            return socket.Request(writer.ToArray());
        }
        catch (NetlinkException ex) when (ex.Error == 2)
        {
            return null;
        }
    }
}
