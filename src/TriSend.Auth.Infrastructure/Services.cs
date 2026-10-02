using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TriSend.Auth.Application;
using TriSend.Auth.Domain;

namespace TriSend.Auth.Infrastructure;
public sealed class UserService(AuthDbContext db):IUserService
{
    public Task<User?> GetByIdAsync(Guid id,CancellationToken ct)=>db.Users.Include(x=>x.ExternalIdentities).SingleOrDefaultAsync(x=>x.Id==id,ct);
    public Task<User?> FindByExternalIdentityAsync(string provider,string subject,CancellationToken ct)=>db.ExternalIdentities.Include(x=>x.User).Where(x=>x.Provider==provider&&x.ProviderSubject==subject).Select(x=>x.User).SingleOrDefaultAsync(ct);
    public async Task<User> ResolveExternalLoginAsync(ExternalIdentityProfile p,CancellationToken ct)
    {
        var identity=await db.ExternalIdentities.Include(x=>x.User).SingleOrDefaultAsync(x=>x.Provider==p.Provider&&x.ProviderSubject==p.Subject,ct);
        if(identity is not null)
        {
            if(!identity.User.IsActive)throw new UnauthorizedAccessException("ACCOUNT_DISABLED");
            var u=identity.User;identity.LastLoginAt=DateTime.UtcNow;u.DisplayName=p.DisplayName??u.DisplayName;u.FirstName=p.FirstName??u.FirstName;u.LastName=p.LastName??u.LastName;u.ProfilePictureUrl=p.PictureUrl??u.ProfilePictureUrl;u.IsEmailVerified|=p.IsEmailVerified;u.LastLoginAt=DateTime.UtcNow;u.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);return u;
        }
        var normalized=p.Email.Trim().ToUpperInvariant();
        if(await db.Users.AnyAsync(x=>x.NormalizedEmail==normalized,ct))throw new AccountLinkRequiredException();
        var user=new User{Email=p.Email.Trim(),NormalizedEmail=normalized,DisplayName=p.DisplayName,FirstName=p.FirstName,LastName=p.LastName,ProfilePictureUrl=p.PictureUrl,IsEmailVerified=p.IsEmailVerified,LastLoginAt=DateTime.UtcNow};
        user.ExternalIdentities.Add(new ExternalIdentity{User=user,Provider=p.Provider,ProviderSubject=p.Subject,EmailAtProvider=p.Email});db.Users.Add(user);await db.SaveChangesAsync(ct);return user;
    }
    public async Task LinkExternalIdentityAsync(Guid userId,ExternalIdentityProfile p,CancellationToken ct)
    {
        if(!await db.Users.AnyAsync(x=>x.Id==userId&&x.IsActive,ct))throw new KeyNotFoundException("USER_NOT_FOUND");
        if(await db.ExternalIdentities.AnyAsync(x=>x.Provider==p.Provider&&x.ProviderSubject==p.Subject,ct))throw new InvalidOperationException("EXTERNAL_IDENTITY_ALREADY_LINKED");
        db.ExternalIdentities.Add(new ExternalIdentity{UserId=userId,Provider=p.Provider,ProviderSubject=p.Subject,EmailAtProvider=p.Email});await db.SaveChangesAsync(ct);
    }
}
public sealed class SessionService(AuthDbContext db):ISessionService
{
    public async Task<AuthSession>CreateAsync(Guid userId,string? clientId,string? deviceId,string? deviceName,string? ip,string? userAgent,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);var now=DateTime.UtcNow;
        if(await db.AuthSessions.CountAsync(x=>x.UserId==userId&&x.RevokedAt==null&&x.ExpiresAt>now,ct)>=3)throw new MaxSessionsReachedException();
        var s=new AuthSession{UserId=userId,ClientId=clientId,DeviceId=deviceId,DeviceName=deviceName,CreatedByIp=ip,UserAgent=userAgent,CreatedAt=now,LastActivityAt=now,ExpiresAt=now.AddDays(30)};db.AuthSessions.Add(s);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return s;
    }
    public async Task<IReadOnlyList<AuthSession>>GetActiveAsync(Guid userId,CancellationToken ct)=>await db.AuthSessions.AsNoTracking().Where(x=>x.UserId==userId&&x.RevokedAt==null&&x.ExpiresAt>DateTime.UtcNow).OrderByDescending(x=>x.LastActivityAt).ToListAsync(ct);
    public async Task<bool>RevokeAsync(Guid userId,Guid sessionId,CancellationToken ct){var s=await db.AuthSessions.SingleOrDefaultAsync(x=>x.Id==sessionId&&x.UserId==userId&&x.RevokedAt==null,ct);if(s is null)return false;s.RevokedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);return true;}
    public async Task<int>RevokeAllAsync(Guid userId,CancellationToken ct){var ss=await db.AuthSessions.Where(x=>x.UserId==userId&&x.RevokedAt==null).ToListAsync(ct);foreach(var s in ss)s.RevokedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);return ss.Count;}
    public Task<bool>IsActiveAsync(Guid sessionId,Guid userId,CancellationToken ct)=>db.AuthSessions.AnyAsync(x=>x.Id==sessionId&&x.UserId==userId&&x.RevokedAt==null&&x.ExpiresAt>DateTime.UtcNow,ct);
}
public sealed class AuditService(AuthDbContext db):IAuditService
{
    public async Task WriteAsync(string eventType,Guid? userId,string? clientId,string? ip,string? ua,object? metadata,CancellationToken ct){db.AuditLogs.Add(new AuditLog{EventType=eventType,UserId=userId,ClientId=clientId,IpAddress=ip,UserAgent=ua,Metadata=metadata is null?null:JsonSerializer.Serialize(metadata)});await db.SaveChangesAsync(ct);}
}
public sealed class GoogleIdentityProvider:IGoogleIdentityProvider
{
    public string Name=>"google";
    public ExternalIdentityProfile Map(ClaimsPrincipal p)=>new(Name,p.FindFirst("sub")?.Value??p.FindFirst(ClaimTypes.NameIdentifier)?.Value??throw new SecurityTokenValidationException("Google subject missing."),p.FindFirst("email")?.Value??p.FindFirst(ClaimTypes.Email)?.Value??throw new SecurityTokenValidationException("Google email missing."),p.FindFirst("name")?.Value??p.FindFirst(ClaimTypes.Name)?.Value,p.FindFirst("given_name")?.Value??p.FindFirst(ClaimTypes.GivenName)?.Value,p.FindFirst("family_name")?.Value??p.FindFirst(ClaimTypes.Surname)?.Value,p.FindFirst("picture")?.Value,string.Equals(p.FindFirst("email_verified")?.Value,"true",StringComparison.OrdinalIgnoreCase));
}
public sealed class MicrosoftIdentityProvider:IMicrosoftIdentityProvider
{
    public string Name=>"microsoft";
    public ExternalIdentityProfile Map(ClaimsPrincipal p)=>new(Name,p.FindFirst("sub")?.Value??throw new SecurityTokenValidationException("Microsoft subject missing."),p.FindFirst("email")?.Value??p.FindFirst("preferred_username")?.Value??p.FindFirst(ClaimTypes.Email)?.Value??throw new SecurityTokenValidationException("Microsoft email missing."),p.FindFirst("name")?.Value??p.FindFirst(ClaimTypes.Name)?.Value,p.FindFirst("given_name")?.Value??p.FindFirst(ClaimTypes.GivenName)?.Value,p.FindFirst("family_name")?.Value??p.FindFirst(ClaimTypes.Surname)?.Value,null,false);
}
public static class TokenHashing{public static string Sha256(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));}
