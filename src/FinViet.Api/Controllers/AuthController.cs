using FinViet.Application.Common;
using FinViet.Application.Features.Auth.Commands.AdminLogin;
using FinViet.Application.Features.Auth.Commands.ForgotPassword;
using FinViet.Application.Features.Auth.Commands.GoogleLogin;
using FinViet.Application.Features.Auth.Commands.Login;
using FinViet.Application.Features.Auth.Commands.Logout;
using FinViet.Application.Features.Auth.Commands.Register;
using FinViet.Application.Features.Auth.Commands.RefreshToken;
using FinViet.Application.Features.Auth.Commands.ResetPassword;
using FinViet.Application.Features.Auth.Commands.VerifyEmail;
using MediatR;
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
}

// ── Request DTOs (inline for simplicity) ──────────────────────────────────────
public record RegisterRequest(string FullName, string Email, string Password);
public record VerifyEmailRequest(string Token);
public record LoginRequest(string Email, string Password);
public record AdminLoginRequest(string Username, string Password);
public record GoogleLoginRequest(string IdToken);
public record RefreshTokenRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
