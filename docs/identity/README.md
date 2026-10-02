# TriSend Central Identity

TriSend Identity is an OpenID Connect Provider and OAuth 2.0 Authorization Server. It is deliberately independent of the messaging database.

## Protocol

- Authorization Code flow only for interactive clients.
- PKCE is mandatory.
- JWT access tokens are signed asymmetrically by TriSend.
- OpenIddict serves standard discovery and JWKS metadata.
- Refresh tokens are encrypted opaque tokens managed by OpenIddict and are automatically redeemed/rotated.
- No implicit or resource-owner-password flow.
- Redirect URIs are registered per client and validated by OpenIddict.

## Identity

User is the TriSend subject. External identities are keyed by Provider + ProviderSubject. Email is never used as the external identity key. An email collision requires explicit account linking.

## Sessions

TriSend maintains a maximum of three active browser sessions per user. Session creation uses a serializable SQL Server transaction.

## Credentials

Production requires separate signing and encryption certificates. They are loaded from Azure Key Vault as base64-encoded PFX secrets. Multiple certificates can be configured to support overlap during rotation. Development certificates are allowed only in Development.

## Database

The identity database is SQL Server and is separate from the existing messaging PostgreSQL database.

Before production, generate and commit EF Core migrations:

dotnet ef migrations add InitialIdentity --project src/TriSend.Auth.Infrastructure --startup-project src/TriSend.Auth.Api
dotnet ef database update --project src/TriSend.Auth.Infrastructure --startup-project src/TriSend.Auth.Api

Do not use EnsureCreated in production.

## API validation

Separate .NET APIs should validate the issuer, JWT signature and expected audience/resource using OpenIddict validation or standard JWT bearer discovery. React clients are public clients and never contain a client secret.

## Operational requirements

- Share ASP.NET Core Data Protection keys across identity instances.
- Keep provider secrets, database credentials and certificates in a secret store.
- Use exact redirect URIs and explicit CORS origins.
- Configure trusted forwarded proxies.
- Do not log authorization codes, access tokens, refresh tokens, secrets or private keys.
- Rotate signing certificates with overlap so old JWKS keys remain available until issued tokens expire.
