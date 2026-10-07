using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TriSend.Api.Services;

namespace TriSend.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly IConfiguration _configuration;

    public AuthController(AuthService auth, IConfiguration configuration)
    {
        _auth = auth;
        _configuration = configuration;
    }

    [HttpGet("{provider}/login")]
    public async Task<IActionResult> Login(
        string provider,
        [FromQuery] string appId,
        [FromQuery] string role,
        [FromQuery] string redirectUri,
        CancellationToken ct)
    {
        try
        {
            return Redirect(await _auth.CreateLoginUrlAsync(provider, appId, role, redirectUri, ct));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(
        string provider,
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken ct)
    {
        try
        {
            return Redirect(await _auth.CompleteOAuthAsync(provider, code, state, ct));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message, maxSessions = ex.MaxSessions });
        }
    }

    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _auth.ExchangeCodeAsync(
                request.Code,
                request.ReplaceOldest,
                Request.Headers.UserAgent.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                ct));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                code = ex.Code,
                message = ex.Message,
                maxSessions = ex.MaxSessions
            });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _auth.RefreshAsync(request.RefreshToken, ct));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var principal = ValidateBearerToken();
        if (principal is null) return Unauthorized();

        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId))
            return Unauthorized();

        await _auth.LogoutAsync(userId, sessionId, ct);
        return Ok(new { message = "Logged out." });
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var principal = ValidateBearerToken();
        if (principal is null) return Unauthorized();

        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            return Unauthorized();

        await _auth.LogoutAllAsync(userId, ct);
        return Ok(new { message = "All sessions revoked." });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var principal = ValidateBearerToken();
        if (principal is null) return Unauthorized();

        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            return Unauthorized();

        return Ok(new
        {
            id = userId,
            email = principal.FindFirstValue(JwtRegisteredClaimNames.Email),
            name = principal.FindFirstValue("name"),
            role = principal.FindFirstValue("role"),
            appId = principal.FindFirstValue("app"),
            sessionId = principal.FindFirstValue("sid")
        });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        var principal = ValidateBearerToken();
        if (principal is null) return Unauthorized();

        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            return Unauthorized();

        return Ok(await _auth.SessionsAsync(userId, ct));
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        var principal = ValidateBearerToken();
        if (principal is null) return Unauthorized();

        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            return Unauthorized();

        await _auth.RevokeSessionAsync(userId, sessionId, ct);
        return NoContent();
    }

    private ClaimsPrincipal? ValidateBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = header["Bearer ".Length..].Trim();
        var signingKey = _configuration["Auth:Jwt:SigningKey"];
        var issuer = _configuration["Auth:Jwt:Issuer"];
        var audience = _configuration["Auth:Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(signingKey) ||
            string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(audience))
            return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);
        }
        catch
        {
            return null;
        }
    }

    public sealed record ExchangeRequest(string Code, bool ReplaceOldest = false);
    public sealed record RefreshRequest(string RefreshToken);
}
