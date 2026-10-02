# TriSend Documentation

## Product and architecture

- [MVP](./MVP.md) — MVP scope and acceptance criteria
- [PRD](./PRD.md) — product requirements
- [ARCHITECTURE](./architecture.md) — current platform and identity architecture
- [API](./API.md) — messaging API contract
- [CONSUMPTION](./CONSUMPTION.md) — application integration model
- [DATABASE](./DATABASE.md) — messaging data model
- [PROVIDERS](./PROVIDERS.md) — messaging provider abstraction
- [IDENTITY](./identity/README.md) — centralized OIDC/OAuth identity architecture
- [DOTNET IDENTITY API](./identity/dotnet-api.md) — protecting resource APIs with TriSend Identity

## Development and operations

- [LOCAL-DEVELOPMENT](./LOCAL-DEVELOPMENT.md) — local setup
- [DEPLOYMENT](./DEPLOYMENT.md) — deployment architecture and requirements
- [SDK](./SDK.md) — SDK overview
- [ROADMAP](./ROADMAP.md) — implementation sequence
- [SMOKE-TEST](./SMOKE-TEST.md) — smoke-test procedures

## Identity implementation

The identity implementation lives under:

- src/TriSend.Auth.Domain
- src/TriSend.Auth.Application
- src/TriSend.Auth.Infrastructure
- src/TriSend.Auth.Api
- sdk/typescript
- tests/TriSend.Auth.Tests

The identity service uses SQL Server and is intentionally isolated from the messaging PostgreSQL database.

Security automation is under .github/workflows/security.yml and .github/workflows/dast.yml.
