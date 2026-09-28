using Npgsql;
using TriSend.Api.Data;
using TriSend.Api.Email;
using TriSend.Api.Security;

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
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
            return;
        }

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<MessageRepository>();
builder.Services.AddSingleton<BrandedEmailTemplate>();
builder.Services.AddHttpClient<ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
});

var app = builder.Build();

app.UseCors("Frontend");
app.UseMiddleware<MvpApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "trisend-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/health/db", async (IConfiguration configuration, CancellationToken cancellationToken) =>
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
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("select 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "healthy",
            database = "postgres",
            utc = DateTimeOffset.UtcNow
        });
    }
    catch
    {
        return Results.Json(
            new { status = "unhealthy", database = "postgres" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapControllers();

app.Run();
