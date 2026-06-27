namespace FinViet.Application.Interfaces;

public interface IEmailService
{
    /// <summary>Send the 6-character email verification code to a newly registered user.</summary>
    Task SendVerificationEmailAsync(string toEmail, string toName, string verificationCode);

    /// <summary>Send the 6-character password reset code to user's email.</summary>
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetCode);
}
