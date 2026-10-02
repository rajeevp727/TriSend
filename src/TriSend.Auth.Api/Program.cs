using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using TriSend.Auth.Application;
using TriSend.Auth.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddControllers();

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseSqlServer(config.GetConnectionString("Identity"));
    options.UseOpenIddict();
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISessionService, SessionService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "__Host-trisend-auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/login";
})
.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
{
    options.ClientId = config["Google:ClientId"] ?? "";
    options.ClientSecret = config["Google:ClientSecret"] ?? "";
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.SaveTokens = false;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.Events.OnCreatingTicket = async context =>
    {
        var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
        var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionService>();
        var subject = context.User.FindFirst("sub")?.Value
            ?? throw new SecurityTokenValidationException("Google subject missing.");
        var email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? context.User.FindFirst("email")?.Value
            ?? throw new SecurityTokenValidationException("Google email missing.");

        var user = await userService.FindByExternalIdentityAsync("google", subject, context.HttpContext.RequestAborted)
                   ?? await userService.CreateFromExternalIdentityAsync(
                       "google", subject, email,
                       context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
                       context.User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value,
                       context.User.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value,
                       context.User.FindFirst("picture")?.Value,
                       true, context.HttpContext.RequestAborted);

        await sessions.CreateAsync(
            user.Id, context.Properties.Items.TryGetValue("client_id", out var clientId) ? clientId : null,
            null, null, context.HttpContext.Connection.RemoteIpAddress?.ToString(),
            context.HttpContext.Request.Headers.UserAgent.ToString(), context.HttpContext.RequestAborted);

        context.Identity!.AddClaim(new System.Security.Claims.Claim("trisend_user_id", user.Id.ToString()));
    };
})
.AddOpenIdConnect("Microsoft", options =>
{
    options.Authority = $"https://login.microsoftonline.com/{config["Microsoft:Tenant"] ?? "common"}/v2.0";
    options.ClientId = config["Microsoft:ClientId"] ?? "";
    options.ClientSecret = config["Microsoft:ClientSecret"] ?? "";
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.ResponseType = "code";
    options.UsePkce = true;
    options.SaveTokens = false;
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.GetClaimsFromUserInfoEndpoint = true;
    options.Events.OnTokenValidated = async context =>
    {
        var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
        var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionService>();
        var subject = context.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? context.Principal?.FindFirst("sub")?.Value
            ?? throw new SecurityTokenValidationException("Microsoft subject missing.");
        var email = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? context.Principal?.FindFirst("preferred_username")?.Value
            ?? throw new SecurityTokenValidationException("Microsoft email missing.");

        var user = await userService.FindByExternalIdentityAsync("microsoft", subject, context.HttpContext.RequestAborted)
                   ?? await userService.CreateFromExternalIdentityAsync(
                       "microsoft", subject, email,
                       context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
                       context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value,
                       context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value,
                       null, true, context.HttpContext.RequestAborted);

        await sessions.CreateAsync(
            user.Id, null, null, null, context.HttpContext.Connection.RemoteIpAddress?.ToString(),
            context.HttpContext.Request.Headers.UserAgent.ToString(), context.HttpContext.RequestAborted);

        var identity = (System.Security.Claims.ClaimsIdentity)context.Principal!.Identity!;
        identity.AddClaim(new System.Security.Claims.Claim("trisend_user_id", user.Id.ToString()));
    };
});

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<AuthDbContext>();
    })
    .AddServer(options =>
    {
        options.SetIssuer(new Uri(config["Authentication:Issuer"] ?? "https://auth.trisend.com"));
        options.SetAuthorizationEndpointUris("/oauth/authorize");
        options.SetTokenEndpointUris("/oauth/token");
        options.SetUserinfoEndpointUris("/userinfo");
        options.SetEndSessionEndpointUris("/oauth/logout");

        options.AllowAuthorizationCodeFlow();
        options.AllowRefreshTokenFlow();
        options.RequireProofKeyForCodeExchange();

        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(
            config.GetValue("Authentication:AccessTokenLifetimeMinutes", 10)));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(
            config.GetValue("Authentication:RefreshTokenLifetimeDays", 30)));

        options.DisableAccessTokenEncryption();

        options.AddDevelopmentEncryptionCertificate();
        options.AddDevelopmentSigningCertificate();

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableUserinfoEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("Identity", policy =>
    {
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("Identity");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "trisend-identity" }));

await IdentitySeeder.SeedAsync(app.Services, config);

app.Run();