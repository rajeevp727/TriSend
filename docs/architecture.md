# TriSend Architecture

TriSend has two deliberately separated data planes:

1. Central Identity — OIDC/OAuth authentication, users, external identities, sessions and token issuance.
2. Messaging platform — SMS, email and WhatsApp delivery and messaging data.

~~~mermaid
graph TD
    Browser[React / Browser Apps] -->|OIDC Authorization Code + PKCE| Identity[TriSend Identity]
    Identity --> IdentityDB[(SQL Server Identity DB)]
    Identity --> Google[Google]
    Identity --> Microsoft[Microsoft]

    Browser -->|Bearer access token| APIs[Application APIs]
    APIs -->|validate issuer/signature/audience| Identity
    APIs --> MessageDB[(PostgreSQL Messaging DB)]
    APIs --> Providers[SMS / Email / WhatsApp Providers]
~~~

## Central Identity

TriSend Identity is the central OIDC Provider and OAuth 2.0 Authorization Server for:

- TriSend
- GreenPantry
- OmegaTech
- SprintDeck
- 248 Works
- future registered applications

The protocol boundary is standards-based OIDC/OAuth 2.0 rather than a custom authentication protocol.

Interactive clients use Authorization Code + PKCE. Browser applications are public OAuth clients and never contain confidential client secrets. Confidential clients authenticate server-side.

Identity stores users, external identities, sessions, refresh-token records, audit information and OpenIddict application/scope metadata in a dedicated SQL Server database.

## Token and API boundary

Resource APIs receive standard bearer JWTs. They must validate:

- TriSend issuer;
- JWT signature using the published JWKS;
- expiration;
- application-specific audience/resource;
- required scopes where applicable.

A valid token authenticates the TriSend subject; it does not automatically grant every business permission in the application.

## Messaging platform

The existing messaging API remains separate from the identity database. Messaging persistence uses PostgreSQL/Npgsql, while identity persistence uses SQL Server/EF Core.

The messaging layer is responsible for channel-specific delivery, provider credentials, message status and business rules. Identity is not used as a replacement for application-level authorization.

## Security boundary

Secrets and signing material are server-side only. Production identity certificates and provider credentials belong in a protected secret store such as Azure Key Vault.

Security checks include CodeQL, .NET dependency audits, npm audit and Gitleaks in CI. OWASP ZAP baseline DAST is available as a manually triggered workflow for a deployed staging/target environment.

See [Central Identity](./identity/README.md) for the detailed identity protocol, session, configuration and production checklist.
