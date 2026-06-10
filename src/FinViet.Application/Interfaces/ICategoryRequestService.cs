using FinViet.Application.DTOs.CategoryRequests;

namespace FinViet.Application.Interfaces;

public interface ICategoryRequestService
{
    // ── Customer ─────────────────────────────────────────────
    Task<CategoryRequestResponse> SubmitAsync(
        Guid customerId,
        CreateCategoryRequestRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoryRequestResponse>> GetMyRequestsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    // ── Admin ────────────────────────────────────────────────
    Task<IReadOnlyList<CategoryRequestResponse>> GetRequestsAsync(
        string? status,
        CancellationToken cancellationToken = default);

    Task<CategoryRequestResponse?> ApproveAsync(
        Guid adminId,
        Guid requestId,
        ReviewCategoryRequestRequest request,
        CancellationToken cancellationToken = default);

    Task<CategoryRequestResponse?> RejectAsync(
        Guid adminId,
        Guid requestId,
        ReviewCategoryRequestRequest request,
        CancellationToken cancellationToken = default);
}
