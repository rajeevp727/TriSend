using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace TriSend.Api.Services;

public sealed class AuthService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    private NpgsqlConnection CreateConnection() =>
        new(_configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string is not configured."));

    public async Task<string> CreateLoginUrlAsync(string provider, string appId, string role, string redirectUri, CancellationToken ct)
    {
        ValidateProvider(provider);
        ValidateRedirect(appId, redirectUri);

        var state = RandomToken(48);
        var verifier = RandomToken(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            insert into auth_requests(state,provider,role,app_id,redirect_uri,code_verifier,expires_at_utc)
            values(@state,@provider,@role,@app,@redirect,@verifier,@expires)
            """, db);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("provider", provider.ToLowerInvariant());
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("app", appId);
        command.Parameters.AddWithValue("redirect", redirectUri);
        command.Parameters.AddWithValue("verifier", verifier);
        command.Parameters.AddWithValue("expires", DateTimeOffset.UtcNow.AddMinutes(10));
        await command.ExecuteNonQueryAsync(ct);

        var clientId = _configuration[$"Auth:{ProviderName(provider)}:ClientId"]
            ?? throw Missing($"Auth:{ProviderName(provider)}:ClientId");
        var callbackBase = (_configuration["Auth:PublicBaseUrl"] ?? "").TrimEnd('/');
        if (!Uri.TryCreate(callbackBase, UriKind.Absolute, out _))
            throw Missing("Auth:PublicBaseUrl");
        var oauthRedirectUri = $"{callbackBase}/auth/{provider.ToLowerInvariant()}/callback";

        var authorizationEndpoint = provider.ToLowerInvariant() == "google"
            ? "https://accounts.google.com/o/oauth2/v2/auth"
            : "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";

        var scope = provider.ToLowerInvariant() == "google"
            ? "openid email profile"
            : "openid profile email User.Read";

        return authorizationEndpoint
            + "?client_id=" + Uri.EscapeDataString(clientId)
            + "&redirect_uri=" + Uri.EscapeDataString(oauthRedirectUri)
            + "&response_type=code"
            + "&scope=" + Uri.EscapeDataString(scope)
            + "&state=" + Uri.EscapeDataString(state)
            + "&code_challenge=" + Uri.EscapeDataString(challenge)
            + "&code_challenge_method=S256";
    }

    public async Task<string> CompleteOAuthAsync(string provider, string code, string state, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        await using var request = new NpgsqlCommand("""
            select provider,role,app_id,redirect_uri,code_verifier
            from auth_requests
            where state=@state and expires_at_utc > now()
            """, db);
        request.Parameters.AddWithValue("state", state);
        await using var reader = await request.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new AuthException("invalid_state", "The sign-in request is invalid or expired.", 400);

        var storedProvider = reader.GetString(0);
        var role = reader.GetString(1);
        var appId = reader.GetString(2);
        var redirectUri = reader.GetString(3);
        var verifier = reader.GetString(4);
        await reader.CloseAsync();

        if (!string.Equals(provider, storedProvider, StringComparison.OrdinalIgnoreCase))
            throw new AuthException("invalid_state", "The sign-in provider does not match.", 400);

        var callbackBase = (_configuration["Auth:PublicBaseUrl"] ?? "").TrimEnd('/');
        var oauthRedirectUri = $"{callbackBase}/auth/{provider.ToLowerInvariant()}/callback";
        var accessToken = await ExchangeProviderCodeAsync(provider, code, oauthRedirectUri, verifier, ct);
        var profile = await GetProfileAsync(provider, accessToken, ct);
        var userId = await UpsertUserAsync(db, profile, provider, ct);
        var effectiveRole = await GetOrCreateAppRoleAsync(db, userId, appId, role, ct);

        await using var deleteRequest = new NpgsqlCommand("delete from auth_requests where state=@state", db);
        deleteRequest.Parameters.AddWithValue("state", state);
        await deleteRequest.ExecuteNonQueryAsync(ct);

        var loginCode = RandomToken(48);
        await using var ticket = new NpgsqlCommand("""
            insert into auth_login_tickets(code,user_id,app_id,role,expires_at_utc)
            values(@code,@user,@app,@role,@expires)
            """, db);
        ticket.Parameters.AddWithValue("code", loginCode);
        ticket.Parameters.AddWithValue("user", userId);
        ticket.Parameters.AddWithValue("app", appId);
        ticket.Parameters.AddWithValue("role", effectiveRole);
        ticket.Parameters.AddWithValue("expires", DateTimeOffset.UtcNow.AddMinutes(2));
        await ticket.ExecuteNonQueryAsync(ct);

        return AppendQuery(redirectUri, "auth_code", loginCode);
    }

    public async Task<AuthTokenResponse> ExchangeCodeAsync(string code, bool replaceOldest, string? userAgent, string? ipAddress, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        await using var ticket = new NpgsqlCommand("""
            select user_id,app_id,role
            from auth_login_tickets
            where code=@code and expires_at_utc > now() and consumed_at_utc is null
            """, db);
        ticket.Parameters.AddWithValue("code", code);
        await using var reader = await ticket.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new AuthException("invalid_code", "The authentication code is invalid or expired.", 400);

        var userId = reader.GetGuid(0);
        var appId = reader.GetString(1);
        var role = reader.GetString(2);
        await reader.CloseAsync();

        var activeCount = await ActiveSessionCountAsync(db, userId, ct);
        if (activeCount >= 3)
        {
            if (!replaceOldest)
                throw new AuthException("MAX_SESSIONS", "You already have 3 active sessions. Confirm replacement of the oldest session to continue.", 409, true);

            await using var revoke = new NpgsqlCommand("""
                update auth_sessions set revoked_at_utc=now()
                where id = (
                    select id from auth_sessions
                    where user_id=@user and revoked_at_utc is null and expires_at_utc > now()
                    order by last_used_at_utc asc limit 1
                )
                """, db);
            revoke.Parameters.AddWithValue("user", userId);
            await revoke.ExecuteNonQueryAsync(ct);
        }

        await using var consume = new NpgsqlCommand("update auth_login_tickets set consumed_at_utc=now() where code=@code", db);
        consume.Parameters.AddWithValue("code", code);
        await consume.ExecuteNonQueryAsync(ct);

        var refreshToken = RandomToken(64);
        var sessionId = Guid.NewGuid();

        await using var insert = new NpgsqlCommand("""
            insert into auth_sessions(id,user_id,app_id,role,refresh_token_hash,expires_at_utc,user_agent,ip_address)
            values(@id,@user,@app,@role,@hash,@expires,@ua,@ip)
            """, db);
        insert.Parameters.AddWithValue("id", sessionId);
        insert.Parameters.AddWithValue("user", userId);
        insert.Parameters.AddWithValue("app", appId);
        insert.Parameters.AddWithValue("role", role);
        insert.Parameters.AddWithValue("hash", Hash(refreshToken));
        insert.Parameters.AddWithValue("expires", DateTimeOffset.UtcNow.AddDays(30));
        insert.Parameters.AddWithValue("ua", (object?)userAgent ?? DBNull.Value);
        insert.Parameters.AddWithValue("ip", (object?)ipAddress ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(ct);

        return await CreateTokenResponseAsync(db, userId, appId, role, sessionId, refreshToken, ct);
    }

    public async Task<AuthTokenResponse> RegisterWithPasswordAsync(
        string email,
        string password,
        string? displayName,
        string appId,
        string role,
        bool replaceOldest,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct)
    {
        ValidateAppRole(role);
        if (string.IsNullOrWhiteSpace(appId))
            throw new AuthException("invalid_app", "Application id is required.", 400);

        var normalizedEmail = NormalizeEmail(email);
        ValidatePassword(password);

        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var transaction = await db.BeginTransactionAsync(ct);

        await using var existing = new NpgsqlCommand("select id from auth_users where email=@email", db, transaction);
        existing.Parameters.AddWithValue("email", normalizedEmail);
        var existingId = await existing.ExecuteScalarAsync(ct);
        if (existingId is not null)
            throw new AuthException("email_exists", "An account already exists for this email address. Sign in instead.", 409);

        var userId = Guid.NewGuid();
        await using var user = new NpgsqlCommand("""
            insert into auth_users(id,email,display_name)
            values(@id,@email,@name)
            """, db, transaction);
        user.Parameters.AddWithValue("id", userId);
        user.Parameters.AddWithValue("email", normalizedEmail);
        user.Parameters.AddWithValue("name", (object?)displayName?.Trim() ?? DBNull.Value);
        await user.ExecuteNonQueryAsync(ct);

        const int iterations = 120000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

        await using var credentials = new NpgsqlCommand("""
            insert into auth_password_credentials(user_id,password_hash,password_salt,iterations)
            values(@user,@hash,@salt,@iterations)
            """, db, transaction);
        credentials.Parameters.AddWithValue("user", userId);
        credentials.Parameters.AddWithValue("hash", Convert.ToBase64String(hash));
        credentials.Parameters.AddWithValue("salt", Convert.ToBase64String(salt));
        credentials.Parameters.AddWithValue("iterations", iterations);
        await credentials.ExecuteNonQueryAsync(ct);

        await using var appRole = new NpgsqlCommand("""
            insert into auth_user_roles(user_id,app_id,role)
            values(@user,@app,@role)
            """, db, transaction);
        appRole.Parameters.AddWithValue("user", userId);
        appRole.Parameters.AddWithValue("app", appId);
        appRole.Parameters.AddWithValue("role", role);
        await appRole.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
        return await CreateSessionAsync(db, userId, appId, role, replaceOldest, userAgent, ipAddress, ct);
    }

    public async Task<AuthTokenResponse> LoginWithPasswordAsync(
        string email,
        string password,
        string appId,
        bool replaceOldest,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new AuthException("invalid_app", "Application id is required.", 400);

        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(password))
            throw new AuthException("invalid_credentials", "Email or password is incorrect.", 401);

        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        await using var command = new NpgsqlCommand("""
            select u.id, pc.password_hash, pc.password_salt, pc.iterations, r.role
            from auth_users u
            left join auth_password_credentials pc on pc.user_id=u.id
            left join auth_user_roles r on r.user_id=u.id and r.app_id=@app
            where u.email=@email
            """, db);
        command.Parameters.AddWithValue("email", normalizedEmail);
        command.Parameters.AddWithValue("app", appId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) ||
            reader.IsDBNull(1) ||
            reader.IsDBNull(2) ||
            reader.IsDBNull(4))
            throw new AuthException("invalid_credentials", "Email or password is incorrect.", 401);

        var userId = reader.GetGuid(0);
        var storedHash = Convert.FromBase64String(reader.GetString(1));
        var salt = Convert.FromBase64String(reader.GetString(2));
        var iterations = reader.GetInt32(3);
        var role = reader.GetString(4);
        await reader.CloseAsync();

        var computed = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, storedHash.Length);
        if (!CryptographicOperations.FixedTimeEquals(computed, storedHash))
            throw new AuthException("invalid_credentials", "Email or password is incorrect.", 401);

        return await CreateSessionAsync(db, userId, appId, role, replaceOldest, userAgent, ipAddress, ct);
    }

    public async Task<AuthTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);

        await using var command = new NpgsqlCommand("""
            select id,user_id,app_id,role
            from auth_sessions
            where refresh_token_hash=@hash and revoked_at_utc is null and expires_at_utc > now()
            """, db);
        command.Parameters.AddWithValue("hash", Hash(refreshToken));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new AuthException("invalid_refresh_token", "Refresh token is invalid or expired.", 401);

        var sessionId = reader.GetGuid(0);
        var userId = reader.GetGuid(1);
        var appId = reader.GetString(2);
        var role = reader.GetString(3);
        await reader.CloseAsync();

        var nextRefresh = RandomToken(64);
        await using var rotate = new NpgsqlCommand("""
            update auth_sessions
            set refresh_token_hash=@next,last_used_at_utc=now()
            where id=@id
            """, db);
        rotate.Parameters.AddWithValue("next", Hash(nextRefresh));
        rotate.Parameters.AddWithValue("id", sessionId);
        await rotate.ExecuteNonQueryAsync(ct);

        return await CreateTokenResponseAsync(db, userId, appId, role, sessionId, nextRefresh, ct);
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("update auth_sessions set revoked_at_utc=now() where id=@id and user_id=@user", db);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task LogoutAllAsync(Guid userId, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("update auth_sessions set revoked_at_utc=now() where user_id=@user and revoked_at_utc is null", db);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<object>> SessionsAsync(Guid userId, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            select id,app_id,role,created_at_utc,last_used_at_utc,expires_at_utc,user_agent
            from auth_sessions
            where user_id=@user and revoked_at_utc is null and expires_at_utc > now()
            order by last_used_at_utc desc
            """, db);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<object>();
        while (await reader.ReadAsync(ct))
            result.Add(new
            {
                id = reader.GetGuid(0),
                appId = reader.GetString(1),
                role = reader.GetString(2),
                createdAtUtc = reader.GetDateTime(3),
                lastUsedAtUtc = reader.GetDateTime(4),
                expiresAtUtc = reader.GetDateTime(5),
                userAgent = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        return result;
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand("update auth_sessions set revoked_at_utc=now() where id=@id and user_id=@user", db);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<AuthTokenResponse> CreateSessionAsync(
        NpgsqlConnection db,
        Guid userId,
        string appId,
        string role,
        bool replaceOldest,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct)
    {
        var activeCount = await ActiveSessionCountAsync(db, userId, ct);
        if (activeCount >= 3)
        {
            if (!replaceOldest)
                throw new AuthException("MAX_SESSIONS", "You already have 3 active sessions. Confirm replacement of the oldest session to continue.", 409, true);

            await using var revoke = new NpgsqlCommand("""
                update auth_sessions set revoked_at_utc=now()
                where id = (
                    select id from auth_sessions
                    where user_id=@user and revoked_at_utc is null and expires_at_utc > now()
                    order by last_used_at_utc asc limit 1
                )
                """, db);
            revoke.Parameters.AddWithValue("user", userId);
            await revoke.ExecuteNonQueryAsync(ct);
        }

        var refreshToken = RandomToken(64);
        var sessionId = Guid.NewGuid();

        await using var insert = new NpgsqlCommand("""
            insert into auth_sessions(id,user_id,app_id,role,refresh_token_hash,expires_at_utc,user_agent,ip_address)
            values(@id,@user,@app,@role,@hash,@expires,@ua,@ip)
            """, db);
        insert.Parameters.AddWithValue("id", sessionId);
        insert.Parameters.AddWithValue("user", userId);
        insert.Parameters.AddWithValue("app", appId);
        insert.Parameters.AddWithValue("role", role);
        insert.Parameters.AddWithValue("hash", Hash(refreshToken));
        insert.Parameters.AddWithValue("expires", DateTimeOffset.UtcNow.AddDays(30));
        insert.Parameters.AddWithValue("ua", (object?)userAgent ?? DBNull.Value);
        insert.Parameters.AddWithValue("ip", (object?)ipAddress ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(ct);

        return await CreateTokenResponseAsync(db, userId, appId, role, sessionId, refreshToken, ct);
    }

    private async Task<string> GetOrCreateAppRoleAsync(
        NpgsqlConnection db,
        Guid userId,
        string appId,
        string requestedRole,
        CancellationToken ct)
    {
        ValidateAppRole(requestedRole);

        await using var existing = new NpgsqlCommand("""
            select role from auth_user_roles where user_id=@user and app_id=@app
            """, db);
        existing.Parameters.AddWithValue("user", userId);
        existing.Parameters.AddWithValue("app", appId);
        var role = await existing.ExecuteScalarAsync(ct);
        if (role is string storedRole)
            return storedRole;

        await using var insert = new NpgsqlCommand("""
            insert into auth_user_roles(user_id,app_id,role)
            values(@user,@app,@role)
            on conflict(user_id,app_id) do nothing
            """, db);
        insert.Parameters.AddWithValue("user", userId);
        insert.Parameters.AddWithValue("app", appId);
        insert.Parameters.AddWithValue("role", requestedRole);
        await insert.ExecuteNonQueryAsync(ct);
        return requestedRole;
    }

    private static void ValidateAppRole(string role)
    {
        if (role is not ("JobSeeker" or "Employer"))
            throw new AuthException("invalid_role", "Choose Employee or Employer.", 400);
    }

    private static string NormalizeEmail(string email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < 8 ||
            !password.Any(char.IsLetter) ||
            !password.Any(char.IsDigit))
            throw new AuthException("weak_password", "Password must be at least 8 characters and contain at least one letter and one number.", 400);
    }

    private async Task<int> ActiveSessionCountAsync(NpgsqlConnection db, Guid userId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("select count(*) from auth_sessions where user_id=@user and revoked_at_utc is null and expires_at_utc > now()", db);
        command.Parameters.AddWithValue("user", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private async Task<Guid> UpsertUserAsync(NpgsqlConnection db, Profile profile, string provider, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            insert into auth_users(id,email,display_name,avatar_url)
            values(@id,@email,@name,@avatar)
            on conflict(email) do update set display_name=excluded.display_name,avatar_url=excluded.avatar_url,updated_at_utc=now()
            returning id
            """, db);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("email", profile.Email);
        command.Parameters.AddWithValue("name", (object?)profile.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("avatar", (object?)profile.AvatarUrl ?? DBNull.Value);
        var userId = (Guid)(await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Unable to create user."));

        await using var identity = new NpgsqlCommand("""
            insert into auth_identities(id,user_id,provider,provider_subject,email)
            values(@id,@user,@provider,@subject,@email)
            on conflict(provider,provider_subject) do update set user_id=excluded.user_id,email=excluded.email
            """, db);
        identity.Parameters.AddWithValue("id", Guid.NewGuid());
        identity.Parameters.AddWithValue("user", userId);
        identity.Parameters.AddWithValue("provider", provider.ToLowerInvariant());
        identity.Parameters.AddWithValue("subject", profile.Subject);
        identity.Parameters.AddWithValue("email", profile.Email);
        await identity.ExecuteNonQueryAsync(ct);
        return userId;
    }

    private async Task<AuthTokenResponse> CreateTokenResponseAsync(NpgsqlConnection db, Guid userId, string appId, string role, Guid sessionId, string refreshToken, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("select email,display_name,avatar_url from auth_users where id=@id", db);
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new AuthException("user_not_found", "User not found.", 401);
        var email = reader.GetString(0);
        var name = reader.IsDBNull(1) ? null : reader.GetString(1);
        var avatar = reader.IsDBNull(2) ? null : reader.GetString(2);
        await reader.CloseAsync();

        var issuer = _configuration["Auth:Jwt:Issuer"] ?? throw Missing("Auth:Jwt:Issuer");
        var audience = _configuration["Auth:Jwt:Audience"] ?? throw Missing("Auth:Jwt:Audience");
        var signingKey = _configuration["Auth:Jwt:SigningKey"] ?? throw Missing("Auth:Jwt:SigningKey");
        if (signingKey.Length < 32) throw new InvalidOperationException("Auth:Jwt:SigningKey must be at least 32 characters.");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("name", name ?? string.Empty),
            new Claim("app", appId),
            new Claim("role", role),
            new Claim("sid", sessionId.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer, audience, claims, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(15), credentials);

        return new AuthTokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            refreshToken,
            sessionId,
            DateTimeOffset.UtcNow.AddMinutes(15),
            new { id = userId, email, name, avatarUrl = avatar, role, appId });
    }

    private async Task<string> ExchangeProviderCodeAsync(string provider, string code, string redirectUri, string verifier, CancellationToken ct)
    {
        var providerName = ProviderName(provider);
        var clientId = _configuration[$"Auth:{providerName}:ClientId"] ?? throw Missing($"Auth:{providerName}:ClientId");
        var clientSecret = _configuration[$"Auth:{providerName}:ClientSecret"] ?? throw Missing($"Auth:{providerName}:ClientSecret");
        var endpoint = provider.ToLowerInvariant() == "google"
            ? "https://oauth2.googleapis.com/token"
            : "https://login.microsoftonline.com/common/oauth2/v2.0/token";

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier
        };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
            throw new AuthException("oauth_token_exchange_failed", "The OAuth provider rejected the authorization code.", 502);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("access_token").GetString()
            ?? throw new AuthException("oauth_token_missing", "The OAuth provider did not return an access token.", 502);
    }

    private async Task<Profile> GetProfileAsync(string provider, string accessToken, CancellationToken ct)
    {
        var endpoint = provider.ToLowerInvariant() == "google"
            ? "https://openidconnect.googleapis.com/v1/userinfo"
            : "https://graph.microsoft.com/oidc/userinfo";

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new AuthException("oauth_profile_failed", "Unable to retrieve the OAuth profile.", 502);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        var subject = root.GetProperty("sub").GetString()
            ?? throw new AuthException("oauth_profile_invalid", "OAuth profile is missing a subject.", 502);
        var email = root.TryGetProperty("email", out var emailValue) ? emailValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(email))
            throw new AuthException("oauth_email_required", "Your OAuth account did not provide an email address.", 400);

        var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        var picture = root.TryGetProperty("picture", out var pictureValue) ? pictureValue.GetString() : null;
        return new Profile(subject, email, name, picture);
    }

    private void ValidateProvider(string provider)
    {
        if (provider is not ("google" or "microsoft"))
            throw new AuthException("unsupported_provider", "Only Google and Microsoft are supported.", 400);
    }

    private void ValidateRedirect(string appId, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new AuthException("invalid_app", "Application id is required.", 400);

        var allowed = _configuration.GetSection("Auth:AllowedRedirectUris").Get<string[]>() ?? [];
        if (!allowed.Contains(redirectUri, StringComparer.Ordinal))
            throw new AuthException("invalid_redirect_uri", "The redirect URI is not registered.", 400);
    }

    private static string ProviderName(string provider) =>
        provider.ToLowerInvariant() == "google" ? "Google" : "Microsoft";

    private static string RandomToken(int bytes) =>
        Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(value)));

    private static string AppendQuery(string uri, string key, string value) =>
        uri + (uri.Contains('?') ? "&" : "?") + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);

    private static InvalidOperationException Missing(string key) =>
        new($"Required configuration '{key}' is missing.");

    private sealed record Profile(string Subject, string Email, string? Name, string? AvatarUrl);
}

public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    object User);

public sealed class AuthException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }
    public bool MaxSessions { get; }

    public AuthException(string code, string message, int statusCode, bool maxSessions = false)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        MaxSessions = maxSessions;
    }
}
