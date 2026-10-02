using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using TriSend.Auth.Application;

namespace TriSend.Auth.Api.Controllers;

[ApiController]
public sealed class OAuthController(
    IUserService users,
    ISessionService sessions,
    IAuditService audit,
    IGoogleIdentityProvider google,
    IMicrosoftIdentityProvider microsoft) : ControllerBase
{
    private const string ExternalScheme = "External";

    [HttpGet("~/login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public IActionResult Login([FromQuery] string returnUrl, [FromQuery] string? client_id = null, [FromQuery] string? provider = null)
    {
        if (!IsLocalAuthorizeUrl(returnUrl)) return BadRequest(new { error = "invalid_return_url" });
        if (provider is "google" or "microsoft") return ChallengeExternal(provider == "google" ? GoogleDefaults.AuthenticationScheme : "Microsoft", returnUrl, client_id);
        var encoded = Uri.EscapeDataString(returnUrl);
        var client = string.IsNullOrWhiteSpace(client_id) ? "" : $"&client_id={Uri.EscapeDataString(client_id)}";
        return Content($"""
            <!doctype html><html><head><meta charset="utf-8"><title>Sign in - TriSend</title></head>
            <body style="font-family:system-ui;max-width:420px;margin:80px auto">
            <h1>Sign in to TriSend</h1><p>Use a trusted identity provider.</p>
            <p><a href="/login/google?returnUrl={encoded}{client}">Continue with Google</a></p>
            <p><a href="/login/microsoft?returnUrl={encoded}{client}">Continue with Microsoft</a></p>
            </body></html>
            """, "text/html");
    }

    [HttpGet("~/login/google")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public IActionResult Google([FromQuery] string returnUrl, [FromQuery] string? client_id = null) =>
        ChallengeExternal(GoogleDefaults.AuthenticationScheme, returnUrl, client_id);

    [HttpGet("~/login/microsoft")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public IActionResult Microsoft([FromQuery] string returnUrl, [FromQuery] string? client_id = null) =>
        ChallengeExternal("Microsoft", returnUrl, client_id);

    [HttpGet("~/signin-external")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ExternalCallback(CancellationToken ct)
    {
        var result = await HttpContext.AuthenticateAsync(ExternalScheme);
        if (!result.Succeeded || result.Principal is not { Identity.IsAuthenticated: true })
            return Unauthorized(new { error = "external_login_failed" });

        var providerName = result.Properties?.Items["provider"]
            ?? throw new InvalidOperationException("External provider missing.");
        IExternalIdentityProvider provider = providerName switch
        {
            "google" => google,
            "microsoft" => microsoft,
            _ => throw new InvalidOperationException("Unsupported external provider.")
        };

        try
        {
            var profile = provider.Map(result.Principal);
            var linkUser = result.Properties?.Items["link_user_id"];
            if (Guid.TryParse(linkUser, out var linkUserId))
            {
                await users.LinkExternalIdentityAsync(linkUserId, profile, ct);
                await audit.WriteAsync("ACCOUNT_LINKED", linkUserId, null, RemoteIp(), UserAgent(),
                    new { provider = profile.Provider }, ct);
            }
            else
            {
                var user = await users.ResolveExternalLoginAsync(profile, ct);
                var clientId = result.Properties?.Items["client_id"];
                var session = await sessions.CreateAsync(
                    user.Id, clientId, null, null, RemoteIp(), UserAgent(), ct);

                var identity = new ClaimsIdentity(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);
                identity.AddClaim(new System.Security.Claims.Claim("trisend_user_id", user.Id.ToString()));
                identity.AddClaim(new System.Security.Claims.Claim("trisend_session_id", session.Id.ToString()));

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties { IsPersistent = true, ExpiresUtc = new DateTimeOffset(session.ExpiresAt) });

                await audit.WriteAsync("USER_LOGIN", user.Id, clientId, RemoteIp(), UserAgent(),
                    new { provider = profile.Provider, sessionId = session.Id }, ct);
            }

            var returnUrl = result.Properties?.Items["return_url"];
            if (!string.IsNullOrWhiteSpace(returnUrl) && IsLocalAuthorizeUrl(returnUrl))
                return Redirect(returnUrl);
            return Ok(new { authenticated = true });
        }
        catch (AccountLinkRequiredException)
        {
            return Conflict(new { error = "ACCOUNT_LINK_REQUIRED" });
        }
        catch (MaxSessionsReachedException)
        {
            return Conflict(new { error = "MAX_SESSIONS_REACHED" });
        }
        finally
        {
            await HttpContext.SignOutAsync(ExternalScheme);
        }
    }

    [HttpGet("~/oauth/authorize")]
    [AllowAnonymous]
    [EnableRateLimiting("oauth")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded ||
            result.Principal?.FindFirstValue("trisend_user_id") is not { } userIdText ||
            !Guid.TryParse(userIdText, out var userId))
        {
            if (Request.Query["prompt"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("none", StringComparer.Ordinal))
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            var returnUrl = Request.Scheme + "://" + Request.Host + Request.PathBase + Request.Path + Request.QueryString;
            var provider = Request.Query["provider"].ToString();
            var providerQuery = string.IsNullOrWhiteSpace(provider) ? "" : $"&provider={Uri.EscapeDataString(provider)}";
            return Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}&client_id={Uri.EscapeDataString(Request.Query["client_id"].ToString())}{providerQuery}");
        }

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive) return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (!Guid.TryParse(result.Principal.FindFirstValue("trisend_session_id"), out var sessionId) ||
            !await sessions.IsActiveAsync(sessionId, userId, ct))
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, user.DisplayName ?? user.Email));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, user.Email));
        identity.AddClaim(new Claim("session_id", sessionId.ToString()));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(Request.Query["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        principal.SetResources(Request.Query["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(s => s.EndsWith("-api", StringComparison.OrdinalIgnoreCase)));
        principal.SetDestinations(claim => claim.Type switch
        {
            OpenIddictConstants.Claims.Email when principal.HasScope(OpenIddictConstants.Scopes.Email) =>
                [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            OpenIddictConstants.Claims.Name when principal.HasScope(OpenIddictConstants.Scopes.Profile) =>
                [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            "session_id" => [],
            _ => [OpenIddictConstants.Destinations.AccessToken]
        });

        await audit.WriteAsync("OAUTH_LOGIN", user.Id, Request.Query["client_id"].ToString(), RemoteIp(), UserAgent(),
            new { scopes = Request.Query["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries), sessionId }, ct);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UserInfo(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(OpenIddictConstants.Claims.Subject), out var userId))
            return Unauthorized();

        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive) return Unauthorized();

        var result = new Dictionary<string, object?> { ["sub"] = user.Id.ToString() };
        if (User.HasScope(OpenIddictConstants.Scopes.Email))
        {
            result["email"] = user.Email;
            result["email_verified"] = user.IsEmailVerified;
        }
        if (User.HasScope(OpenIddictConstants.Scopes.Profile))
        {
            result["name"] = user.DisplayName;
            result["given_name"] = user.FirstName;
            result["family_name"] = user.LastName;
            result["picture"] = user.ProfilePictureUrl;
        }
        return Ok(result);
    }

    [HttpGet("~/oauth/logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromQuery] string? post_logout_redirect_uri, CancellationToken ct)
    {
        var requestClientId = Request.Query["client_id"].ToString();
        if (!string.IsNullOrWhiteSpace(post_logout_redirect_uri) &&
            !await IsRegisteredPostLogoutRedirectAsync(requestClientId, post_logout_redirect_uri, ct))
            return BadRequest(new { error = "invalid_post_logout_redirect_uri" });

        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (Guid.TryParse(result.Principal?.FindFirstValue("trisend_user_id"), out var userId))
        {
            await sessions.RevokeAllAsync(userId, ct);
            await audit.WriteAsync("USER_LOGOUT_ALL", userId, null, RemoteIp(), UserAgent(), null, ct);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return string.IsNullOrWhiteSpace(post_logout_redirect_uri)
            ? Ok(new { logged_out = true })
            : Redirect(post_logout_redirect_uri);
    }

    private async Task<bool> IsRegisteredPostLogoutRedirectAsync(string? clientId, string redirectUri, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
            return false;

        // The logout redirect must be an exact registered URI. The authorization
        // server remains the source of truth for client registrations.
        var manager = HttpContext.RequestServices.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();
        var application = await manager.FindByClientIdAsync(clientId, ct);
        if (application is null)
            return false;

        return await manager.ValidatePostLogoutRedirectUriAsync(
            application,
            uri.ToString(),
            ct);
    }

    private IActionResult ChallengeExternal(string scheme, string returnUrl, string? clientId)
    {
        if (!IsLocalAuthorizeUrl(returnUrl)) return BadRequest(new { error = "invalid_return_url" });
        var provider = scheme == GoogleDefaults.AuthenticationScheme ? "google" : "microsoft";
        var properties = new AuthenticationProperties { RedirectUri = "/signin-external" };
        properties.Items["return_url"] = returnUrl;
        properties.Items["client_id"] = clientId;
        properties.Items["provider"] = provider;
        return Challenge(properties, scheme);
    }

    private bool IsLocalAuthorizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return string.Equals(uri.Scheme, Request.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == (Request.Host.Port ?? (Request.IsHttps ? 443 : 80)) &&
               string.Equals(uri.AbsolutePath, "/oauth/authorize", StringComparison.Ordinal);
    }

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? UserAgent() => Request.Headers.UserAgent.ToString();
}
