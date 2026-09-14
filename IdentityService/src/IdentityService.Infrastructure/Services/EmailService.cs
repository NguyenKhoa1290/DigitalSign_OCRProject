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

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            settings["FromName"] ?? "HAU Documents",
            settings["Username"]));
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
            settings["SmtpHost"] ?? "smtp.gmail.com",
            int.Parse(settings["SmtpPort"] ?? "587"),
            SecureSocketOptions.StartTls);

        await client.AuthenticateAsync(
            settings["Username"],
            settings["Password"]);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
