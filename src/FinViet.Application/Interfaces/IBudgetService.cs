using FinViet.Application.DTOs.Budgets;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FinViet.Application.Interfaces
{
    // Flat recurring budgets (schema v2.1 §5 / BUSINESS_LOGIC §6).
    // `spent` derived theo tháng (ICT); % hũ lấy từ customer; mẫu số bucket = income × pct.
    public interface IBudgetService
    {
        // GET /budgets?month — danh sách budget + derived spent/remaining/percentage/status cho tháng.
        Task<IReadOnlyList<BudgetResponse>> GetBudgetsAsync(
            Guid customerId,
            string? month,
            CancellationToken cancellationToken = default);

        // GET /budgets/buckets?month — tóm tắt 3 hũ theo allocationCap (income × pct) + pacing.
        Task<BucketSummaryListResponse> GetBucketSummaryAsync(
            Guid customerId,
            string? month,
            CancellationToken cancellationToken = default);

        // POST /budgets — upsert theo (customer, category, wallet).
        Task<BudgetResponse> UpsertBudgetAsync(
            Guid customerId,
            UpsertBudgetRequest request,
            CancellationToken cancellationToken = default);

        // PATCH /budgets/{id}
        Task<BudgetResponse> UpdateBudgetAsync(
            Guid customerId,
            Guid budgetId,
            UpdateBudgetRequest request,
            CancellationToken cancellationToken = default);

        // DELETE /budgets/{id}
        Task<bool> DeleteBudgetAsync(
            Guid customerId,
            Guid budgetId,
            CancellationToken cancellationToken = default);

        // Nguồn chuẩn duy nhất cho điểm Budget Adherence (Metric 2 của Spending Score).
        // Trả về null nếu chưa có income (để Spending Score re-normalize trọng số).
        Task<decimal?> ComputeBudgetAdherenceScoreAsync(
            Guid customerId,
            DateOnly periodStart,
            DateOnly periodEnd,
            CancellationToken cancellationToken = default);

        // Cập nhật mốc cảnh báo + bắn alert 80%/100% sau khi transaction thay đổi (BL §2b).
        Task SyncBudgetOnTransactionChangeAsync(
            Guid customerId,
            DateOnly affectedDate,
            CancellationToken cancellationToken = default);
    }
}
