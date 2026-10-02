using TriSend.Auth.Domain;

namespace TriSend.Auth.Application;

public interface IExternalIdentityProvider
{
    string Name { get; }
}

public interface IGoogleIdentityProvider : IExternalIdentityProvider { }
public interface IMicrosoftIdentityProvider : IExternalIdentityProvider { }

public interface IUserService
{
    Task<User?> FindByExternalIdentityAsync(string provider, string subject, CancellationToken ct);
    Task<User> CreateFromExternalIdentityAsync(string provider, string subject, string email, string? displayName, string? firstName, string? lastName, string? pictureUrl, bool emailVerified, CancellationToken ct);
    Task LinkExternalIdentityAsync(Guid userId, string provider, string subject, string? email, CancellationToken ct);
}

public interface ISessionService
{
    Task<AuthSession> CreateAsync(Guid userId, string? clientId, string? deviceId, string? deviceName, string? ip, string? userAgent, CancellationToken ct);
    Task<IReadOnlyList<AuthSession>> GetActiveAsync(Guid userId, CancellationToken ct);
    Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<int> RevokeAllAsync(Guid userId, CancellationToken ct);
}