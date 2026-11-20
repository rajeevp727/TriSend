using Azure.Identity;
using Npgsql;
using TriSend.Api.Data;
using TriSend.Api.Email;
using TriSend.Api.Security;
using TriSend.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Load secrets from Azure Key Vault when a vault URI is configured.
// Locally, DefaultAzureCredential uses the developer's Azure CLI sign-in.
var keyVaultUri = builder.Configuration["KeyVault:VaultUri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}

// Keep compatibility with the Key Vault secret names already created.
// Key Vault converts "--" to ":", so Microsoft--ClientId becomes Microsoft:ClientId.
CopyKeyVaultAlias(builder.Configuration, "Microsoft:ClientId", "Auth:Microsoft:ClientId");
CopyKeyVaultAlias(builder.Configuration, "Microsoft:ClientSecret", "Auth:Microsoft:ClientSecret");
CopyKeyVaultAlias(builder.Configuration, "PublicBaseUrl", "Auth:PublicBaseUrl");

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
                    reason = "Configure ConnectionStrings:Postgres in Azure Key Vault or Postgres settings."
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
                errorMessage = "Database connection failed. Check the ConnectionStrings:Postgres Key Vault secret and Supabase network/password settings."
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapControllers();
app.Run();

static void CopyKeyVaultAlias(IConfigurationManager configuration, string sourceKey, string targetKey)
{
    if (string.IsNullOrWhiteSpace(configuration[targetKey]) &&
        !string.IsNullOrWhiteSpace(configuration[sourceKey]))
    {
        configuration[targetKey] = configuration[sourceKey];
    }
}