using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Host;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Tests;

public class UdpPrivacyLifecycleTests
{
    [Fact]
    public async Task ARefusedGuardRemovalKeepsTheConfigurationForTheNextDeleteAttempt()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg0") with { UdpPrivate = true }, default).ConfigureAwait(false);
        var held = added.Record!;
        var ledger = Held(held.Name);
        ledger.Refuses = "firewall";
        var host = new EndpointHost(ledger, new Kernel());

        await Assert.ThrowsAsync<HostNetworkException>(() => bench.Configs.RemoveAsync(
            held.Id, (previous, ct) => host.WithdrawAsync(previous.Name, ct), default)).ConfigureAwait(false);

        bench.Db.ChangeTracker.Clear();
        var remaining = (await bench.Configs.FindAsync(held.Id, default).ConfigureAwait(false))!;
        Assert.Equal(held.Name, remaining.Name);
        Assert.True(remaining.UdpPrivate);
        Assert.Equal(held.IsDefault, remaining.IsDefault);

        ledger.Refuses = string.Empty;
        var removed = await bench.Configs.RemoveAsync(held.Id, (previous, ct) => host.WithdrawAsync(previous.Name, ct), default).ConfigureAwait(false);

        Assert.True(removed.IsOk);
        Assert.Null(await bench.Configs.FindAsync(held.Id, default).ConfigureAwait(false));
        Assert.Equal(UdpPrivacyRuleset.Clear(held.Name), ledger.Ruleset);
    }

    [Fact]
    public async Task ARefusedGuardRemovalKeepsTheOldNameUntilRenameCanComplete()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg0") with { UdpPrivate = true }, default).ConfigureAwait(false);
        var held = added.Record!;
        var draft = held with { Name = "awg-new", ListenPort = 51821 };
        var ledger = Held(held.Name);
        ledger.Refuses = "firewall";
        var host = new EndpointHost(ledger, new Kernel());

        await Assert.ThrowsAsync<HostNetworkException>(() => bench.Configs.ChangeAsync(
            held.Id, draft, (previous, ct) => host.WithdrawAsync(previous.Name, ct), default)).ConfigureAwait(false);

        bench.Db.ChangeTracker.Clear();
        var remaining = (await bench.Configs.FindAsync(held.Id, default).ConfigureAwait(false))!;
        Assert.Equal(held.Name, remaining.Name);
        Assert.Equal(held.ListenPort, remaining.ListenPort);

        ledger.Refuses = string.Empty;
        var renamed = await bench.Configs.ChangeAsync(held.Id, draft, (previous, ct) => host.WithdrawAsync(previous.Name, ct), default).ConfigureAwait(false);

        Assert.True(renamed.IsOk);
        Assert.Equal(draft.Name, renamed.Record!.Name);
        Assert.Equal(draft.ListenPort, renamed.Record.ListenPort);
        Assert.Equal(UdpPrivacyRuleset.Clear(held.Name), ledger.Ruleset);
    }

    [Fact]
    public async Task AnInvalidRenameLeavesTheWorkingInterfaceUntouched()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg0") with { UdpPrivate = true }, default).ConfigureAwait(false);
        var held = added.Record!;
        var ledger = Held(held.Name);
        var host = new EndpointHost(ledger, new Kernel());

        var refused = await bench.Configs.ChangeAsync(held.Id, held with { Name = "bad name" },
            (previous, ct) => host.WithdrawAsync(previous.Name, ct), default).ConfigureAwait(false);

        Assert.False(refused.IsOk);
        Assert.Contains(held.Name, ledger.Links);
        Assert.Empty(ledger.Steps);
        Assert.Equal(held.Name, (await bench.Configs.FindAsync(held.Id, default).ConfigureAwait(false))!.Name);
    }

    [Fact]
    public async Task ADatabaseFailureAfterWithdrawalKeepsTheOldIdentityForRetry()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg0") with { UdpPrivate = true }, default).ConfigureAwait(false);
        var held = added.Record!;
        var ledger = Held(held.Name);
        var host = new EndpointHost(ledger, new Kernel());
        var draft = held with { Name = "awg-new" };
        await bench.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_config_rename BEFORE UPDATE ON Configs BEGIN SELECT RAISE(FAIL, 'test save refusal'); END;").ConfigureAwait(false);

        await Assert.ThrowsAsync<DbUpdateException>(() => bench.Configs.ChangeAsync(
            held.Id, draft, (previous, ct) => host.WithdrawAsync(previous.Name, ct), default)).ConfigureAwait(false);

        Assert.DoesNotContain(held.Name, ledger.Links);
        Assert.Equal(UdpPrivacyRuleset.Clear(held.Name), ledger.Ruleset);
        bench.Db.ChangeTracker.Clear();
        Assert.Equal(held.Name, (await bench.Configs.FindAsync(held.Id, default).ConfigureAwait(false))!.Name);

        await bench.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_config_rename;").ConfigureAwait(false);
        var retried = await bench.Configs.ChangeAsync(held.Id, draft, (previous, ct) => host.WithdrawAsync(previous.Name, ct), default).ConfigureAwait(false);

        Assert.True(retried.IsOk);
        Assert.Equal(draft.Name, retried.Record!.Name);
    }

    private static Ledger Held(string name)
    {
        var ledger = new Ledger();
        ledger.Links.Add(name);
        ledger.Tables[UdpPrivacyRuleset.Table(name)] = "held";
        return ledger;
    }
}
