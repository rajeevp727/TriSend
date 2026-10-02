# Consuming TriSend Identity from .NET APIs

TriSend resource APIs should validate standard OAuth 2.0 bearer access tokens issued by the TriSend OIDC provider. The resource API owns its business authorization; TriSend establishes the authenticated subject and token claims.

## Required validation

Every resource API should validate:

- issuer: the exact configured TriSend issuer;
- JWT signature using TriSend's published JWKS;
- token expiration;
- expected audience/resource;
- required scopes for the endpoint, where applicable.

Never accept a token merely because it is a valid JWT. A token issued for `greenpantry-api` must not authorize access to `omegatech-api`.

## OpenIddict validation

For an API using OpenIddict validation, configure the TriSend issuer and use the ASP.NET Core integration:

```csharp
services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(configuration["Authentication:Issuer"]!);
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });
```

Protect endpoints with:

```csharp
[Authorize(AuthenticationSchemes =
    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
```

Configure the validation stack to enforce the API's expected resource/audience and required scopes. Do not copy the TriSend signing private key into the API. Public signing keys are obtained through OIDC discovery/JWKS.

## Microsoft JwtBearer

A resource API can also use the standard ASP.NET Core JwtBearer handler:

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = configuration["Authentication:Issuer"];
        options.Audience = "greenpantry-api";
        options.RequireHttpsMetadata = true;
    });
```

Use the corresponding resource for each API, for example:

- GreenPantry: `greenpantry-api`
- OmegaTech: `omegatech-api`

Validate issuer and audience rather than accepting arbitrary audiences. For APIs with multiple resources, configure the allowed audiences explicitly.

## React/browser clients

Browser applications are public OAuth clients. They use Authorization Code + PKCE and never receive or store a confidential client secret.

## Failure handling

Treat invalid, expired, wrong-audience, or insufficient-scope tokens as authentication/authorization failures. Do not fall back to email matching or another application-local identity heuristic.

## Operational guidance

- Keep the TriSend issuer URL in environment/configuration, not hard-coded per deployment.
- Cache discovery/JWKS according to the authentication library's normal behavior; do not fetch JWKS on every request.
- Do not log bearer tokens or Authorization headers.
- Prefer HTTPS everywhere outside local development.
