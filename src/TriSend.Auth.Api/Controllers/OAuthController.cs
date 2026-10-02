using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using TriSend.Auth.Application;
using TriSend.Auth.Domain;

namespace TriSend.Auth.Api.Controllers;

[ApiController]
public sealed class OAuthController(
    IUserService users,
    ISessionService sessions) : ControllerBase
{
    [HttpGet("~/login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return BadRequest(new { error = "missing_return_url" });

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath != "/oauth/authorize")
            return BadRequest(new { error = "invalid_return_url" });

        var html = $"""
        <!doctype html>
        <html><head><title>Sign in - TriSend</title></head>
        <body style="font-family:system-ui;max-width:420px;margin:80px auto">
          <h1>Sign in to TriSend</h1>
          <p>Choose an identity provider.</p>
          <p><a href="/login/google?returnUrl={Uri.EscapeDataString(returnUrl)}">Continue with Google</a></p>
          <p><a href="/login/microsoft?returnUrl={Uri.EscapeDataString(returnUrl)}">Continue with Microsoft</a></p>
        </body></html>
        """;
        return Content(html, "text/html");
    }

    [HttpGet("~/login/google")]
    [AllowAnonymous]
    public IActionResult Google([FromQuery] string returnUrl)
        => Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, "Google");

    [HttpGet("~/login/microsoft")]
    [AllowAnonymous]
    public IActionResult Microsoft([FromQuery] string returnUrl)
        => Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, "Microsoft");

    [HttpGet("~/oauth/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenID Connect request cannot be retrieved.");

        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal?.FindFirstValue("trisend_user_id") is not { } userIdText ||
            !Guid.TryParse(userIdText, out var userId))
        {
            var returnUrl = Request.GetEncodedUrl();
            return Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var user = await users.FindByExternalIdentityAsync("google", "", HttpContext.RequestAborted)
                   ?? await FindUserByIdAsync(userId);

        if (user is null || !user.IsActive)
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, user.Email)
            .SetDestinations(OpenIddictConstants.Destinations.IdentityToken, OpenIddictConstants.Destinations.AccessToken));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, user.DisplayName ?? user.Email)
            .SetDestinations(OpenIddictConstants.Destinations.IdentityToken, OpenIddictConstants.Destinations.AccessToken));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources(await GetResourcesAsync(request.GetScopes()));
        principal.SetClaim(OpenIddictConstants.Claims.Audience, request.ClientId!);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UserInfo()
    {
        var subject = User.FindFirstValue(OpenIddictConstants.Claims.Subject);
        if (!Guid.TryParse(subject, out var userId)) return Unauthorized();

        var user = await FindUserByIdAsync(userId);
        if (user is null || !user.IsActive) return Unauthorized();

        var result = new Dictionary<string, object?>
        {
            ["sub"] = user.Id.ToString(),
            ["email"] = user.Email,
            ["name"] = user.DisplayName
        };

        return Ok(result);
    }

    [HttpGet("~/oauth/logout")]
    public async Task<IActionResult> Logout([FromQuery] string? post_logout_redirect_uri)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!string.IsNullOrWhiteSpace(post_logout_redirect_uri))
            return Redirect(post_logout_redirect_uri);
        return Ok(new { logged_out = true });
    }

    private async Task<User?> FindUserByIdAsync(Guid id)
    {
        // UserService deliberately exposes provider lookup only; the ID lookup will be added
        // to the application abstraction before the first production release.
        return await Task.FromResult<User?>(null);
    }

    private static Task<IEnumerable<string>> GetResourcesAsync(IReadOnlyCollection<string> scopes) =>
        Task.FromResult<IEnumerable<string>>(scopes.Where(x => x is not OpenIddictConstants.Scopes.OpenId and not OpenIddictConstants.Scopes.Profile and not OpenIddictConstants.Scopes.Email));
    }
}