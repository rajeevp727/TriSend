using System.Net;
using System.Net.Mail;
using System.Text.Json;
using TriSend.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TriSend.Worker;

public sealed class MessageProcessor(
    IConfiguration configuration,
    ILogger<MessageProcessor> logger)
{
    [Function("ProcessMessage")]
    public async Task Run(
        [ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnection")]
        string body,
        CancellationToken cancellationToken)
    {
        var command = JsonSerializer.Deserialize<SendMessageCommand>(body)
            ?? throw new InvalidOperationException("Invalid TriSend message payload.");

        await UpdateStatusAsync(command.MessageId, "processing", null, null, cancellationToken);

        try
        {
            var providerMessageId = await SendAsync(command, cancellationToken);

            logger.LogInformation(
                "TriSend worker sent message {MessageId}. Channel={Channel} ProviderMessageId={ProviderMessageId}",
                command.MessageId, command.Channel, providerMessageId);

            await UpdateStatusAsync(
                command.MessageId,
                "sent",
                providerMessageId,
                null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "TriSend worker failed message {MessageId}. Channel={Channel}",
                command.MessageId,
                command.Channel);

            await UpdateStatusAsync(
                command.MessageId,
                "failed",
                null,
                ex.Message,
                cancellationToken);

            throw;
        }
    }

    private async Task<string> SendAsync(
        SendMessageCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Channel != MessageChannel.Email)
            throw new NotSupportedException(
                $"SMTP provider currently supports email only. Channel={command.Channel}");

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
            Subject = command.Subject ?? "TriSend message",
            Body = command.Body,
            IsBodyHtml = false
        };
        mail.To.Add(new MailAddress(command.Recipient));

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
        var connectionString = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("ConnectionStrings:Sql is required.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE dbo.Messages
            SET Status = @Status,
                ProviderMessageId = COALESCE(@ProviderMessageId, ProviderMessageId),
                Error = @Error,
                SentAt = CASE WHEN @Status = 'sent' THEN SYSUTCDATETIME() ELSE SentAt END
            WHERE Id = @Id;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", messageId);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue(
            "@ProviderMessageId",
            (object?)providerMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
