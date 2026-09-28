using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TriSend.Api.Email;

public sealed class ResendEmailSender(
    HttpClient httpClient,
    IConfiguration configuration,
    BrandedEmailTemplate emailTemplate)
{
    public async Task<string> SendAsync(
        string idempotencyKey,
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["Resend:ApiKey"]
            ?? throw new InvalidOperationException("Resend:ApiKey is required.");

        var fromAddress = configuration["Resend:FromAddress"]
            ?? throw new InvalidOperationException("Resend:FromAddress is required.");

        var fromName = configuration["Resend:FromName"];
        var from = string.IsNullOrWhiteSpace(fromName)
            ? fromAddress
            : $"{fromName} <{fromAddress}>";

        var template = emailTemplate.Build(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var payload = new Dictionary<string, object?>
        {
            ["from"] = from,
            ["to"] = new[] { recipient },
            ["subject"] = subject,
            ["text"] = template.Text,
            ["html"] = template.Html
        };

        if (template.ImageBase64 is not null &&
            template.ImageFileName is not null &&
            template.ImageContentType is not null &&
            template.ImageContentId is not null)
        {
            payload["attachments"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["content"] = template.ImageBase64,
                    ["filename"] = template.ImageFileName,
                    ["content_type"] = template.ImageContentType,
                    ["content_id"] = template.ImageContentId
                }
            };
        }

        request.Content = JsonContent.Create(payload);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Resend rejected the email ({(int)response.StatusCode}): {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);

        if (!document.RootElement.TryGetProperty("id", out var idElement) ||
            string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw new InvalidOperationException("Resend returned a successful response without an email id.");
        }

        return idElement.GetString()!;
    }
}
