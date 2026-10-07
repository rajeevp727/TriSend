using Npgsql;
using TriSend.Api.Data;
using TriSend.Api.Email;
using TriSend.Api.Security;
using TriSend.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            return;
        }

        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddSingleton<MessageRepository>();
builder.Services.AddSingleton<BrandedEmailTemplate>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
});

var app = builder.Build();

app.UseCors("Frontend");

// Authentication endpoints use their own JWT validation.
// The legacy tenant API-key middleware continues to protect /v1 messaging endpoints.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/v1"),
    branch => branch.UseMiddleware<MvpApiKeyMiddleware>());

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "trisend-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapMethods("/health/db", new[] { "GET", "HEAD" }, async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var connectionString = configuration.GetConnectionString("Postgres");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.Json(
            new { status = "unhealthy", database = "not_configured" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        // Keep the diagnostic DB probe bounded so a network/pooler problem
        // cannot consume Render's entire deployment health-check window.
        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 3,
            CommandTimeout = 3
        };

        await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("select 1", connection)
        {
            CommandTimeout = 3
        };
        await command.ExecuteScalarAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "healthy",
            database = "postgres",
            utc = DateTimeOffset.UtcNow
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex,
            "PostgreSQL health check failed. Database connection could not be established.");

        return Results.Json(
            new { status = "unhealthy", database = "postgres" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapControllers();

app.Run();
