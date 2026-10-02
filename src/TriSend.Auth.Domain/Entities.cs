namespace TriSend.Auth.Domain;
public sealed class User
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string Email { get; set; }=null!;
    public string NormalizedEmail { get; set; }=null!;
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsActive { get; set; }=true;
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }=DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public ICollection<ExternalIdentity> ExternalIdentities { get; set; }=[];
}
public sealed class ExternalIdentity
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; }=null!;
    public string Provider { get; set; }=null!;
    public string ProviderSubject { get; set; }=null!;
    public string? EmailAtProvider { get; set; }
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; }=DateTime.UtcNow;
}
public sealed class AuthSession
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid UserId { get; set; }
    public string? ClientId { get; set; }
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; }=DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
}
public sealed class RefreshTokenRecord
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public string ClientId { get; set; }=null!;
    public string TokenHash { get; set; }=null!;
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
}
public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string? ClientId { get; set; }
    public string EventType { get; set; }=null!;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; }=DateTime.UtcNow;
    public string? Metadata { get; set; }
}
