# TriSend Deployment

TriSend currently contains two independently deployable concerns:

- Messaging API — existing .NET messaging service with PostgreSQL/Npgsql persistence.
- Central Identity API — OIDC/OAuth authorization server with SQL Server/EF Core persistence.

Keep these data stores isolated. The identity service must not use the messaging PostgreSQL database.

## Central Identity deployment

The identity API is under src/TriSend.Auth.Api.

Production requirements:

- .NET 8 runtime.
- SQL Server for the identity database.
- Azure Key Vault for provider credentials, database credentials and signing/encryption certificate material.
- HTTPS.
- Explicit CORS origins.
- Exact registered redirect and post-logout redirect URIs.
- Trusted forwarded-proxy configuration.
- Persistent shared ASP.NET Core Data Protection keys when more than one identity instance is deployed.
- Production signing and encryption certificates; development certificates must not be used in production.

### Identity configuration

Start from:

src/TriSend.Auth.Api/appsettings.example.json

Configure at minimum:

- ConnectionStrings:Identity
- Authentication:Issuer
- Google client ID/secret
- Microsoft tenant/client ID/secret
- signing certificate secret names
- encryption certificate secret names
- Key Vault URI
- allowed CORS origins
- trusted proxy addresses
- OAuth client registrations

Confidential OAuth clients must have a non-empty secret. Public browser clients must not have a secret.

### Database

Generate and review EF Core migrations before production:

~~~bash
dotnet ef migrations add InitialIdentity \
  --project src/TriSend.Auth.Infrastructure \
  --startup-project src/TriSend.Auth.Api

dotnet ef database update \
  --project src/TriSend.Auth.Infrastructure \
  --startup-project src/TriSend.Auth.Api
~~~

Do not use EnsureCreated for production schema management.

### Health and verification

The identity service exposes:

GET /health

After deployment, verify:

1. Health endpoint returns successfully.
2. OIDC discovery is reachable from the public issuer.
3. JWKS is reachable and contains the active signing key.
4. Authorization Code + PKCE login works.
5. Google and Microsoft provider login works when configured.
6. Access tokens contain the expected issuer and application resource/audience.
7. Resource APIs reject wrong-audience tokens.
8. Current-session logout revokes only the current session.
9. logout-all revokes all active sessions.
10. Session limit rejects a fourth active session.

## Messaging API deployment

The messaging API continues to use its own PostgreSQL connection and deployment configuration. Do not point the identity service at the messaging database.

Use the messaging-specific deployment documentation/configuration for provider credentials and delivery infrastructure.

## Security pipeline

.github/workflows/security.yml runs:

- CodeQL for C#.
- CodeQL for JavaScript/TypeScript.
- .NET vulnerable/deprecated package checks.
- npm high-severity audit.
- Gitleaks secret scanning.

.github/workflows/dast.yml runs OWASP ZAP baseline against a URL supplied through manual workflow dispatch.

DAST should be run against an isolated deployed staging environment before production. The current workflow has not been run against a deployed TriSend Identity target yet.

## Deployment order

For a new identity environment:

1. Provision SQL Server.
2. Provision Key Vault and required secrets/certificates.
3. Configure identity environment variables/secrets.
4. Apply the reviewed EF migration.
5. Deploy the Identity API.
6. Verify discovery/JWKS and health.
7. Register exact OAuth redirect URIs.
8. Deploy/verify consuming applications.
9. Run staging OAuth/OIDC integration tests and DAST.
10. Promote only after security and end-to-end checks pass.
