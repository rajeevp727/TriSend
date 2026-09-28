using TriSend.Api.Data;
using TriSend.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<MessageRepository>();

var app = builder.Build();

app.UseMiddleware<MvpApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "trisend-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapControllers();

app.Run();
