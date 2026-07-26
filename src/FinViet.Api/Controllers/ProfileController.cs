using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.Features.Profile.Commands.ScheduleIncomeAllocationChange;
using FinViet.Application.Features.Profile.Commands.UpdateProfile;
using FinViet.Application.Features.Profile.Commands.UpdateProfileSettings;
using FinViet.Application.Features.Profile.Commands.UploadAvatar;
using FinViet.Application.Features.Profile.Queries.GetIncomeAllocation;
using FinViet.Application.Features.Profile.Queries.GetProfile;
using FinViet.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize(Roles = "Customer")]
[Produces("application/json")]
public class ProfileController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProfileController(IMediator mediator) => _mediator = mediator;

    /// <summary>Lấy thông tin profile của người dùng đang đăng nhập.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var result     = await _mediator.Send(new GetProfileQuery(customerId), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Cập nhật tên và thu nhập tháng dự kiến.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var result     = await _mediator.Send(
            new UpdateProfileCommand(customerId, request.FullName, request.MonthlyIncomeExpected,
                request.Gender, request.DateOfBirth, request.OnboardingDone,
                request.NeedsPct, request.WantsPct, request.SavingsPct), ct);

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Phân bổ thu nhập hiện tại (đã khóa) và bản nháp cho tháng sau, nếu có.</summary>
    [HttpGet("income-allocation")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIncomeAllocation(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var result     = await _mediator.Send(new GetIncomeAllocationQuery(customerId), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Đặt lịch thay đổi thu nhập/phân bổ hũ, có hiệu lực từ đầu tháng sau.</summary>
    [HttpPost("income-allocation")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ScheduleIncomeAllocationChange(
        [FromBody] ScheduleIncomeAllocationRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var result     = await _mediator.Send(
            new ScheduleIncomeAllocationChangeCommand(
                customerId, request.MonthlyIncome, request.NeedsPct, request.WantsPct, request.SavingsPct), ct);

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Cập nhật giao diện (theme) và ngưỡng cảnh báo ngân sách.</summary>
    [HttpPatch("settings")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfileSettings(
        [FromBody] UpdateProfileSettingsRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var result     = await _mediator.Send(
            new UpdateProfileSettingsCommand(customerId, request.Theme, request.NotifBudgetThresholds), ct);

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Upload ảnh đại diện (JPEG/PNG/WebP, tối đa 5MB).</summary>
    [HttpPost("avatar")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("No file provided."));

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var fileContent = ms.ToArray();

        var customerId = User.GetCustomerId();
        var avatarUrl  = await _mediator.Send(
            new UploadAvatarCommand(customerId, fileContent, file.FileName, file.ContentType), ct);

        return Ok(ApiResponse<string>.Ok(avatarUrl, "Avatar uploaded successfully."));
    }
}

public record UpdateProfileRequest(
    string FullName,
    decimal? MonthlyIncomeExpected,
    Gender? Gender = null,
    DateOnly? DateOfBirth = null,
    bool? OnboardingDone = null,
    int? NeedsPct = null,
    int? WantsPct = null,
    int? SavingsPct = null);

public record ScheduleIncomeAllocationRequest(
    decimal MonthlyIncome,
    int NeedsPct,
    int WantsPct,
    int SavingsPct);

public record UpdateProfileSettingsRequest(
    AppTheme? Theme = null,
    int[]? NotifBudgetThresholds = null);