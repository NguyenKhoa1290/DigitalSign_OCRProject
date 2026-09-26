using IdentityService.Core.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace IdentityService.Infrastructure.Services;

/// <summary>
/// Gửi email transactional qua Gmail SMTP bằng MailKit.
/// Cấu hình đọc từ EmailSettings trong appsettings.json.
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
        => _configuration = configuration;

    public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string otp)
    {
        var settings = _configuration.GetSection("EmailSettings");
        var smtpHost = settings["SmtpHost"] ?? "smtp.gmail.com";
        var smtpPort = int.Parse(settings["SmtpPort"] ?? "587");
        var secureSocketOptions = ParseSecureSocketOptions(settings["SecureSocketOptions"]);
        var requireAuth = bool.TryParse(settings["RequireAuth"], out var parsedRequireAuth)
            ? parsedRequireAuth
            : true;
        var username = settings["Username"];
        var password = settings["Password"];
        string? authUsername = null;
        string? authPassword = null;
        var fromEmail = settings["FromEmail"];
        var fromName = settings["FromName"] ?? "HAU Documents";

        if (requireAuth)
        {
            authUsername = GetRequiredSetting(settings, "Username");
            authPassword = GetRequiredSetting(settings, "Password");
            username = authUsername;
            password = authPassword;
        }

        var senderEmail = !string.IsNullOrWhiteSpace(fromEmail)
            ? fromEmail
            : username;

        if (string.IsNullOrWhiteSpace(senderEmail))
            throw new InvalidOperationException(
                "EmailSettings:FromEmail or EmailSettings:Username must be configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, senderEmail));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = "[HAU Documents] Mã OTP khôi phục mật khẩu";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='font-family:Arial,sans-serif;max-width:520px;margin:auto;padding:24px;
            border:1px solid #e5e7eb;border-radius:12px;'>
  <div style='text-align:center;margin-bottom:24px;'>
    <h2 style='color:#1e3a5f;margin:0;'>HAU Documents</h2>
    <p style='color:#6b7280;margin:4px 0 0;'>Hệ thống Quản lý Công văn</p>
  </div>
  <p style='color:#374151;'>Xin chào <strong>{toName}</strong>,</p>
  <p style='color:#374151;'>Bạn đã yêu cầu khôi phục mật khẩu. Đây là mã OTP của bạn:</p>
  <div style='text-align:center;margin:28px 0;'>
    <span style='font-size:36px;font-weight:bold;letter-spacing:10px;color:#1e3a5f;
                 background:#f0f4ff;padding:16px 32px;border-radius:8px;
                 display:inline-block;'>{otp}</span>
  </div>
  <p style='color:#6b7280;font-size:14px;'>
    ⏱ Mã OTP có hiệu lực trong <strong>15 phút</strong> kể từ thời điểm gửi.
  </p>
  <p style='color:#6b7280;font-size:14px;'>
    Nếu bạn không yêu cầu khôi phục mật khẩu, hãy bỏ qua email này.
  </p>
  <hr style='border:none;border-top:1px solid #e5e7eb;margin:24px 0;' />
  <p style='color:#9ca3af;font-size:12px;text-align:center;'>
    © {DateTime.Now.Year} Trường Đại học Kiến Trúc Hà Nội
  </p>
</div>",
            TextBody = $"Mã OTP khôi phục mật khẩu của bạn: {otp}\nMã có hiệu lực trong 15 phút."
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            smtpHost,
            smtpPort,
            secureSocketOptions);

        if (requireAuth)
            await client.AuthenticateAsync(authUsername!, authPassword!);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    public async Task SendEmailVerificationOtpAsync(string toEmail, string toName, string otp)
    {
        var settings = _configuration.GetSection("EmailSettings");
        var smtpHost = settings["SmtpHost"] ?? "smtp.gmail.com";
        var smtpPort = int.Parse(settings["SmtpPort"] ?? "587");
        var secureSocketOptions = ParseSecureSocketOptions(settings["SecureSocketOptions"]);
        var requireAuth = bool.TryParse(settings["RequireAuth"], out var parsedRequireAuth)
            ? parsedRequireAuth
            : true;
        var username = settings["Username"];
        var fromEmail = settings["FromEmail"];
        var fromName = settings["FromName"] ?? "HAU Documents";
        string? authUsername = null;
        string? authPassword = null;

        if (requireAuth)
        {
            authUsername = GetRequiredSetting(settings, "Username");
            authPassword = GetRequiredSetting(settings, "Password");
            username = authUsername;
        }

        var senderEmail = !string.IsNullOrWhiteSpace(fromEmail)
            ? fromEmail
            : username;

        if (string.IsNullOrWhiteSpace(senderEmail))
            throw new InvalidOperationException(
                "EmailSettings:FromEmail or EmailSettings:Username must be configured.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, senderEmail));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = "[HAU Documents] Xác minh địa chỉ email";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='font-family:Arial,sans-serif;max-width:520px;margin:auto;padding:24px;
            border:1px solid #e5e7eb;border-radius:12px;'>
  <div style='text-align:center;margin-bottom:24px;'>
    <h2 style='color:#1e3a5f;margin:0;'>HAU Documents</h2>
    <p style='color:#6b7280;margin:4px 0 0;'>Xác minh địa chỉ email</p>
  </div>
  <p style='color:#374151;'>Xin chào <strong>{toName}</strong>,</p>
  <p style='color:#374151;'>Nhập mã dưới đây để xác minh email cho tài khoản của bạn:</p>
  <div style='text-align:center;margin:28px 0;'>
    <span style='font-size:36px;font-weight:bold;letter-spacing:10px;color:#1e3a5f;
                 background:#f0f4ff;padding:16px 32px;border-radius:8px;
                 display:inline-block;'>{otp}</span>
  </div>
  <p style='color:#6b7280;font-size:14px;'>
    Mã xác minh có hiệu lực trong <strong>15 phút</strong> và chỉ dùng một lần.
  </p>
  <p style='color:#6b7280;font-size:14px;'>
    Nếu bạn không thực hiện yêu cầu này, hãy bỏ qua email.
  </p>
</div>",
            TextBody = $"Mã xác minh email HAU Documents: {otp}\nMã có hiệu lực trong 15 phút."
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpHost, smtpPort, secureSocketOptions);

        if (requireAuth)
            await client.AuthenticateAsync(authUsername!, authPassword!);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static SecureSocketOptions ParseSecureSocketOptions(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SecureSocketOptions.StartTls;

        return Enum.TryParse<SecureSocketOptions>(value, ignoreCase: true, out var result)
            ? result
            : throw new InvalidOperationException(
                $"EmailSettings:SecureSocketOptions has invalid value '{value}'.");
    }

    private static string GetRequiredSetting(IConfigurationSection settings, string key)
    {
        var value = settings[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"EmailSettings:{key} is not configured.");

        return value;
    }
}
