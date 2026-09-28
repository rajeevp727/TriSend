using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TriSend.Api.Email;

public sealed class ResendEmailSender(
    HttpClient httpClient,
    IConfiguration configuration)
{
    public async Task<string> SendAsync(
        Guid messageId,
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", messageId.ToString());

        request.Content = JsonContent.Create(new
        {
            from,
            to = new[] { recipient },
            subject,
            text = body
        });

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
