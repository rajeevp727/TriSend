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

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/v1"),
    branch => branch.UseMiddleware<MvpApiKeyMiddleware>());

app.MapMethods("/health", new[] { "GET", "HEAD" }, () => Results.Ok(new
{
    status = "healthy",
    service = "trisend-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/health/auth-config", (IConfiguration configuration) =>
{
    var allowedRedirectUris = configuration
        .GetSection("Auth:AllowedRedirectUris")
        .Get<string[]>() ?? [];

    const string productionRedirectUri = "https://248works.rajeevstech.in/";

    return Results.Ok(new
    {
        status = "ok",
        productionRedirectUriRegistered = allowedRedirectUris.Contains(
            productionRedirectUri,
            StringComparer.Ordinal),
        registeredRedirectUriCount = allowedRedirectUris.Length,
        registeredRedirectUris = allowedRedirectUris,
        publicBaseUrl = configuration["Auth:PublicBaseUrl"],
        googleClientIdConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Google:ClientId"]),
        microsoftClientIdConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Microsoft:ClientId"]),
        jwtIssuerConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Jwt:Issuer"]),
        jwtAudienceConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Jwt:Audience"]),
        jwtSigningKeyConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Jwt:SigningKey"])
    });
});

app.MapMethods("/health/db", new[] { "GET", "HEAD" }, async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    try
    {
        var connectionString = PostgresConnectionFactory.GetConnectionString(configuration);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Results.Json(
                new
                {
                    status = "unhealthy",
                    database = "not_configured",
                    reason = "Configure Postgres__Host, Postgres__Database, Postgres__Username, and Postgres__Password."
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 5,
            CommandTimeout = 5
        };

        await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("select 1", connection)
        {
            CommandTimeout = 5
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
        app.Logger.LogError(ex, "PostgreSQL health check failed.");

        var errorType = ex.GetType().Name;
        var sqlState = ex is PostgresException postgresException
            ? postgresException.SqlState
            : null;

        return Results.Json(
            new
            {
                status = "unhealthy",
                database = "postgres",
                reason = "connection_failed",
                errorType,
                sqlState,
                errorMessage = "Database connection failed. Check the Render Postgres__* environment variables and Supabase network/password settings."
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapControllers();
app.Run();
