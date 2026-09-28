using TriSend.Api.Data;
using TriSend.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace TriSend.Api.Controllers;

[ApiController]
[Route("v1/messages")]
public sealed class MessagesController(
    MessageRepository repository,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send(
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Recipient) ||
            string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { code = "validation_error", message = "Recipient and body are required." });

        if (!Enum.TryParse<MessageChannel>(request.Channel, true, out var channel))
            return BadRequest(new { code = "validation_error", message = "Channel must be sms, whatsapp, or email." });

        if (channel == MessageChannel.Email && string.IsNullOrWhiteSpace(request.Subject))
            return BadRequest(new { code = "validation_error", message = "Subject is required for email." });

        var tenantId = GetTenantId();
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        await repository.InsertAsync(
            new MessageRecord(
                id,
                tenantId,
                channel.ToString().ToLowerInvariant(),
                request.Recipient.Trim(),
                request.Body.Trim(),
                request.Subject?.Trim(),
                "queued",
                null,
                null,
                createdAt,
                null),
            cancellationToken);

        return Accepted($"/v1/messages/{id}", new
        {
            id,
            status = "queued",
            channel = channel.ToString().ToLowerInvariant()
        });
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
