using System.Security.Cryptography;
using System.Text;

namespace TriSend.Api.Security;

public sealed class MvpApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly byte[] _configuredApiKey =
        Encoding.UTF8.GetBytes(configuration["Mvp:ApiKey"] ?? string.Empty);

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        if (_configuredApiKey.Length == 0)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "api_not_configured",
                message = "MVP API key is not configured."
            });
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";

        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        var suppliedValue = authorization[bearerPrefix.Length..].Trim();
        if (suppliedValue.Length == 0)
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        var suppliedKey = Encoding.UTF8.GetBytes(suppliedValue);
        if (suppliedKey.Length != _configuredApiKey.Length ||
            !CryptographicOperations.FixedTimeEquals(suppliedKey, _configuredApiKey))
        {
            await WriteUnauthorizedAsync(context);
            return;
        }

        await next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "unauthorized",
            message = "A valid tenant API key is required."
        });
    }
}