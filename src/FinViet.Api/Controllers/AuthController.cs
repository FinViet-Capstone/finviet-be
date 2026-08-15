using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.Features.Auth.Commands.AdminLogin;
using FinViet.Application.Features.Auth.Commands.ChangeAdminPassword;
using FinViet.Application.Features.Auth.Commands.ChangePassword;
using FinViet.Application.Features.Auth.Commands.ForgotPassword;
using FinViet.Application.Features.Auth.Commands.GoogleLogin;
using FinViet.Application.Features.Auth.Commands.Login;
using FinViet.Application.Features.Auth.Commands.Logout;
using FinViet.Application.Features.Auth.Commands.Register;
using FinViet.Application.Features.Auth.Commands.RefreshToken;
using FinViet.Application.Features.Auth.Commands.ResendVerificationEmail;
using FinViet.Application.Features.Auth.Commands.ResetPassword;
using FinViet.Application.Features.Auth.Commands.VerifyEmail;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>Đăng ký tài khoản mới với email/password. Gửi email xác minh qua SendGrid.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RegisterCommand(request.FullName, request.Email, request.Password), ct);

        return StatusCode(201, ApiResponse<string>.Ok(result));
    }

    /// <summary>Xác minh email qua token được gửi trong email.</summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new VerifyEmailCommand(request.Token), ct);
        return Ok(ApiResponse<string>.Ok(result));
    }

    /// <summary>Xác minh email qua link (GET) – dùng cho cả mobile và web khi user click link trong email.</summary>
    [HttpGet("verify-email")]
    [Produces("text/html")]
    public async Task<IActionResult> VerifyEmailFromLink([FromQuery] string token, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new VerifyEmailCommand(token), ct);
            return new ContentResult
            {
                ContentType = "text/html; charset=utf-8",
                StatusCode  = StatusCodes.Status200OK,
                Content     = BuildVerifyEmailHtml(true, "Email của bạn đã được xác minh thành công. Bạn có thể quay lại ứng dụng FinViet và đăng nhập.")
            };
        }
        catch (Exception ex) when (ex is FinViet.Application.Common.Exceptions.NotFoundException
                                   or FinViet.Application.Common.Exceptions.BadRequestException)
        {
            return new ContentResult
            {
                ContentType = "text/html; charset=utf-8",
                StatusCode  = StatusCodes.Status200OK,
                Content     = BuildVerifyEmailHtml(false, ex.Message)
            };
        }
    }

    /// <summary>Gửi lại email xác minh nếu user chưa nhận được hoặc token đã hết hạn.</summary>
    [HttpPost("resend-verification")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ResendVerificationEmailCommand(request.Email), ct);
        return Ok(ApiResponse<string>.Ok(result));
    }

    /// <summary>Đăng nhập bằng email/password. Trả về JWT access token và refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new LoginCommand(request.Email, request.Password), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Admin đăng nhập bằng username/password. Trả về JWT có role=Admin.</summary>
    [HttpPost("admin-login")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdminLogin([FromBody] AdminLoginRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AdminLoginCommand(request.Username, request.Password), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Đăng nhập bằng Google OAuth qua Firebase ID token.</summary>
    [HttpPost("google-login")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new GoogleLoginCommand(request.IdToken), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Làm mới access token với refresh token (rotation: token cũ bị thu hồi).</summary>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RefreshTokenCommand(request.RefreshToken), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Đăng xuất – thu hồi refresh token.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await _mediator.Send(new LogoutCommand(request.RefreshToken), ct);
        return NoContent();
    }

    /// <summary>Gửi email đặt lại mật khẩu qua SendGrid.</summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ForgotPasswordCommand(request.Email), ct);
        return Ok(ApiResponse<string>.Ok(result));
    }

    /// <summary>Đặt lại mật khẩu mới bằng token từ email.</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ResetPasswordCommand(request.Token, request.NewPassword, request.ConfirmPassword), ct);
        return Ok(ApiResponse<string>.Ok(result));
    }

    /// <summary>Đổi mật khẩu khi đã đăng nhập (yêu cầu mật khẩu hiện tại).</summary>
    [HttpPost("change-password")]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var result = await _mediator.Send(
            new ChangePasswordCommand(customerId, request.CurrentPassword, request.NewPassword), ct);

        return Ok(ApiResponse<string>.Ok(result));
    }

    /// <summary>Đổi mật khẩu admin khi đã đăng nhập (yêu cầu mật khẩu hiện tại).</summary>
    [HttpPost("admin-change-password")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeAdminPassword([FromBody] ChangeAdminPasswordRequest request, CancellationToken ct)
    {
        var adminId = User.GetCustomerId();
        var result = await _mediator.Send(
            new ChangeAdminPasswordCommand(adminId, request.CurrentPassword, request.NewPassword), ct);

        return Ok(ApiResponse<string>.Ok(result));
    }

    private static string BuildVerifyEmailHtml(bool success, string message)
    {
        var color   = success ? "#10B981" : "#EF4444";
        var title   = success ? "Xác minh thành công" : "Xác minh thất bại";
        var icon    = success ? "&#10004;" : "&#10006;";
        return $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
  <meta charset=""utf-8""/>
  <meta name=""viewport"" content=""width=device-width,initial-scale=1""/>
  <title>FinViet – {title}</title>
</head>
<body style=""font-family:Arial,sans-serif;background:#F3F4F6;margin:0;padding:24px;"">
  <div style=""max-width:480px;margin:48px auto;background:#fff;border-radius:12px;padding:32px;text-align:center;box-shadow:0 4px 12px rgba(0,0,0,0.05);"">
    <div style=""width:72px;height:72px;border-radius:50%;background:{color};color:#fff;font-size:40px;line-height:72px;margin:0 auto 16px;"">{icon}</div>
    <h2 style=""color:{color};margin:0 0 12px;"">{title}</h2>
    <p style=""color:#374151;font-size:16px;line-height:1.5;"">{System.Net.WebUtility.HtmlEncode(message)}</p>
    <p style=""color:#6B7280;font-size:13px;margin-top:24px;"">Bạn có thể đóng cửa sổ này và quay lại ứng dụng FinViet.</p>
  </div>
</body>
</html>";
    }
}

// ── Request DTOs (inline for simplicity) ──────────────────────────────────────
public record RegisterRequest(string FullName, string Email, string Password);
public record VerifyEmailRequest(string Token);
public record ResendVerificationRequest(string Email);
public record LoginRequest(string Email, string Password);
public record AdminLoginRequest(string Username, string Password);
public record GoogleLoginRequest(string IdToken);
public record RefreshTokenRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ChangeAdminPasswordRequest(string CurrentPassword, string NewPassword);
