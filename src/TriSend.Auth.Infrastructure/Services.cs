using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TriSend.Auth.Application;
using TriSend.Auth.Domain;

namespace TriSend.Auth.Infrastructure;

public sealed class UserService(AuthDbContext db) : IUserService
{
    public Task<User?> FindByExternalIdentityAsync(string provider, string subject, CancellationToken ct) =>
        db.ExternalIdentities.Include(x => x.User)
            .Where(x => x.Provider == provider && x.ProviderSubject == subject)
            .Select(x => x.User)
            .SingleOrDefaultAsync(ct);

    public async Task<User> CreateFromExternalIdentityAsync(string provider, string subject, string email, string? displayName, string? firstName, string? lastName, string? pictureUrl, bool emailVerified, CancellationToken ct)
    {
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.Trim().ToUpperInvariant(),
            DisplayName = displayName,
            FirstName = firstName,
            LastName = lastName,
            ProfilePictureUrl = pictureUrl,
            IsEmailVerified = emailVerified
        };
        user.ExternalIdentities.Add(new ExternalIdentity
        {
            User = user, Provider = provider, ProviderSubject = subject, EmailAtProvider = email
        });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task LinkExternalIdentityAsync(Guid userId, string provider, string subject, string? email, CancellationToken ct)
    {
        var exists = await db.ExternalIdentities.AnyAsync(x => x.Provider == provider && x.ProviderSubject == subject, ct);
        if (exists) throw new InvalidOperationException("EXTERNAL_IDENTITY_ALREADY_LINKED");
        db.ExternalIdentities.Add(new ExternalIdentity { UserId = userId, Provider = provider, ProviderSubject = subject, EmailAtProvider = email });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SessionService(AuthDbContext db) : ISessionService
{
    public async Task<AuthSession> CreateAsync(Guid userId, string? clientId, string? deviceId, string? deviceName, string? ip, string? userAgent, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await db.AuthSessions.CountAsync(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now, ct);
        if (active >= 3) throw new MaxSessionsReachedException();

        var session = new AuthSession
        {
            UserId = userId, ClientId = clientId, DeviceId = deviceId, DeviceName = deviceName,
            CreatedByIp = ip, UserAgent = userAgent, CreatedAt = now, LastActivityAt = now,
            ExpiresAt = now.AddDays(30)
        };
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public Task<IReadOnlyList<AuthSession>> GetActiveAsync(Guid userId, CancellationToken ct) =>
        db.AuthSessions.Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(x => x.LastActivityAt).ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<AuthSession>)t.Result, ct);

    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.AuthSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
        if (session is null) return false;
        session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var sessions = await db.AuthSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var session in sessions) session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return sessions.Count;
    }
}

public sealed class MaxSessionsReachedException : Exception
{
    public MaxSessionsReachedException() : base("You already have 3 active devices.") { }
}

public static class TokenHashing
{
    public static string Sha256(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}