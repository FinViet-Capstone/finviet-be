namespace FinViet.Application.Interfaces;

public interface IEmailService
{
    /// <summary>Send email verification link to a newly registered user.</summary>
    Task SendVerificationEmailAsync(string toEmail, string toName, string verificationUrl);

    /// <summary>Send password reset link to user's email.</summary>
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetUrl);
}
