import {
  UserManager,
  WebStorageStateStore,
  type User,
  type UserManagerSettings
} from "oidc-client-ts";

export interface TriSendAuthOptions {
  authority: string;
  clientId: string;
  redirectUri: string;
  postLogoutRedirectUri?: string;
  scopes?: string;
  resource?: string;
}

export interface TriSendSession {
  id: string;
  clientId?: string;
  createdAt: string;
  lastActivityAt: string;
  expiresAt: string;
  deviceId?: string;
  deviceName?: string;
}

export function createTriSendAuth(options: TriSendAuthOptions) {
  const settings: UserManagerSettings = {
    authority: options.authority,
    client_id: options.clientId,
    redirect_uri: options.redirectUri,
    post_logout_redirect_uri: options.postLogoutRedirectUri,
    response_type: "code",
    scope: options.scopes ?? "openid profile email offline_access",
    resource: options.resource,
    automaticSilentRenew: true,
    revokeTokensOnSignout: false,
    userStore: new WebStorageStateStore({ store: window.sessionStorage })
  };

  const manager = new UserManager(settings);

  const apiFetch = async (path: string, init: RequestInit = {}) => {
    const response = await fetch(new URL(path, options.authority), {
      ...init,
      credentials: "include",
      headers: { ...(init.headers ?? {}) }
    });
    if (!response.ok) throw new Error("TriSend request failed: " + response.status);
    return response;
  };

  return {
    manager,
    async login(provider?: "google" | "microsoft") {
      await manager.signinRedirect(provider ? { extraQueryParams: { provider } } : {});
    },
    loginWithGoogle() {
      return manager.signinRedirect({ extraQueryParams: { provider: "google" } });
    },
    loginWithMicrosoft() {
      return manager.signinRedirect({ extraQueryParams: { provider: "microsoft" } });
    },
    async handleCallback(url = window.location.href): Promise<User> {
      return manager.signinRedirectCallback(url);
    },
    async logout() {
      await manager.signoutRedirect();
    },
    async getCurrentUser(): Promise<User | null> {
      return manager.getUser();
    },
    async getAccessToken(): Promise<string | null> {
      const user = await manager.getUser();
      if (!user) return null;
      if (!user.expired) return user.access_token;
      try {
        const refreshed = await manager.signinSilent();
        return refreshed?.access_token ?? null;
      } catch {
        return null;
      }
    },
    async refreshSession(): Promise<User | null> {
      const user = await manager.getUser();
      if (!user) return null;
      return manager.signinSilent();
    },
    async isAuthenticated(): Promise<boolean> {
      const user = await manager.getUser();
      return !!user && !user.expired;
    },
    async getSessions(): Promise<TriSendSession[]> {
      const response = await apiFetch("/auth/sessions");
      return response.json();
    },
    async revokeSession(sessionId: string): Promise<void> {
      await apiFetch("/auth/sessions/" + encodeURIComponent(sessionId), { method: "DELETE" });
    },
    async logoutAll(): Promise<void> {
      await apiFetch("/auth/logout-all", { method: "POST" });
      await manager.removeUser();
    }
  };
}
