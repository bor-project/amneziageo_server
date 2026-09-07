using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Config;
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
    }
}
