using System.Data;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using TriSend.Api.Data;
using TriSend.Api.Email;
namespace TriSend.Api.Services;
public sealed class EmailOtpService(IConfiguration configuration, ResendEmailSender emailSender)
{
    private const string Role = "BullionIQ";
    private const int OtpLifetimeMinutes = 10, MaxAttempts = 5, MaxRequestsPerWindow = 3, WindowMinutes = 15;
    private NpgsqlConnection CreateConnection() => new(PostgresConnectionFactory.GetConnectionString(configuration) ?? throw new InvalidOperationException("Postgres database configuration is required."));
    public async Task RequestAsync(string email, string appId, string? ipAddress, CancellationToken ct)
    {
        var normalizedEmail=NormalizeEmail(email); ValidateEmail(normalizedEmail); ValidateApp(appId);
        var pepper=configuration["Auth:EmailOtp:HashPepper"];
        if(string.IsNullOrWhiteSpace(pepper)||pepper.Length<32) throw new InvalidOperationException("Auth:EmailOtp:HashPepper must be configured with at least 32 characters.");
        var otp=RandomNumberGenerator.GetInt32(0,1_000_000).ToString("D6"); var otpHash=HashOtp(pepper,normalizedEmail,appId,otp); var otpId=Guid.NewGuid();
        await using(var db=CreateConnection()){await db.OpenAsync(ct); await using var tx=await db.BeginTransactionAsync(ct);
            await using(var advisory=new NpgsqlCommand("select pg_advisory_xact_lock(hashtextextended(@key,0))",db,tx)){advisory.Parameters.AddWithValue("key",$"{appId}:{normalizedEmail}");await advisory.ExecuteNonQueryAsync(ct);}
            await using(var count=new NpgsqlCommand("select count(*) from auth_email_otps where app_id=@app and email=@email and created_at_utc>now()-make_interval(mins=>@window)",db,tx)){count.Parameters.AddWithValue("app",appId);count.Parameters.AddWithValue("email",normalizedEmail);count.Parameters.AddWithValue("window",WindowMinutes);if(Convert.ToInt32(await count.ExecuteScalarAsync(ct))>=MaxRequestsPerWindow)throw new AuthException("otp_rate_limited","Too many code requests. Please wait before trying again.",429);}
            await using(var supersede=new NpgsqlCommand("update auth_email_otps set consumed_at_utc=now() where app_id=@app and email=@email and consumed_at_utc is null",db,tx)){supersede.Parameters.AddWithValue("app",appId);supersede.Parameters.AddWithValue("email",normalizedEmail);await supersede.ExecuteNonQueryAsync(ct);}
            await using(var insert=new NpgsqlCommand("insert into auth_email_otps(id,app_id,email,code_hash,expires_at_utc,request_ip) values(@id,@app,@email,@hash,@expires,@ip)",db,tx)){insert.Parameters.AddWithValue("id",otpId);insert.Parameters.AddWithValue("app",appId);insert.Parameters.AddWithValue("email",normalizedEmail);insert.Parameters.AddWithValue("hash",otpHash);insert.Parameters.AddWithValue("expires",DateTimeOffset.UtcNow.AddMinutes(OtpLifetimeMinutes));insert.Parameters.AddWithValue("ip",(object?)ipAddress??DBNull.Value);await insert.ExecuteNonQueryAsync(ct);} await tx.CommitAsync(ct);}
        try{await emailSender.SendAsync($"auth-email-otp-{otpId:N}",normalizedEmail,"Your BullionIQ verification code",$"Your BullionIQ verification code is {otp}. It expires in {OtpLifetimeMinutes} minutes. If you did not request this code, you can ignore this email.",ct);}
        catch{await using var db=CreateConnection();await db.OpenAsync(ct);await using var invalidate=new NpgsqlCommand("update auth_email_otps set consumed_at_utc=now() where id=@id and consumed_at_utc is null",db);invalidate.Parameters.AddWithValue("id",otpId);await invalidate.ExecuteNonQueryAsync(ct);throw;}
    }
    public async Task<string> VerifyAsync(string email,string appId,string otp,CancellationToken ct)
    {
        var normalizedEmail=NormalizeEmail(email);ValidateEmail(normalizedEmail);ValidateApp(appId);
        if(otp is null||otp.Length!=6||!otp.All(char.IsDigit))throw new AuthException("invalid_otp","The verification code is invalid or expired.",400);
        var pepper=configuration["Auth:EmailOtp:HashPepper"];if(string.IsNullOrWhiteSpace(pepper)||pepper.Length<32)throw new InvalidOperationException("Auth:EmailOtp:HashPepper must be configured with at least 32 characters.");
        await using var db=CreateConnection();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        await using(var advisory=new NpgsqlCommand("select pg_advisory_xact_lock(hashtextextended(@key,0))",db,tx)){advisory.Parameters.AddWithValue("key",$"{appId}:{normalizedEmail}");await advisory.ExecuteNonQueryAsync(ct);}
        Guid otpId;string storedHash;int attempts;
        await using(var select=new NpgsqlCommand("select id,code_hash,attempt_count from auth_email_otps where app_id=@app and email=@email and consumed_at_utc is null and expires_at_utc>now() order by created_at_utc desc limit 1 for update",db,tx)){select.Parameters.AddWithValue("app",appId);select.Parameters.AddWithValue("email",normalizedEmail);await using var reader=await select.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))throw new AuthException("invalid_otp","The verification code is invalid or expired.",400);otpId=reader.GetGuid(0);storedHash=reader.GetString(1);attempts=reader.GetInt32(2);}
        if(attempts>=MaxAttempts)throw new AuthException("otp_attempts_exceeded","Too many incorrect attempts. Request a new code.",429);
        var expected=Convert.FromHexString(storedHash);var supplied=Convert.FromHexString(HashOtp(pepper,normalizedEmail,appId,otp));
        if(!CryptographicOperations.FixedTimeEquals(expected,supplied)){await using var increment=new NpgsqlCommand("update auth_email_otps set attempt_count=attempt_count+1,consumed_at_utc=case when attempt_count+1>=@max then now() else consumed_at_utc end where id=@id",db,tx);increment.Parameters.AddWithValue("max",MaxAttempts);increment.Parameters.AddWithValue("id",otpId);await increment.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);throw new AuthException("invalid_otp","The verification code is invalid or expired.",400);}
        await using(var consume=new NpgsqlCommand("update auth_email_otps set consumed_at_utc=now() where id=@id and consumed_at_utc is null",db,tx)){consume.Parameters.AddWithValue("id",otpId);if(await consume.ExecuteNonQueryAsync(ct)!=1)throw new AuthException("invalid_otp","The verification code is invalid or expired.",400);}
        Guid userId=Guid.NewGuid();
        await using(var user=new NpgsqlCommand("insert into auth_users(id,email) values(@id,@email) on conflict(email) do update set updated_at_utc=now() returning id",db,tx)){user.Parameters.AddWithValue("id",userId);user.Parameters.AddWithValue("email",normalizedEmail);userId=(Guid)(await user.ExecuteScalarAsync(ct)??throw new InvalidOperationException("Unable to activate account."));}
        await using(var identity=new NpgsqlCommand("insert into auth_identities(id,user_id,provider,provider_subject,email) values(@id,@user,'email_otp',@subject,@email) on conflict(provider,provider_subject) do update set user_id=excluded.user_id,email=excluded.email",db,tx)){identity.Parameters.AddWithValue("id",Guid.NewGuid());identity.Parameters.AddWithValue("user",userId);identity.Parameters.AddWithValue("subject",normalizedEmail);identity.Parameters.AddWithValue("email",normalizedEmail);await identity.ExecuteNonQueryAsync(ct);}
        await using(var role=new NpgsqlCommand("insert into auth_user_roles(user_id,app_id,role) values(@user,@app,'BullionIQ') on conflict(user_id,app_id) do nothing",db,tx)){role.Parameters.AddWithValue("user",userId);role.Parameters.AddWithValue("app",appId);await role.ExecuteNonQueryAsync(ct);}
        var ticketCode=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+','-').Replace('/','_');
        await using(var ticket=new NpgsqlCommand("insert into auth_login_tickets(code,user_id,app_id,role,expires_at_utc) values(@code,@user,@app,'BullionIQ',@expires)",db,tx)){ticket.Parameters.AddWithValue("code",ticketCode);ticket.Parameters.AddWithValue("user",userId);ticket.Parameters.AddWithValue("app",appId);ticket.Parameters.AddWithValue("expires",DateTimeOffset.UtcNow.AddMinutes(2));await ticket.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);return ticketCode;
    }
    private void ValidateApp(string appId){var allowed=configuration.GetSection("Auth:EmailOtp:AllowedApps").Get<string[]>()??[];if(string.IsNullOrWhiteSpace(appId)||!allowed.Contains(appId,StringComparer.Ordinal))throw new AuthException("invalid_app","Email OTP is not enabled for this application.",400);}
    private static void ValidateEmail(string email){if(email.Length is <5 or >320||!System.Net.Mail.MailAddress.TryCreate(email,out var parsed)||!string.Equals(parsed.Address,email,StringComparison.OrdinalIgnoreCase))throw new AuthException("invalid_email","Enter a valid email address.",400);}
    private static string NormalizeEmail(string? email)=>(email??string.Empty).Trim().ToLowerInvariant();
    private static string HashOtp(string pepper,string email,string appId,string otp)=>Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper),Encoding.UTF8.GetBytes($"{appId}\n{email}\n{otp}")));
}