# TriSend Central Identity

TriSend Identity is the centralized authentication service for the TriSend application family. It is an OpenID Connect (OIDC) Provider and OAuth 2.0 Authorization Server, independent of the existing messaging database.

## Supported protocol

- OIDC discovery and standard OAuth 2.0/OIDC endpoints.
- Authorization Code flow for interactive clients.
- PKCE is required for authorization-code exchanges.
- JWT access tokens are asymmetrically signed; access-token encryption is disabled so resource APIs can validate standard JWTs.
- JWKS/discovery expose the public signing keys.
- Refresh tokens are opaque/encrypted tokens managed by OpenIddict and redeemed/rotated by OpenIddict. TriSend additionally binds refresh-token validation to the active TriSend session.
- No implicit grant or resource-owner-password grant.
- Registered redirect URIs are exact and client-specific.
- Endpoints currently exposed: `/oauth/authorize`, `/oauth/token`, `/userinfo`, `/oauth/logout`.

## Applications and resources

The current resource/audience registrations are:

| Application | Client type | Resource/audience |
|---|---|---|
| TriSend | Confidential | application-specific resources configured for the client |
| GreenPantry | Confidential | `greenpantry-api` |
| OmegaTech | Confidential | `omegatech-api` |
| SprintDeck | Public | `sprintdeck` |
| 248 Works | Public | `248works` |

Browser applications are public OAuth clients and must never contain a client secret. Confidential-client secrets must be supplied through protected deployment configuration/secret storage.

Initial OIDC scopes are `openid`, `profile`, `email`, and `offline_access`. Application-specific resources are granted only when registered for that client.

## External identity model

Google and Microsoft are supported through provider abstractions. The durable external identity key is:

`Provider + ProviderSubject`

Email is not the external identity key. If a provider login presents an email already belonging to a different TriSend account, the service requires explicit account linking instead of silently merging accounts.

The internal user record owns identity data such as email, display name, names, profile picture, verification state, active state, creation/update timestamps, and last-login timestamp.

## Sessions and logout

TriSend permits at most three active browser sessions per user. Session creation uses a serializable SQL Server transaction; a fourth active login is rejected rather than silently deleting an existing session.

Session-management APIs are available through the auth controller:

- `GET /auth/me`
- `GET /auth/sessions`
- `DELETE /auth/sessions/{sessionId}`
- `POST /auth/logout`
- `POST /auth/logout-all`

Normal OIDC logout revokes the current session. Logging out every session is an explicit `/auth/logout-all` operation.

The logout `post_logout_redirect_uri` is validated against the registered client before redirecting.

## Database and migrations

Identity uses a dedicated SQL Server database. The existing messaging API continues to use its separate PostgreSQL database.

Generate the initial EF Core migration before production:

```bash
dotnet ef migrations add InitialIdentity \
  --project src/TriSend.Auth.Infrastructure \
  --startup-project src/TriSend.Auth.Api

dotnet ef database update \
  --project src/TriSend.Auth.Infrastructure \
  --startup-project src/TriSend.Auth.Api
```

Do not use `EnsureCreated` for production schema management.

## Configuration and secrets

See `src/TriSend.Auth.Api/appsettings.example.json` for the configuration shape.

Production configuration includes:

- Identity SQL Server connection string.
- TriSend issuer URL.
- Google and Microsoft provider credentials.
- Azure Key Vault URI.
- Signing and encryption certificate secret names.
- Exact CORS origins.
- Trusted forwarded-proxy IPs.
- OAuth client registrations and redirect URIs.

Production signing/encryption certificates are loaded from Azure Key Vault as base64-encoded PFX secrets. The service fails closed when production certificates are not configured. Development certificates are permitted only in Development when explicitly enabled.

Do not commit client secrets, provider secrets, database credentials, certificate material, authorization codes, access tokens, refresh tokens, or private keys.

## Security controls

Current implementation includes:

- Explicit CORS allow-list.
- HTTPS redirection.
- Secure, HttpOnly, SameSite cookies.
- Separate short-lived external-authentication cookie.
- Login and OAuth endpoint rate limiting.
- Exact registered redirect/post-logout redirect validation.
- Explicit resource/audience assignment.
- Session-aware refresh-token validation.
- Account-linking protection for email collisions.
- Audit-service abstraction for authentication/session events.
- Asymmetric signing and certificate-based key management.

## SAST and DAST

Security automation is defined in:

- `.github/workflows/security.yml`: CodeQL for C# and TypeScript/JavaScript, .NET dependency/deprecation audits, npm audit, and Gitleaks.
- `.github/workflows/dast.yml`: manually triggered OWASP ZAP baseline scan.

DAST requires a deployed staging/target URL. Do not point the workflow at an arbitrary production or third-party URL. The workflow accepts the deployed Identity base URL as `target_url`.

A successful SAST run does not mean the system is production-ready; it only verifies the automated checks that ran successfully.

## Production checklist

Before production deployment:

1. Generate and review the SQL Server EF Core migration.
2. Configure Google/Microsoft production credentials in a secret store.
3. Configure Azure Key Vault signing and encryption certificates.
4. Configure a persistent shared ASP.NET Core Data Protection key ring for all identity instances.
5. Register exact production redirect and post-logout URLs.
6. Configure explicit CORS origins and trusted forwarded proxies.
7. Verify each API validates issuer, signature, expiration, and its expected audience/resource.
8. Run the full OAuth/OIDC integration and end-to-end tests with real provider configuration.
9. Deploy an isolated staging environment and run the OWASP ZAP DAST workflow against it.
10. Verify certificate rotation and JWKS overlap before relying on multiple identity instances.
11. Review refresh-token rotation/reuse-detection requirements against the exact OpenIddict configuration and add application-level persistence if stronger token-family/reuse detection is required.
12. Review audit-log retention, alerting, and operational access controls.

## Architecture boundary

TriSend owns centralized identity, authentication, OIDC/OAuth flows, sessions, and token issuance.

Each consuming application owns its own business data, roles, permissions, and application-specific authorization. A resource API must not treat possession of a valid TriSend token as permission to perform every business operation.

