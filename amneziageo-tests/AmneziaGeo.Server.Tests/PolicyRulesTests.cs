using System.Buffers.Binary;
using AmneziaGeo.Server.Awg.Netlink;

namespace AmneziaGeo.Server.Tests;

public class PolicyRulesTests
{
    private const ushort NewRule = 32;

    [Fact]
    public void TheRequestAsksForTheRulesOfEveryFamily()
    {
        var request = PolicyRules.Request(7);

        Assert.Equal(28, request.Length);
        Assert.Equal(28u, BinaryPrimitives.ReadUInt32LittleEndian(request));
        Assert.Equal((ushort)34, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4)));
        Assert.Equal((ushort)0x301, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6)));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(8)));
        Assert.Equal(new byte[12], request[16..]);
    }

    [Fact]
    public void TheAnswerSaysWhereEveryRuleStandsWhatItMatchesAndWhatItDoes()
    {
        var rules = PolicyRules.Rules(
        [
            Rule(PolicyRules.Inet, 254, PolicyRules.ToTable, writer => writer.PutU32(6, 32766)),
            Rule(PolicyRules.Inet, 252, PolicyRules.ToTable, writer =>
            {
                writer.PutU32(6, 10000);
                writer.PutU32(10, 0xA601);
                writer.PutU32(16, uint.MaxValue);
                writer.PutU32(15, 42601);
            }),
            Rule(PolicyRules.Inet6, 0, PolicyRules.Unreachable, writer =>
            {
                writer.PutU32(6, 10255);
                writer.PutU32(10, 0xA600);
                writer.PutU32(16, 0xFFFFFF00);
            }),
            Rule(PolicyRules.Inet, 255, PolicyRules.ToTable, _ => { }),
        ]);

        Assert.Equal(
            [
                new PolicyRule(PolicyRules.Inet, 32766, null, null, PolicyRules.ToTable, 254),
                new PolicyRule(PolicyRules.Inet, 10000, 0xA601, uint.MaxValue, PolicyRules.ToTable, 42601),
                new PolicyRule(PolicyRules.Inet6, 10255, 0xA600, 0xFFFFFF00, PolicyRules.Unreachable, 0),
                new PolicyRule(PolicyRules.Inet, 0, null, null, PolicyRules.ToTable, 255),
            ],
            rules);
    }

    [Fact]
    public void WhatIsNotARuleIsPassedOver()
    {
        var other = new NetlinkMessage(24, 0, PolicyRules.Inet, new byte[20]);
        var cut = new NetlinkMessage(NewRule, 0, PolicyRules.Inet, new byte[4]);

        Assert.Empty(PolicyRules.Rules([other, cut]));
    }

    // Lays out a rule as the kernel answers it: what follows the family in the header of the rule, then its attributes.
    private static NetlinkMessage Rule(byte family, byte table, byte action, Action<NetlinkWriter> fill)
    {
        var writer = new NetlinkWriter();
        writer.Begin(NewRule, 0, 7, 0, 0);
        fill(writer);
        var attributes = writer.ToArray()[20..];
        var payload = new byte[8 + attributes.Length];
        payload[0] = table;
        payload[3] = action;
        attributes.CopyTo(payload, 8);

        return new NetlinkMessage(NewRule, 0, family, payload);
    }
}
