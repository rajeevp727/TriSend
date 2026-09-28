using System.Net;
namespace TriSend.Api.Email;

public sealed record EmailTemplate(
    string Html,
    string Text,
    string? ImageBase64,
    string? ImageFileName,
    string? ImageContentType,
    string? ImageContentId);

public sealed class BrandedEmailTemplate(IConfiguration configuration)
{
    private const string DefaultLoaderPath = "Email/Assets/TriSend.png";
    private const string DefaultLoaderContentId = "trisend-loader";

    public EmailTemplate Build(string body)
    {
        var enabled = configuration.GetValue("EmailSignature:Enabled", true);
        var companyName = configuration["EmailSignature:CompanyName"] ?? "TriSend";
        var tagline = configuration["EmailSignature:Tagline"] ?? "Messaging Infrastructure Platform for Apps";
        var websiteUrl = configuration["EmailSignature:WebsiteUrl"] ?? "https://rajeevstech.in";
        var websiteLabel = configuration["EmailSignature:WebsiteLabel"] ?? "rajeevstech.in";
        var footerText = configuration["EmailSignature:FooterText"] ?? "© 2026 TriSend. All rights reserved.";
        var loaderPath = configuration["EmailSignature:LoaderImagePath"] ?? DefaultLoaderPath;
        var loaderContentId = configuration["EmailSignature:LoaderContentId"] ?? DefaultLoaderContentId;

        var escapedBody = WebUtility.HtmlEncode(body).Replace("\r\n", "<br>").Replace("\n", "<br>");
        var text = body;

        if (!enabled)
        {
            return new EmailTemplate(
                $"<div style=\"font-family:Arial,Helvetica,sans-serif;line-height:1.6;white-space:normal\">{escapedBody}</div>",
                text,
                null,
                null,
                null,
                null);
        }

        var image = TryLoadImage(loaderPath);

        var imageHtml = image is null
            ? string.Empty
            : $"<img src=\"cid:{WebUtility.HtmlEncode(loaderContentId)}\" width=\"42\" height=\"42\" alt=\"TriSend\" style=\"display:block;border:0;width:42px;height:42px;object-fit:contain\">";

        var footerImageHtml = image is null
            ? string.Empty
            : $"<img src=\"cid:{WebUtility.HtmlEncode(loaderContentId)}\" width=\"28\" height=\"28\" alt=\"\" style=\"display:block;border:0;width:28px;height:28px;object-fit:contain\">";

        var safeCompanyName = WebUtility.HtmlEncode(companyName);
        var safeTagline = WebUtility.HtmlEncode(tagline);
        var safeWebsiteLabel = WebUtility.HtmlEncode(websiteLabel);
        var safeWebsiteUrl = WebUtility.HtmlEncode(websiteUrl);
        var emailAddress = configuration["EmailSignature:Email"] ?? "support@rajeevstech.in";
        var safeEmailAddress = WebUtility.HtmlEncode(emailAddress);
        var safeFooterText = WebUtility.HtmlEncode(footerText);

        var html = $"""
<!doctype html>
<html>
  <body style="margin:0;padding:0;background:#f5f7fa;">
    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f5f7fa;margin:0;padding:0;">
      <tr>
        <td align="center" style="padding:32px 16px;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:640px;background:#ffffff;border-radius:12px;">
            <tr>
              <td style="padding:24px 28px 12px 28px;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                  <tr>
                    <td style="vertical-align:middle;padding-right:12px;">{imageHtml}</td>
                    <td style="vertical-align:middle;">
                      <div style="font-family:Arial,Helvetica,sans-serif;font-size:20px;font-weight:700;color:#111827;">{safeCompanyName}</div>
                      <div style="font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#6b7280;">{safeTagline}</div>
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
            <tr>
              <td style="padding:12px 28px 28px 28px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.65;color:#1f2937;">
                {escapedBody}
              </td>
            </tr>
            <tr>
              <td style="padding:0 28px;">
                <div style="height:1px;background:#e5e7eb;line-height:1px;font-size:1px;">&nbsp;</div>
              </td>
            </tr>
            <tr>
              <td style="padding:18px 28px 24px 28px;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                  <tr>
                    <td style="vertical-align:middle;padding-right:10px;">{footerImageHtml}</td>
                    <td style="vertical-align:middle;font-family:Arial,Helvetica,sans-serif;">
                      <div style="font-size:12px;color:#6b7280;">{safeFooterText}</div>
                      <a href="{safeWebsiteUrl}" style="font-size:12px;color:#2563eb;text-decoration:none;">{safeWebsiteLabel}</a>
                      <span style="font-size:12px;color:#9ca3af;">&nbsp;&nbsp;|&nbsp;&nbsp;</span>
                      <a href="mailto:{safeEmailAddress}" style="font-size:12px;color:#2563eb;text-decoration:none;">{safeEmailAddress}</a>
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </td>
      </tr>
    </table>
  </body>
</html>
""";

        var textWithSignature = $"""
{body}

---
{companyName}
{tagline}
{websiteLabel}: {websiteUrl}
{footerText}
{emailAddress}
""";

        return new EmailTemplate(
            html,
            textWithSignature,
            image?.Base64,
            image?.FileName,
            image?.ContentType,
            image is null ? null : loaderContentId);
    }

    private static ImageAsset? TryLoadImage(string configuredPath)
    {
        var relativePath = configuredPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(AppContext.BaseDirectory, relativePath);

        if (!File.Exists(fullPath))
            return null;

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".gif" => "image/gif",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };

        if (contentType is null)
            return null;

        var bytes = File.ReadAllBytes(fullPath);

        return new ImageAsset(
            Convert.ToBase64String(bytes),
            Path.GetFileName(fullPath),
            contentType);
    }

    private sealed record ImageAsset(
        string Base64,
        string FileName,
        string ContentType);
}
