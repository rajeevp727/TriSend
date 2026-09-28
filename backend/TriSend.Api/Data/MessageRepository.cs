using Npgsql;

namespace TriSend.Api.Data;

public sealed record MessageRecord(
    Guid Id,
    Guid TenantId,
    string Channel,
    string Recipient,
    string Body,
    string? Subject,
    string Status,
    string? ProviderMessageId,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

public sealed class MessageRepository(IConfiguration configuration)
{
    private readonly string _connectionString =
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

    public async Task InsertAsync(MessageRecord message, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO messages
                (id, tenant_id, channel, recipient, body, subject, status, created_at_utc, updated_at_utc)
            VALUES
                (@id, @tenant_id, @channel, @recipient, @body, @subject, @status, @created_at_utc, @updated_at_utc);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", message.Id);
        command.Parameters.AddWithValue("tenant_id", message.TenantId);
        command.Parameters.AddWithValue("channel", message.Channel);
        command.Parameters.AddWithValue("recipient", message.Recipient);
        command.Parameters.AddWithValue("body", message.Body);
        command.Parameters.AddWithValue("subject", (object?)message.Subject ?? DBNull.Value);
        command.Parameters.AddWithValue("status", message.Status);
        command.Parameters.AddWithValue("created_at_utc", message.CreatedAt);
        command.Parameters.AddWithValue("updated_at_utc", message.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(
        Guid id,
        string status,
        string? providerMessageId,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE messages
            SET status = @status,
                provider_message_id = COALESCE(@provider_message_id, provider_message_id),
                error = @error,
                sent_at = CASE WHEN @status = 'sent' THEN now() ELSE sent_at END,
                updated_at_utc = now()
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue(
            "provider_message_id",
            (object?)providerMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MessageRecord?> GetAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, tenant_id, channel, recipient, body, subject, status,
                   provider_message_id, error, created_at_utc, sent_at
            FROM messages
            WHERE tenant_id = @tenant_id AND id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new MessageRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));
    }
}
