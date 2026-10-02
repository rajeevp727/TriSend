using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using TriSend.Auth.Api;
using TriSend.Auth.Application;
using TriSend.Auth.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseSqlServer(config.GetConnectionString("Identity"));
    options.UseOpenIddict();
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton<CertificateLoader>();
builder.Services.AddSingleton<IGoogleIdentityProvider, GoogleIdentityProvider>();
builder.Services.AddSingleton<IMicrosoftIdentityProvider, MicrosoftIdentityProvider>();
builder.Services.AddDataProtection().SetApplicationName("TriSend.Identity");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "__Host-trisend-auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/login";
})
.AddCookie("External", options =>
{
    options.Cookie.Name = "__Host-trisend-external";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
})
.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
{
    options.ClientId = config["Authentication:Google:ClientId"]
        ?? throw new InvalidOperationException("Google client ID is not configured.");
    options.ClientSecret = config["Authentication:Google:ClientSecret"]
        ?? throw new InvalidOperationException("Google client secret is not configured.");
    options.SignInScheme = "External";
    options.SaveTokens = false;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
})
.AddOpenIdConnect("Microsoft", options =>
{
    options.Authority = $"https://login.microsoftonline.com/{config["Authentication:Microsoft:Tenant"] ?? "common"}/v2.0";
    options.ClientId = config["Authentication:Microsoft:ClientId"]
        ?? throw new InvalidOperationException("Microsoft client ID is not configured.");
    options.ClientSecret = config["Authentication:Microsoft:ClientSecret"]
        ?? throw new InvalidOperationException("Microsoft client secret is not configured.");
    options.SignInScheme = "External";
    options.ResponseType = "code";
    options.UsePkce = true;
    options.SaveTokens = false;
    options.MapInboundClaims = false;
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
});

builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>())
    .AddServer(options =>
    {
        options.SetIssuer(new Uri(config["Authentication:Issuer"]
            ?? throw new InvalidOperationException("Authentication:Issuer is required.")));
        options.SetAuthorizationEndpointUris("oauth/authorize");
        options.SetTokenEndpointUris("oauth/token");
        options.SetUserInfoEndpointUris("userinfo");
        options.SetEndSessionEndpointUris("oauth/logout");

        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow()
            .RequireProofKeyForCodeExchange();

        options.RegisterResources("greenpantry-api", "omegatech-api", "sprintdeck", "248works");
        options.RegisterAudiences("greenpantry-api", "omegatech-api", "sprintdeck", "248works");
        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess);

        options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5));
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(
            config.GetValue("Authentication:AccessTokenLifetimeMinutes", 10)));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(
            config.GetValue("Authentication:RefreshTokenLifetimeDays", 30)));

        options.DisableAccessTokenEncryption();
        options.UseDataProtection();

        var loader = new CertificateLoader(config);
        var signing = loader.LoadAsync("Authentication:SigningCertificateSecretNames", CancellationToken.None)
            .GetAwaiter().GetResult();
        var encryption = loader.LoadAsync("Authentication:EncryptionCertificateSecretNames", CancellationToken.None)
            .GetAwaiter().GetResult();

        if (signing.Count > 0 && encryption.Count > 0)
        {
            foreach (var certificate in signing) options.AddSigningCertificate(certificate);
            foreach (var certificate in encryption) options.AddEncryptionCertificate(certificate);
        }
        else if (builder.Environment.IsDevelopment() && config.GetValue("Authentication:AllowDevelopmentCertificates", true))
        {
            options.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();
        }
        else
        {
            throw new InvalidOperationException("Production signing/encryption certificates are not configured.");
        }

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.SetIssuer(config["Authentication:Issuer"]!);
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddCors(options =>
{
    var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy("Identity", policy => policy.WithOrigins(origins)
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("oauth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});

builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var value in config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        if (IPAddress.TryParse(value, out var ip)) options.KnownProxies.Add(ip);
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("Identity");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

await IdentitySeeder.SeedAsync(app.Services, config, CancellationToken.None);
app.Run();
