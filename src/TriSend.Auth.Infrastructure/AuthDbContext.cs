using Microsoft.EntityFrameworkCore;
using TriSend.Auth.Domain;

namespace TriSend.Auth.Infrastructure;
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options):DbContext(options)
{
    public DbSet<User> Users=>Set<User>();
    public DbSet<ExternalIdentity> ExternalIdentities=>Set<ExternalIdentity>();
    public DbSet<AuthSession> AuthSessions=>Set<AuthSession>();
    public DbSet<RefreshTokenRecord> RefreshTokens=>Set<RefreshTokenRecord>();
    public DbSet<AuditLog> AuditLogs=>Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>(e=>{e.ToTable("Users");e.HasKey(x=>x.Id);e.Property(x=>x.Email).HasMaxLength(320).IsRequired();e.Property(x=>x.NormalizedEmail).HasMaxLength(320).IsRequired();e.Property(x=>x.DisplayName).HasMaxLength(200);e.Property(x=>x.FirstName).HasMaxLength(100);e.Property(x=>x.LastName).HasMaxLength(100);e.Property(x=>x.ProfilePictureUrl).HasMaxLength(2048);e.HasIndex(x=>x.NormalizedEmail).IsUnique();});
        modelBuilder.Entity<ExternalIdentity>(e=>{e.ToTable("ExternalIdentities");e.HasKey(x=>x.Id);e.Property(x=>x.Provider).HasMaxLength(50).IsRequired();e.Property(x=>x.ProviderSubject).HasMaxLength(500).IsRequired();e.Property(x=>x.EmailAtProvider).HasMaxLength(320);e.HasIndex(x=>new{x.Provider,x.ProviderSubject}).IsUnique();e.HasOne(x=>x.User).WithMany(x=>x.ExternalIdentities).HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);});
        modelBuilder.Entity<AuthSession>(e=>{e.ToTable("AuthSessions");e.HasKey(x=>x.Id);e.Property(x=>x.ClientId).HasMaxLength(100);e.Property(x=>x.DeviceId).HasMaxLength(200);e.Property(x=>x.DeviceName).HasMaxLength(200);e.Property(x=>x.CreatedByIp).HasMaxLength(64);e.Property(x=>x.UserAgent).HasMaxLength(1024);e.HasIndex(x=>new{x.UserId,x.RevokedAt,x.ExpiresAt});});
        modelBuilder.Entity<RefreshTokenRecord>(e=>{e.ToTable("RefreshTokens");e.HasKey(x=>x.Id);e.Property(x=>x.ClientId).HasMaxLength(100).IsRequired();e.Property(x=>x.TokenHash).HasMaxLength(128).IsRequired();e.HasIndex(x=>x.TokenHash).IsUnique();e.HasIndex(x=>new{x.UserId,x.SessionId});});
        modelBuilder.Entity<AuditLog>(e=>{e.ToTable("AuditLogs");e.HasKey(x=>x.Id);e.Property(x=>x.EventType).HasMaxLength(100).IsRequired();e.Property(x=>x.ClientId).HasMaxLength(100);e.Property(x=>x.IpAddress).HasMaxLength(64);e.Property(x=>x.UserAgent).HasMaxLength(1024);e.Property(x=>x.Metadata);e.HasIndex(x=>new{x.UserId,x.Timestamp});});
    }
}
