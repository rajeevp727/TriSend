using System.Net;
using System.Net.Mail;
using Npgsql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TriSend.Contracts;

namespace TriSend.Worker;

public sealed class MessageProcessor(
    IConfiguration configuration,
    ILogger<MessageProcessor> logger) : BackgroundService
{
    private readonly string _connectionString =
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

    private readonly TimeSpan _pollInterval =
        TimeSpan.FromSeconds(configuration.GetValue("Worker:PollIntervalSeconds", 1));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TriSend worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await ClaimNextAsync(stoppingToken);

                if (message is null)
                {
                    await Task.Delay(_pollInterval, stoppingToken);
                    continue;
                }

                await ProcessAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TriSend worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        logger.LogInformation("TriSend worker stopped.");
    }

    private async Task<QueuedMessage?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string sql = """
            WITH next_message AS (
                SELECT id
                FROM messages
                WHERE status = 'queued'
                ORDER BY created_at_utc
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE messages AS m
            SET status = 'processing',
                updated_at_utc = now()
            FROM next_message
            WHERE m.id = next_message.id
            RETURNING m.id, m.tenant_id, m.channel, m.recipient, m.subject, m.body;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.CloseAsync();
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var message = new QueuedMessage(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5));

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return message;
    }

    private async Task ProcessAsync(
        QueuedMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.TryParse<MessageChannel>(message.Channel, true, out var channel))
                throw new InvalidOperationException($"Unsupported channel '{message.Channel}'.");

            if (channel != MessageChannel.Email)
                throw new NotSupportedException(
                    $"SMTP provider currently supports email only. Channel={channel}");

            var providerMessageId = await SendEmailAsync(message, cancellationToken);

            await UpdateStatusAsync(
                message.Id,
                "sent",
                providerMessageId,
                null,
                cancellationToken);

            logger.LogInformation(
                "TriSend worker sent message {MessageId}. Channel={Channel} ProviderMessageId={ProviderMessageId}",
                message.Id,
                message.Channel,
                providerMessageId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "TriSend worker failed message {MessageId}. Channel={Channel}",
                message.Id,
                message.Channel);

            await UpdateStatusAsync(
                message.Id,
                "failed",
                null,
                ex.Message,
                cancellationToken);
        }
    }

    private async Task<string> SendEmailAsync(
        QueuedMessage message,
        CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"]
            ?? throw new InvalidOperationException("Smtp:Host is required.");
        var port = configuration.GetValue<int?>("Smtp:Port") ?? 587;
        var username = configuration["Smtp:Username"]
            ?? throw new InvalidOperationException("Smtp:Username is required.");
        var password = configuration["Smtp:Password"]
            ?? throw new InvalidOperationException("Smtp:Password is required.");
        var fromAddress = configuration["Smtp:FromAddress"] ?? username;
        var fromName = configuration["Smtp:FromName"] ?? "TriSend";
        var enableSsl = configuration.GetValue("Smtp:EnableSsl", true);

        using var mail = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = message.Subject ?? "TriSend message",
            Body = message.Body,
            IsBodyHtml = false
        };

        mail.To.Add(new MailAddress(message.Recipient));

        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30000
        };

        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(mail, cancellationToken);

        return $"smtp_{Guid.NewGuid():N}";
    }

    private async Task UpdateStatusAsync(
        Guid messageId,
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
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue(
            "provider_message_id",
            (object?)providerMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record QueuedMessage(
        Guid Id,
        Guid TenantId,
        string Channel,
        string Recipient,
        string? Subject,
        string Body);
}
