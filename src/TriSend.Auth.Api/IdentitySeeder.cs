using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TriSend.Auth.Api;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        foreach (var (name, displayName) in new[]
        {
            ("greenpantry-api", "GreenPantry API"),
            ("omegatech-api", "OmegaTech API"),
            ("sprintdeck", "SprintDeck"),
            ("248works", "248 Works")
        })
        {
            if (await scopes.FindByNameAsync(name, ct) is null)
                await scopes.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = name, DisplayName = displayName, Resources = { name }
                }, ct);
        }

        foreach (var client in configuration.GetSection("OAuthClients").GetChildren())
        {
            var clientId = client["ClientId"];
            if (string.IsNullOrWhiteSpace(clientId) ||
                await applications.FindByClientIdAsync(clientId, ct) is not null) continue;

            var isPublic = bool.TryParse(client["Public"], out var p) && p;
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                DisplayName = client["DisplayName"],
                ClientType = isPublic ? ClientTypes.Public : ClientTypes.Confidential,
                ClientSecret = isPublic ? null : client["ClientSecret"],
                ConsentType = ConsentTypes.Implicit
            };

            foreach (var redirect in client.GetSection("RedirectUris").Get<string[]>() ?? [])
                descriptor.RedirectUris.Add(new Uri(redirect));
            foreach (var redirect in client.GetSection("PostLogoutRedirectUris").Get<string[]>() ?? [])
                descriptor.PostLogoutRedirectUris.Add(new Uri(redirect));

            descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(Permissions.Endpoints.Token);
            descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
            descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
            descriptor.Permissions.Add(Permissions.Scopes.Profile);
            descriptor.Permissions.Add(Permissions.Scopes.Email);
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + "offline_access");

            foreach (var allowed in client.GetSection("AllowedScopes").Get<string[]>() ?? [])
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + allowed);
                descriptor.AddAudiencePermissions(allowed);
                descriptor.AddResourcePermissions(allowed);
            }

            if (isPublic) descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            await applications.CreateAsync(descriptor, ct);
        }
    }
}
