# TriSend Engineering Memory

> Persistent engineering context for TriSend and its first-party consumers. Read this file BEFORE and AFTER every code update. Treat it as the source of truth for architecture, security decisions, API contracts, deployment state, and next actions.

## Engineering operating rule

Act as a senior software engineer when modifying TriSend or an integrated first-party application.

Before every code change:
1. Read this file.
2. Inspect relevant existing code/configuration.
3. Preserve established architecture and security boundaries.
4. Never invent production URLs, secrets, provider credentials, database values, or API contracts.
5. Prefer small, reviewable changes.
6. Validate affected builds/tests/workflows where available.

After every code change:
1. Re-read this file.
2. Update it if architecture, API contracts, configuration, deployment state, or decisions changed.
3. Record resulting commit/PR/deployment state when known.
4. Never claim deployment success without verified CI/CD evidence.

## Current architecture

TriSend is the centralized identity provider for first-party applications.
Current consumer: 248 Works (`rajeevp727/248-Works`). Planned consumers include GreenPantry and SprintDeck.

Authentication flow:
Google/Microsoft -> TriSend OAuth + PKCE -> short-lived auth_code -> /auth/exchange -> 15-minute JWT + 30-day rotating refresh token -> consumer application.

Supported providers: Google, Microsoft.
TriSend owns provider secrets. Consumer frontends must never contain Google/Microsoft client secrets.

JWT claims: sub, email, name, app, role, sid.
JWT issuer/audience/signing key come from server configuration.
Refresh tokens are stored only as hashes and rotated on refresh.

## Session policy

Maximum active sessions per user: 3.
Fourth login returns HTTP 409 with code MAX_SESSIONS.
The consumer must explicitly confirm replacement; replaceOldest=true revokes the oldest active session.

Supported endpoints:
- GET /auth/google/login
- GET /auth/microsoft/login
- GET /auth/google/callback
- GET /auth/microsoft/callback
- POST /auth/exchange
- POST /auth/refresh
- GET /auth/me
- GET /auth/sessions
- DELETE /auth/sessions/{sessionId}
- POST /auth/logout
- POST /auth/logout-all

## Database

Authentication data is stored in PostgreSQL.
Migration: database/migrations/002_auth_schema.sql
Tables: auth_users, auth_identities, auth_sessions, auth_requests, auth_login_tickets.

## 248 Works integration

248 Works no longer depends on Azure Static Web Apps custom authentication.
Consumer configuration: VITE_TRISEND_AUTH_URL.
Application id: 248works.
Production redirect URI: https://248-works.rajeevstech.in/
Local development redirect URI is supported when explicitly registered.

Never restore SWA /.auth/login/* authentication and never put OAuth provider secrets in 248 Works.

## Deployment verification gate

Do not declare authentication live until all of these are verified:
1. TriSend API is deployed.
2. Auth:PublicBaseUrl points to the actual deployed auth API.
3. Google callback /auth/google/callback is registered.
4. Microsoft callback /auth/microsoft/callback is registered.
5. JWT signing key is configured as a server secret.
6. Google and Microsoft client credentials are configured server-side.
7. PostgreSQL auth migration is applied.
8. VITE_TRISEND_AUTH_URL is configured in 248 Works.
9. Production builds succeed.
10. Google and Microsoft end-to-end login works.
11. Refresh/logout works.
12. Three-session limit and fourth-session replacement work.

## Current status

Completed:
- Central OAuth/JWT authentication implementation.
- Google and Microsoft OAuth.
- PKCE.
- JWT access tokens.
- Rotating refresh tokens.
- Three-session enforcement and oldest-session replacement.
- Logout/session APIs.
- Authentication database schema.
- 248 Works TriSend integration.
- Removal of obsolete SWA authentication.
- This persistent memory document.

## Immediate next work

Priority 1: deploy TriSend API, configure production secrets/environment, apply migration, register OAuth callbacks, configure VITE_TRISEND_AUTH_URL, and run end-to-end authentication tests.

Priority 2: add policy-based JWT authorization middleware, automated authentication integration tests, refresh-token replay/race protection as required, rate limiting, abuse protection, and security audit logging.

Priority 3: publish a stable shared authentication SDK/helper so GreenPantry, SprintDeck, 248 Works, and future first-party applications use the same contract.

## Security rules

Never commit OAuth secrets, JWT signing keys, database passwords, refresh tokens, or authorization codes. Never put server secrets in React/Vite environment variables. Never trust arbitrary redirect URIs. Never log credentials/tokens. Always use HTTPS in production, PKCE, registered redirect URIs, hashed/rotated refresh tokens, server-side session limits, and issuer/audience validation.

## Change log

2026-10-07: Added central OAuth/JWT authentication, PostgreSQL auth schema, session management, 248 Works integration, removal of SWA authentication, and this engineering memory.

2026-10-07: TriSend Render deployment diagnostics and build-time optimization: PostgreSQL health-check failures now log the server-side exception without logging connection-string secrets; Docker build context was narrowed to TriSend.Api plus shared contracts, and publish uses UseAppHost=false to reduce unnecessary image/build output.

2026-10-07: 248 Works footer updated so Privacy and Grievance are navigation links at `/privacy` and `/grievance`; consumer commit `32500e685112bbd6828f92a45e09b0af61aae3f5`.


2026-10-07: 248 Works employer job form now formats salary input using Indian-number grouping (e.g. 15000 -> 15,000), and job description is optional; consumer commit `e1a1192fffeb4e900d949e36310c9a4c5209c9ce`.


2026-10-07: 248 Works UI/UX polish: refined navigation and action labels (`Find Jobs`, `For Employers`, `Applications`, `Post a Job`, `View Job`, `Apply Now`, `Publish Job`), added consistent focus/hover/active states, card elevation, form interaction polish, optional-field styling, and responsive button improvements; consumer commits `d56d348041e1300551f0ec4d300c73757defdabb` and `494283230c289f1d5f3badd712a276b64710e58c`.


2026-10-07: Fixed 248 Works employer `Publish Job` form navigation by preventing native form submission before authentication/business logic; unauthenticated submits now keep the SPA route instead of navigating to `/`. Consumer commit `0d5dc9b6a9b9000f51bcb89752a49df0bd4c9a1c`.

2026-10-07: 248 Works popup behavior standardized: modal dialogs now close only through their explicit close (×) controls; clicking the backdrop/outside a popup no longer dismisses it. Applied to job details and authentication dialogs; consumer commits `61adceb0646980bac2ad7d745ac945fa31063c44` and `fc29912c7e82799b115ddd0b746986f35fad073d`.

2026-10-07: Deployment status reporting rule: after every 248 Works PR/code deployment-triggering change, provide deployment status polls until the relevant GitHub Actions/Azure Static Web Apps deployment reaches a terminal state. Report the run number, commit SHA, status/conclusion, and any failure details when available; never claim deployment success without verified CI/CD evidence.

2026-10-07: 248 Works popup keyboard behavior standardized: pressing Escape now closes open job-details and authentication popups, while clicking the backdrop/outside still does not dismiss them. Explicit × close buttons remain supported; consumer commits `b7f50e49fe227093ee08ea977caac45e0514c312` and `cfc481df380d6cdbd88b531982e33017d2317ab3`.

2026-10-07: 248 Works routing was made URL-driven with dedicated `/emplyee/jobs`, `/employer/jobs/post`, `/applications`, `/privacy`, and `/grievance` routes, browser back/forward support, and a graceful in-app 404 fallback; consumer commit `a4ba52ed2859c59e7dc384e39b92ec79f359e74d`.

2026-10-07: 248 Works MVP baseline expanded against current Naukri/Indeed/LinkedIn patterns: persistent job-seeker profiles, saved jobs, saved-search job alerts, application status tracking, employer job management, employer applicant pipeline statuses, admin summary metrics, protected-route handling, and corrected access-token logout were added across React, Azure Functions and Cosmos-backed APIs. Consumer changes culminated in commits `b20c6c7f9b0d1601d9eacd4d083cbbb310a483c8`, `a75edf531618ebe1fd85589572349197c9ef033a`, `e6a3b3edfd8a538eba5b65114afa366989807132`, `14be55882b6e01ada642286139ced32ec7af7747`, `9a126336aea956e5bcf8fc7ce88bcc3c58cf71cf`, `f5d6d0c19fe538d1599ca6590fc06940fb0351d3`, `4c46739dac1fad173c38d62420c5e1e103ada14c`, `69e31e432e27ef58d1f9579d22c4acfba8b82b69`, and the syntax-correction commit `861d1ceff97d8f4bd28285e2d699e905826dcf17`.




2026-10-07: Added centralized email/credential authentication and app-scoped role persistence for first-party consumers. New TriSend endpoints: POST /auth/register and POST /auth/login. Credentials are stored only as PBKDF2-derived hashes with per-user salts. New PostgreSQL migration 003_password_and_app_roles.sql adds auth_password_credentials and auth_user_roles. OAuth sign-in now reuses the persisted role for an app after first assignment. TriSend commits: 977e9340597487a4c677cca89b0c18f43dbf4624, 97d6286b0d611f525095e5d7239ecae1b66b0df9, 4e693c18a25b067ad6bd6cc6a36b8908d8da0d55.

2026-10-07: 248 Works authentication UX now exposes Log in / Sign up in the top-right, supports email/credential auth plus Google/Microsoft via TriSend, lets new users choose Employee or Employer, and conditionally shows Find Jobs / Post a Job based on the authenticated role. Consumer commits: 79b27139653c9718ff68117176eec44f9dccf609, dd36965c282119dd8aa0e0d03695e472c0f2a70e, d7091373761c9b9a6dc3cc6a6168e668eb4b4c64, 0d417ab2ca4898d90a3efb6422dfac4bf65ed0cc.


2026-10-07: Verified 248 Works frontend deployment for the role-aware authentication UX: Azure Static Web Apps Run #107, commit 0d417ab2ca4898d90a3efb6422dfac4bf65ed0cc, completed successfully. TriSend Backend CI Run #82 built TriSend.Api successfully, but the overall workflow remained failed because the separate TriSend.Worker restore step failed; authentication API deployment remains subject to the existing deployment verification gate.


2026-10-07: 248 Works authentication modal UX was simplified so Login and Sign up are mutually exclusive modes. Removed the duplicate two-tab presentation; each mode now has one contextual switch at the bottom. Consumer commits: 79535494beac1b99cd48abe993a8b0a79b11e45b and 61f1b0613ffe0999a817526252d827278dbca491. Azure Static Web Apps Run #109 for the latest commit is currently in progress; do not claim deployment success until terminal CI evidence is available.


2026-10-07: 248 Works header was simplified to remove duplicate Find Jobs and Post a Job actions. Primary job-entry actions remain in the page body/hero; the top-right header is reserved for account and authenticated workspace navigation. Consumer commit: 83ac41e1cc29d594de3e7560702655bd55e1f5b4. Azure Static Web Apps Run #110 is queued for this change.


2026-10-07: 248 Works auth entry UX finalized per user request: top-right unauthenticated header now shows a single Join Us button; Join Us opens the auth modal, which contains the two-mode Log in / Sign up tabs. The duplicate bottom contextual auth switch is removed. Consumer commits: 34ce105626a40ec0a460c0b36321b02054761278 and 7fa11483c32b31368816f33b7f96f9bbcd45a6ca. Azure Static Web Apps Run #112 is in progress for the final commit.

## Mandatory workflow

Before every future code update: READ THIS FILE -> inspect code -> make change -> validate.
After every future code update: RE-READ THIS FILE -> update memory if state changed -> validate/report verified state.

2026-10-07: Render deployment reliability optimization: bounded the PostgreSQL diagnostic health probe with 3-second Npgsql connection/command timeouts so Supabase connectivity failures fail fast instead of consuming the deployment health-check window. Render's deployment health-check path should use the lightweight /health endpoint; /health/db remains a diagnostic readiness probe and should not gate deployment while PostgreSQL connectivity is being resolved. Code commit: 0c9da6d294f1a6679160cd4e5cd1087c36d02eb8.
