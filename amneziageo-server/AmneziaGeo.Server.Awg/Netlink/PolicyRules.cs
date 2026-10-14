using System.Buffers.Binary;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// One rule of the routing policy of the host.
/// </summary>
/// <param name="Family">The address family of the rule.</param>
/// <param name="Priority">Where the rule stands.</param>
/// <param name="Mark">The firewall mark the rule matches, null when it matches none.</param>
/// <param name="Mask">The mask of that mark, null when the rule names none.</param>
/// <param name="Action">What the rule does with a packet it matches.</param>
/// <param name="Table">The routing table the rule looks a packet up in.</param>
public sealed record PolicyRule(byte Family, uint Priority, uint? Mark, uint? Mask, byte Action, uint Table);

/// <summary>
/// Reads the rules of the routing policy of the host over netlink.
/// </summary>
public static class PolicyRules
{
    /// <summary>
    /// The family of the IPv4 rules.
    /// </summary>
    public const byte Inet = 2;

    /// <summary>
    /// The family of the IPv6 rules.
    /// </summary>
    public const byte Inet6 = 10;

    /// <summary>
    /// The action of a rule that sends a packet to a table.
    /// </summary>
    public const byte ToTable = 1;

    /// <summary>
    /// The action of a rule that answers a packet with unreachable.
    /// </summary>
    public const byte Unreachable = 7;

    private const int NetlinkRoute = 0;

    private const ushort NewRule = 32;

    private const ushort GetRule = 34;

    private const ushort FlagRequest = 1;

    private const ushort FlagDump = 0x300;

    private const int HeaderLength = 16;

    private const int RuleHeaderLength = 12;

    private const int AttributesFrom = RuleHeaderLength - 4;

    private const ushort AttributePriority = 6;

    private const ushort AttributeMark = 10;

    private const ushort AttributeTable = 15;

    private const ushort AttributeMask = 16;

    /// <summary>
    /// Returns the rules the host holds in every family.
    /// </summary>
    public static IReadOnlyList<PolicyRule> Read()
    {
        using var socket = new NetlinkSocket(NetlinkRoute);

        return Rules(socket.Request(Request(socket.NextSequence())));
    }

    /// <summary>
    /// Returns the request for the rules of every family.
    /// </summary>
    public static byte[] Request(uint sequence)
    {
        var request = new byte[HeaderLength + RuleHeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(request, (uint)request.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4), GetRule);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6), FlagRequest | FlagDump);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), sequence);

        return request;
    }

    /// <summary>
    /// Returns the rules an answer carries.
    /// </summary>
    public static IReadOnlyList<PolicyRule> Rules(IEnumerable<NetlinkMessage> answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var rules = new List<PolicyRule>();
        foreach (var message in answer)
        {
            if (message.Type != NewRule || message.Payload.Length < AttributesFrom)
            {
                continue;
            }

            var head = message.Payload.Span;
            var attributes = NetlinkAttributes.Map(message.Payload[AttributesFrom..]);
            rules.Add(new PolicyRule(
                message.Command,
                NetlinkAttributes.U32(attributes, AttributePriority) ?? 0,
                NetlinkAttributes.U32(attributes, AttributeMark),
                NetlinkAttributes.U32(attributes, AttributeMask),
                head[3],
                NetlinkAttributes.U32(attributes, AttributeTable) ?? head[0]));
        }

        return rules;
    }
}
