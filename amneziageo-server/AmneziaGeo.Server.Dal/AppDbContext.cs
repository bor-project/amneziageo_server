using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Probe;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// The accounts, roles, sessions and audit trail of the server.
/// </summary>
public sealed class AppDbContext : IdentityDbContext<AppUser, AppRole, long>
{
    /// <summary>
    /// ctor
    /// </summary>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

    public DbSet<AuditEntity> AuditEntries => Set<AuditEntity>();

    public DbSet<ConfigEntity> Configs => Set<ConfigEntity>();

    public DbSet<ClientEntity> Clients => Set<ClientEntity>();

    public DbSet<GeoSourceEntity> GeoSources => Set<GeoSourceEntity>();

    public DbSet<OutboundEntity> Outbounds => Set<OutboundEntity>();

    public DbSet<RouteRuleEntity> Rules => Set<RouteRuleEntity>();

    public DbSet<BalancerEntity> Balancers => Set<BalancerEntity>();

    public DbSet<DnsSettingsEntity> Resolver => Set<DnsSettingsEntity>();

    public DbSet<PanelEntity> Panel => Set<PanelEntity>();

    public DbSet<ProxyEntity> Proxy => Set<ProxyEntity>();

    public DbSet<TemplateEntity> Templates => Set<TemplateEntity>();

    public DbSet<SubscriptionEntity> Subscription => Set<SubscriptionEntity>();

    /// <summary>
    /// Shapes the tables the server adds to the identity ones.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(128);
            entity.Property(user => user.HostUserName).HasMaxLength(64);
            entity.HasIndex(user => user.HostUserName).IsUnique();
        });

        builder.Entity<AppRole>(entity => entity.Property(role => role.Title).HasMaxLength(128));

        builder.Entity<SessionEntity>(entity =>
        {
            entity.HasOne(session => session.User)
                .WithMany()
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(session => session.Scheme).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(session => new { session.UserId, session.EndedUtc });
        });

        builder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.HasOne(token => token.Session)
                .WithMany()
                .HasForeignKey(token => token.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(token => token.TokenHash).IsUnique();
        });

        builder.Entity<AuditEntity>(entity =>
        {
            entity.Property(entry => entry.Scheme).HasConversion<string>().HasMaxLength(32);
            entity.Property(entry => entry.Action).HasMaxLength(64);
            entity.HasIndex(entry => entry.AtUtc);
        });

        builder.Entity<ConfigEntity>(entity =>
        {
            entity.Property(config => config.Name).HasMaxLength(ConfigRules.MaxNameLength);
            entity.Property(config => config.Host).HasMaxLength(ConfigRules.MaxHostLength);
            entity.HasIndex(config => config.Name).IsUnique();
        });

        builder.Entity<ClientEntity>(entity =>
        {
            entity.Property(client => client.Name).HasMaxLength(ClientRules.MaxNameLength);
            entity.Property(client => client.Note).HasMaxLength(ClientRules.MaxNoteLength);
            entity.Property(client => client.SubscriptionId).HasMaxLength(ClientRules.MaxSubscriptionLength);
            entity.HasIndex(client => new { client.ConfigId, client.Name }).IsUnique();
            entity.HasIndex(client => client.PublicKey).IsUnique();
            entity.HasOne<ConfigEntity>()
                .WithMany()
                .HasForeignKey(client => client.ConfigId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(client => client.TemplateId);
            entity.HasIndex(client => client.SubscriptionId);
        });

        builder.Entity<TemplateEntity>(entity =>
        {
            entity.Property(template => template.Name).HasMaxLength(TemplateRules.MaxNameLength);
            entity.HasIndex(template => template.Name).IsUnique();
        });

        builder.Entity<GeoSourceEntity>(entity =>
        {
            entity.Property(source => source.Name).HasMaxLength(GeoSourceRules.MaxNameLength);
            entity.Property(source => source.Kind).HasMaxLength(16);
            entity.Property(source => source.Url).HasMaxLength(GeoSourceRules.MaxUrlLength);
            entity.HasIndex(source => source.Name).IsUnique();
            entity.HasIndex(source => source.Position);
        });

        builder.Entity<OutboundEntity>(entity =>
        {
            entity.Property(outbound => outbound.Name).HasMaxLength(ConfigRules.MaxNameLength);
            entity.Property(outbound => outbound.Kind).HasMaxLength(16);
            entity.Property(outbound => outbound.Host).HasMaxLength(ConfigRules.MaxHostLength);
            entity.Property(outbound => outbound.Probe).HasMaxLength(ProbeDefaults.MaxAddressLength);
            entity.HasIndex(outbound => outbound.Name).IsUnique();
            entity.HasIndex(outbound => outbound.Mark).IsUnique();
            entity.HasIndex(outbound => outbound.Position);
        });

        builder.Entity<DnsSettingsEntity>(entity =>
        {
            entity.Property(row => row.Upstreams).HasMaxLength(512);
            entity.Property(row => row.Listen).HasMaxLength(512);
        });

        builder.Entity<PanelEntity>(entity =>
        {
            entity.Property(row => row.Listen).HasMaxLength(1024);
            entity.Property(row => row.Domains).HasMaxLength(4096);
            entity.Property(row => row.Path).HasMaxLength(PanelRules.MaxPathLength);
            entity.Property(row => row.Certificate).HasMaxLength(PanelRules.MaxFileLength);
            entity.Property(row => row.CertificateKey).HasMaxLength(PanelRules.MaxFileLength);
            entity.Property(row => row.Language).HasMaxLength(8);
        });

        builder.Entity<SubscriptionEntity>(entity =>
        {
            entity.Property(row => row.Listen).HasMaxLength(1024);
            entity.Property(row => row.Domains).HasMaxLength(4096);
            entity.Property(row => row.Path).HasMaxLength(PanelRules.MaxPathLength);
            entity.Property(row => row.Certificate).HasMaxLength(PanelRules.MaxFileLength);
            entity.Property(row => row.CertificateKey).HasMaxLength(PanelRules.MaxFileLength);
            entity.Property(row => row.Title).HasMaxLength(SubscriptionRules.MaxTitleLength);
        });

        builder.Entity<ProxyEntity>(entity =>
        {
            entity.Property(row => row.Name).HasMaxLength(ProxyRules.MaxNameLength);
            entity.Property(row => row.Kind).HasMaxLength(8);
            entity.Property(row => row.Path).HasMaxLength(ProxyRules.MaxPathLength);
            entity.Property(row => row.Target).HasMaxLength(ProxyRules.MaxTargetLength);
            entity.Property(row => row.Certificate).HasMaxLength(ProxyRules.MaxFileLength);
            entity.Property(row => row.CertificateKey).HasMaxLength(ProxyRules.MaxFileLength);
            entity.HasIndex(row => row.Name).IsUnique();
            entity.HasIndex(row => new { row.Kind, row.Port }).IsUnique();
        });

        builder.Entity<BalancerEntity>(entity =>
        {
            entity.Property(balancer => balancer.Name).HasMaxLength(BalanceRules.MaxNameLength);
            entity.Property(balancer => balancer.Strategy).HasMaxLength(16);
            entity.HasIndex(balancer => balancer.Name).IsUnique();
            entity.HasIndex(balancer => balancer.Position);
        });

        builder.Entity<RouteRuleEntity>(entity =>
        {
            entity.Property(rule => rule.Name).HasMaxLength(RouteRules.MaxNameLength);
            entity.Property(rule => rule.Action).HasMaxLength(16);
            entity.Property(rule => rule.Protocol).HasMaxLength(16);
            entity.Property(rule => rule.Outbound).HasMaxLength(ConfigRules.MaxNameLength);
            entity.HasIndex(rule => rule.Position);
        });
    }
}
