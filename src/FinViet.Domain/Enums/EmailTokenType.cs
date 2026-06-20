namespace FinViet.Domain.Enums;

/// <summary>Maps to Postgres enum <c>email_token_type</c> (verify_email, reset_password).</summary>
public enum EmailTokenType
{
    VerifyEmail,
    ResetPassword
}
