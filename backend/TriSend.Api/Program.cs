using Azure.Identity;
using Npgsql;
using TriSend.Api.Data;
using TriSend.Api.Email;
using TriSend.Api.Security;
using TriSend.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Keep non-secret vault location in appsettings; credentials come from the
// developer's Azure CLI session locally or Managed Identity when hosted.
var keyVaultUriValue = builder.Configuration["KeyVault:VaultUri"];
if (!string.IsNullOrWhiteSpace(keyVaultUriValue))
{
    if (!Uri.TryCreate(keyVaultUriValue, UriKind.Absolute, out var keyVaultUri) ||
        keyVaultUri.Scheme != Uri.UriSchemeHttps)
    {
        throw new InvalidOperationException("KeyVault:VaultUri must be a valid HTTPS URI.");
    }

    builder.Configuration.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential());
}

// Key Vault replaces "--" with ":" in configuration keys. Preserve the
// existing Microsoft--* and PublicBaseUrl names without duplicating secrets.
CopyKeyVaultAlias(builder.Configuration, "Microsoft:ClientId", "Auth:Microsoft:ClientId");
CopyKeyVaultAlias(builder.Configuration, "Microsoft:ClientSecret", "Auth:Microsoft:ClientSecret");
CopyKeyVaultAlias(builder.Configuration, "PublicBaseUrl", "Auth:PublicBaseUrl");

builder.Services.AddControllers();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
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

// Bound outbound calls so a slow OAuth/email provider cannot hold requests
// indefinitely. Resend gets its own typed client and the same bounded timeout.
builder.Services.AddHttpClient(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient<ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
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
    var allowedRedirectUris = configuration.GetSection("Auth:AllowedRedirectUris").Get<string[]>() ?? [];
    const string productionRedirectUri = "https://248works.rajeevstech.in/";

    return Results.Ok(new
    {
        status = "ok",
        productionRedirectUriRegistered = allowedRedirectUris.Contains(productionRedirectUri, StringComparer.Ordinal),
        registeredRedirectUriCount = allowedRedirectUris.Length,
        publicBaseUrlConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:PublicBaseUrl"]),
        googleClientIdConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Google:ClientId"]),
        googleClientSecretConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Google:ClientSecret"]),
        microsoftClientIdConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Microsoft:ClientId"]),
        microsoftClientSecretConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Microsoft:ClientSecret"]),
        jwtIssuerConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Jwt:Issuer"]),
        jwtAudienceConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Jwt:Audience"]),
        jwtSigningKeyConfigured = !string.IsNullOrWhiteSpace(configuration["Auth:Jwt:SigningKey"]),
        resendApiKeyConfigured = !string.IsNullOrWhiteSpace(configuration["Resend:ApiKey"]),
        mvpApiKeyConfigured = !string.IsNullOrWhiteSpace(configuration["Mvp:ApiKey"])
    });
});

app.MapMethods("/health/db", new[] { "GET", "HEAD" }, async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    try
    {
        var connectionString = PostgresConnectionFactory.GetConnectionString(configuration);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Results.Json(new
            {
                status = "unhealthy",
                database = "not_configured",
                reason = "Configure ConnectionStrings:Postgres in Azure Key Vault."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 5,
            CommandTimeout = 5
        };

        await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select 1", connection) { CommandTimeout = 5 };
        await command.ExecuteScalarAsync(cancellationToken);

        return Results.Ok(new { status = "healthy", database = "postgres", utc = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "PostgreSQL health check failed.");
        return Results.Json(new
        {
            status = "unhealthy",
            database = "postgres",
            reason = "connection_failed",
            errorType = ex.GetType().Name,
            sqlState = (ex as PostgresException)?.SqlState,
            errorMessage = "Database connection failed. Check the ConnectionStrings:Postgres secret and Supabase network/password settings."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
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