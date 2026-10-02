using System.Security.Claims;
using TriSend.Auth.Domain;

namespace TriSend.Auth.Application;

public sealed record ExternalIdentityProfile(string Provider,string Subject,string Email,string? DisplayName,string? FirstName,string? LastName,string? PictureUrl,bool IsEmailVerified);
public interface IExternalIdentityProvider { string Name { get; } ExternalIdentityProfile Map(ClaimsPrincipal principal); }
public interface IGoogleIdentityProvider : IExternalIdentityProvider { }
public interface IMicrosoftIdentityProvider : IExternalIdentityProvider { }
public interface IUserService
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<User?> FindByExternalIdentityAsync(string provider,string subject,CancellationToken ct);
    Task<User> ResolveExternalLoginAsync(ExternalIdentityProfile profile,CancellationToken ct);
    Task LinkExternalIdentityAsync(Guid userId,ExternalIdentityProfile profile,CancellationToken ct);
}
public interface ISessionService
{
    Task<AuthSession> CreateAsync(Guid userId,string? clientId,string? deviceId,string? deviceName,string? ip,string? userAgent,CancellationToken ct);
    Task<IReadOnlyList<AuthSession>> GetActiveAsync(Guid userId,CancellationToken ct);
    Task<bool> RevokeAsync(Guid userId,Guid sessionId,CancellationToken ct);
    Task<int> RevokeAllAsync(Guid userId,CancellationToken ct);
    Task<bool> IsActiveAsync(Guid sessionId,Guid userId,CancellationToken ct);
}
public interface IAuditService
{
    Task WriteAsync(string eventType,Guid? userId,string? clientId,string? ip,string? userAgent,object? metadata,CancellationToken ct);
}
public sealed class MaxSessionsReachedException : Exception { public MaxSessionsReachedException() : base("MAX_SESSIONS_REACHED") { } }
public sealed class AccountLinkRequiredException : Exception { public AccountLinkRequiredException() : base("ACCOUNT_LINK_REQUIRED") { } }
