using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TriSend.Auth.Application;
using TriSend.Auth.Infrastructure;
using Xunit;

namespace TriSend.Auth.Tests;

public sealed class IdentityAndSessionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AuthDbContext _db;

    public IdentityAndSessionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task SameEmailFromDifferentProviderRequiresExplicitLinking()
    {
        var service = new UserService(_db);

        var first = await service.ResolveExternalLoginAsync(
            new ExternalIdentityProfile("google", "google-sub-1", "user@example.com", "User", null, null, null, true),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, first.Id);

        var exception = await Assert.ThrowsAsync<AccountLinkRequiredException>(() =>
            service.ResolveExternalLoginAsync(
                new ExternalIdentityProfile("microsoft", "ms-sub-1", "USER@example.com", "User", null, null, null, true),
                CancellationToken.None));

        Assert.Equal("ACCOUNT_LINK_REQUIRED", exception.Message);
    }

    [Fact]
    public async Task ProviderSubjectResolvesTheSameUser()
    {
        var service = new UserService(_db);

        var created = await service.ResolveExternalLoginAsync(
            new ExternalIdentityProfile("google", "stable-sub", "one@example.com", "One", null, null, null, true),
            CancellationToken.None);

        var resolved = await service.FindByExternalIdentityAsync("google", "stable-sub", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(created.Id, resolved!.Id);
    }

    [Fact]
    public async Task FourthActiveSessionIsRejected()
    {
        var service = new SessionService(_db);
        var userId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
            await service.CreateAsync(userId, "sprintdeck", null, $"Device {i}", null, null, CancellationToken.None);

        await Assert.ThrowsAsync<MaxSessionsReachedException>(() =>
            service.CreateAsync(userId, "sprintdeck", null, "Device 4", null, null, CancellationToken.None));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
