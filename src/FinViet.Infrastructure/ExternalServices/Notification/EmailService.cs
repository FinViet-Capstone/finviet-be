using FinViet.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace FinViet.Infrastructure.ExternalServices;

public class EmailService : IEmailService
{
    private readonly SendGridClient? _client;
    private readonly EmailAddress _from;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _logger = logger;

        var apiKey   = config["SendGrid:ApiKey"];
        var fromEmail= config["SendGrid:FromEmail"] ?? "noreply@finviet.app";
        var fromName = config["SendGrid:FromName"]  ?? "FinViet";

        _from = new EmailAddress(fromEmail, fromName);

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("REPLACE_"))
        {
            _logger.LogWarning("SendGrid:ApiKey is not configured. Emails will be logged to console only.");
            _client = null;
        }
        else
        {
            _client = new SendGridClient(apiKey);
        }
    }

    public async Task SendVerificationEmailAsync(string toEmail, string toName, string verificationCode)
    {
        var htmlContent = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
  <h2 style='color: #4F46E5;'>FinViet – Xác minh Email</h2>
  <p>Xin chào <strong>{toName}</strong>,</p>
  <p>Cảm ơn bạn đã đăng ký FinViet! Nhập mã xác minh dưới đây vào ứng dụng để hoàn tất.</p>
  <div style='margin:24px 0;padding:16px 24px;background:#F3F4F6;border-radius:8px;text-align:center;'>
    <span style='font-size:32px;font-weight:bold;letter-spacing:8px;color:#4F46E5;font-family:monospace;'>{verificationCode}</span>
  </div>
  <p style='color:#666;margin-top:16px;font-size:13px;'>
    Mã này sẽ hết hạn sau <strong>24 giờ</strong>.<br/>
    Nếu bạn không đăng ký FinViet, hãy bỏ qua email này.
  </p>
</div>";

        await SendAsync(toEmail, toName, "FinViet – Mã xác minh Email",
            $"Mã xác minh FinViet của bạn là: {verificationCode} (hết hạn sau 24 giờ).", htmlContent);
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetUrl)
    {
        var htmlContent = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
  <h2 style='color: #4F46E5;'>FinViet – Đặt lại Mật khẩu</h2>
  <p>Xin chào <strong>{toName}</strong>,</p>
  <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.</p>
  <a href='{resetUrl}'
     style='display:inline-block;padding:12px 24px;background:#EF4444;color:#fff;text-decoration:none;border-radius:6px;font-weight:bold;'>
    Đặt lại Mật khẩu
  </a>
  <p style='color:#666;margin-top:16px;font-size:13px;'>
    Liên kết này sẽ hết hạn sau <strong>1 giờ</strong>.<br/>
    Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.
  </p>
</div>";

        await SendAsync(toEmail, toName, "FinViet – Đặt lại Mật khẩu",
            $"Đặt lại mật khẩu tại: {resetUrl}", htmlContent);
    }

    private async Task SendAsync(string toEmail, string toName, string subject, string plainText, string html)
    {
        if (_client is null)
        {
            _logger.LogInformation(
                "[DEV-EMAIL] To={To} Subject={Subject}\n{Body}",
                toEmail, subject, plainText);
            return;
        }

        var to  = new EmailAddress(toEmail, toName);
        var msg = MailHelper.CreateSingleEmail(_from, to, subject, plainText, html);

        var response = await _client.SendEmailAsync(msg);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            _logger.LogError(
                "SendGrid send failed. Status={Status} From={From} To={To} Body={Body}",
                response.StatusCode, _from.Email, toEmail, body);
            throw new InvalidOperationException(
                $"SendGrid rejected the email (status {response.StatusCode}): {body}");
        }
    }
}