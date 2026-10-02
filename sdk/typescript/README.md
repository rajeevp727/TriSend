# TriSend TypeScript Auth SDK

This SDK wraps oidc-client-ts instead of implementing PKCE, state, nonce, token refresh, or OIDC discovery manually.

Browser applications are public OAuth clients. Never place a TriSend client secret in React/Vite source code.

The SDK stores the OIDC user in sessionStorage rather than localStorage. It uses Authorization Code + PKCE and refresh-token based renewal through oidc-client-ts.

Example:

import { createTriSendAuth } from "@trisend/auth-client";

const auth = createTriSendAuth({
  authority: "https://auth.trisend.com/",
  clientId: "sprintdeck",
  redirectUri: "https://sprintdeck.in/auth/callback",
  postLogoutRedirectUri: "https://sprintdeck.in/"
});

await auth.loginWithGoogle();
