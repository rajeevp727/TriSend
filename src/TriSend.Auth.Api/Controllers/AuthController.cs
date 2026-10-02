using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriSend.Auth.Application;

namespace TriSend.Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(IUserService users, ISessionService sessions, IAuditService audit) : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId)) return Unauthorized();
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive) return Unauthorized();
        return Ok(new
        {
            user.Id, user.Email, user.DisplayName, user.FirstName, user.LastName,
            user.ProfilePictureUrl, user.IsEmailVerified, user.LastLoginAt
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId)) return Unauthorized();
        if (Guid.TryParse(User.FindFirstValue("trisend_session_id"), out var sessionId))
            await sessions.RevokeAsync(userId, sessionId, ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await audit.WriteAsync("USER_LOGOUT", userId, null, RemoteIp(), UserAgent(), null, ct);
        return NoContent();
    }

    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId)) return Unauthorized();
        var count = await sessions.RevokeAllAsync(userId, ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await audit.WriteAsync("USER_LOGOUT_ALL", userId, null, RemoteIp(), UserAgent(), new { count }, ct);
        return Ok(new { revoked = count });
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId)) return Unauthorized();
        return Ok(await sessions.GetActiveAsync(userId, ct));
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId)) return Unauthorized();
        return await sessions.RevokeAsync(userId, sessionId, ct) ? NoContent() : NotFound();
    }

    [HttpPost("link/{provider}")]
    [Authorize]
    public IActionResult Link(string provider, [FromQuery] string returnUrl)
    {
        if (provider is not ("google" or "microsoft") ||
            !Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId))
            return BadRequest(new { error = "invalid_request" });

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Request.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "invalid_return_url" });

        var properties = new AuthenticationProperties { RedirectUri = "/signin-external" };
        properties.Items["return_url"] = returnUrl;
        properties.Items["provider"] = provider;
        properties.Items["link_user_id"] = userId.ToString();
        return Challenge(properties, provider == "google" ? "Google" : "Microsoft");
    }

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? UserAgent() => Request.Headers.UserAgent.ToString();
}
