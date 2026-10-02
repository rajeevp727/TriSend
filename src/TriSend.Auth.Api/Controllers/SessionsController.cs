using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriSend.Auth.Application;

namespace TriSend.Auth.Api.Controllers;

[ApiController]
[Authorize]
[Route("auth/sessions")]
public sealed class SessionsController(ISessionService sessions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId))
            return Unauthorized();

        return Ok(await sessions.GetActiveAsync(userId, ct));
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Revoke(Guid sessionId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId))
            return Unauthorized();

        return await sessions.RevokeAsync(userId, sessionId, ct)
            ? NoContent()
            : NotFound();
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("trisend_user_id"), out var userId))
            return Unauthorized();

        await sessions.RevokeAllAsync(userId, ct);
        await HttpContext.SignOutAsync();
        return NoContent();
    }
}