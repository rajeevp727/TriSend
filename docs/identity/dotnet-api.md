# Consuming TriSend Identity from .NET APIs

For GreenPantry and OmegaTech APIs, validate access tokens against the TriSend issuer and require the application-specific audience/resource.

Example configuration:

services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(configuration["Authentication:Issuer"]!);
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

Then protect API endpoints with:

[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]

Do not copy the TriSend signing private key into application repositories. Resource APIs consume the public signing keys through OIDC discovery/JWKS.

For Microsoft JwtBearer instead, configure Authority to the TriSend issuer, validate issuer and the expected audience, and use standard OIDC discovery. The audience must be specific to the API, such as greenpantry-api or omegatech-api.

React clients never receive confidential client secrets. They use the public client registration plus Authorization Code + PKCE.
