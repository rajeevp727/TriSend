using TriSend.Api.Data;
using TriSend.Api.Email;
using TriSend.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace TriSend.Api.Controllers;

[ApiController]
[Route("v1/messages")]
public sealed class MessagesController(
    MessageRepository repository,
    ResendEmailSender emailSender,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send(
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Recipient) ||
            string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new
            {
                code = "validation_error",
                message = "Recipient and body are required."
            });
        }

        if (!Enum.TryParse<MessageChannel>(request.Channel, true, out var channel))
        {
            return BadRequest(new
            {
                code = "validation_error",
                message = "Channel must be email, sms, or whatsapp."
            });
        }

        if (channel != MessageChannel.Email)
        {
            return BadRequest(new
            {
                code = "unsupported_channel",
                message = "Only email is currently supported."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return BadRequest(new
            {
                code = "validation_error",
                message = "Subject is required for email."
            });
        }

        var tenantId = GetTenantId();
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var recipient = request.Recipient.Trim();
        var subject = request.Subject.Trim();
        var body = request.Body.Trim();

        await repository.InsertAsync(
            new MessageRecord(
                id,
                tenantId,
                "email",
                recipient,
                body,
                subject,
                "processing",
                null,
                null,
                createdAt,
                null),
            cancellationToken);

        try
        {
            var providerMessageId = await emailSender.SendAsync(
                id,
                recipient,
                subject,
                body,
                cancellationToken);

            await repository.UpdateStatusAsync(
                id,
                "sent",
                providerMessageId,
                null,
                cancellationToken);

            return Ok(new
            {
                id,
                status = "sent",
                channel = "email",
                providerMessageId
            });
        }
        catch (Exception ex)
        {
            await repository.UpdateStatusAsync(
                id,
                "failed",
                null,
                ex.Message,
                cancellationToken);

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                id,
                status = "failed",
                code = "email_provider_error",
                message = "The email provider rejected or could not accept the message."
            });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var message = await repository.GetAsync(GetTenantId(), id, cancellationToken);
        return message is null ? NotFound() : Ok(message);
    }

    private Guid GetTenantId()
    {
        var raw = configuration["Mvp:TenantId"];
        return Guid.TryParse(raw, out var tenantId)
            ? tenantId
            : throw new InvalidOperationException("Mvp:TenantId is not configured.");
    }
}

public sealed record SendMessageRequest(
    string Channel,
    string Recipient,
    string Body,
    string? Subject = null,
    string? IdempotencyKey = null);
