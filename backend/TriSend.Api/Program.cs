using TriSend.Api.Data;
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

var app = builder.Build();

app.UseCors("Frontend");
app.UseMiddleware<MvpApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "trisend-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapControllers();

app.Run();
