# TriSend TypeScript Auth SDK

The SDK is the browser integration for TriSend Central Identity. It wraps `oidc-client-ts` so applications do not implement OAuth/OIDC protocol details themselves.

## Security model

Browser applications are **public OAuth clients**.

- Never put a TriSend client secret in React/Vite source code.
- Use Authorization Code + PKCE.
- Keep the registered redirect URI exact.
- Use HTTPS in deployed environments.
- The SDK uses `sessionStorage` for its OIDC user/session state rather than `localStorage`.
- State, nonce, PKCE, discovery, token handling, and refresh behavior are delegated to `oidc-client-ts`.

## Installation

Build/package the SDK from `sdk/typescript` and consume the generated package from the application.

## Basic setup

```ts
import { createTriSendAuth } from "@trisend/auth-client";

const auth = createTriSendAuth({
  authority: "https://auth.trisend.com/",
  clientId: "sprintdeck",
  redirectUri: "https://sprintdeck.in/auth/callback",
  postLogoutRedirectUri: "https://sprintdeck.in/"
});
```

The `authority` should point at the TriSend issuer. The client ID must be a registered public client for a browser application.

## Login

```ts
await auth.login();
```

Provider-specific helpers are also available:

```ts
await auth.loginWithGoogle();
await auth.loginWithMicrosoft();
```

## Callback

Handle the registered callback route with:

```ts
await auth.handleCallback();
```

The callback URL must exactly match the URI registered for the client.

## Session and token helpers

The SDK exposes helpers for:

- `getCurrentUser()`
- `getAccessToken()`
- `refreshSession()`
- `isAuthenticated()`
- `getSessions()`
- `revokeSession(sessionId)`
- `logout()`
- `logoutAll()`

Use `getAccessToken()` when calling a protected resource API. Do not persist access or refresh tokens in application-managed local storage.

## Logout

`logout()` performs the normal OIDC logout flow. It is associated with the current TriSend session.

Use `logoutAll()` when the user explicitly wants all TriSend sessions revoked.

## Resource APIs

When calling an application API, send the access token as a standard bearer token:

```http
Authorization: Bearer <access-token>
```

The API must validate the token's issuer, signature, expiration, and expected resource/audience. The browser should not attempt to validate or decode the token as an authorization decision.

## Example React flow

```ts
const auth = createTriSendAuth({
  authority: import.meta.env.VITE_TRISEND_AUTHORITY,
  clientId: import.meta.env.VITE_TRISEND_CLIENT_ID,
  redirectUri: import.meta.env.VITE_TRISEND_REDIRECT_URI,
  postLogoutRedirectUri: import.meta.env.VITE_TRISEND_POST_LOGOUT_REDIRECT_URI
});

export async function signIn() {
  await auth.login();
}

export async function finishSignIn() {
  await auth.handleCallback();
}

export async function signOut() {
  await auth.logout();
}
```

## Production checklist

- Register the exact production redirect and post-logout URLs.
- Use a public client registration.
- Never expose a confidential client secret.
- Use HTTPS.
- Run the SDK build and security audit in CI.
- Test login, callback, refresh, session listing/revocation, logout, and logout-all against staging before release.
