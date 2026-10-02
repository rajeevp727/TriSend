using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TriSend.Auth.Api;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        var scopes = new[]
        {
            ("greenpantry", "GreenPantry API"),
            ("omegatech", "OmegaTech API"),
            ("sprintdeck", "SprintDeck"),
            ("248works", "248 Works")
        };

        foreach (var (name, display) in scopes)
        {
            if (await scopeManager.FindByNameAsync(name) is null)
            {
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = name,
                    DisplayName = display,
                    Resources = { name }
                });
            }
        }

        var clients = new[]
        {
            new { Id = "trisend", Name = "TriSend", Type = ClientTypes.Confidential, Secret = configuration["OAuthClients:TriSendSecret"], Redirect = "https://trisend.in/auth/callback" },
            new { Id = "greenpantry", Name = "GreenPantry", Type = ClientTypes.Confidential, Secret = configuration["OAuthClients:GreenPantrySecret"], Redirect = "https://greenpantry.in/auth/callback" },
            new { Id = "omegatech", Name = "OmegaTech", Type = ClientTypes.Confidential, Secret = configuration["OAuthClients:OmegaTechSecret"], Redirect = "https://omegatech.in/auth/callback" },
            new { Id = "sprintdeck", Name = "SprintDeck", Type = ClientTypes.Public, Secret = (string?)null, Redirect = "https://sprintdeck.in/auth/callback" },
            new { Id = "248works", Name = "248 Works", Type = ClientTypes.Public, Secret = (string?)null, Redirect = "https://248works.in/auth/callback" }
        };

        foreach (var client in clients)
        {
            if (await manager.FindByClientIdAsync(client.Id) is not null) continue;

            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = client.Id,
                DisplayName = client.Name,
                ClientType = client.Type,
                ClientSecret = client.Secret,
                ConsentType = ConsentTypes.Explicit,
                RedirectUris = { new Uri(client.Redirect) },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.OpenId,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile
                }
            };

            foreach (var (name, _) in scopes)
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + name);

            if (client.Type == ClientTypes.Public)
                descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

            await manager.CreateAsync(descriptor);
        }
    }
}