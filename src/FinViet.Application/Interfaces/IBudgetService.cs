using FinViet.Application.DTOs.Budgets;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FinViet.Application.Interfaces
{
    public interface IBudgetService
    {
        Task<IReadOnlyList<BudgetResponse>> GetBudgetsAsync(
            Guid customerId,
            string? month,
            CancellationToken cancellationToken = default);

        Task<BucketSummaryListResponse> GetBudgetBucketsAsync(
            Guid customerId,
            string? month,
            CancellationToken cancellationToken = default);

        Task<BudgetResponse> UpsertBudgetAsync(
            Guid customerId,
            UpsertBudgetRequest request,
            CancellationToken cancellationToken = default);

        Task<BudgetResponse> UpdateBudgetAsync(
            Guid customerId,
            Guid budgetId,
            UpdateBudgetRequest request,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteBudgetAsync(
            Guid customerId,
            Guid budgetId,
            CancellationToken cancellationToken = default);

        Task SyncBudgetOnTransactionChangeAsync(
            Guid customerId,
            DateOnly affectedDate,
            CancellationToken cancellationToken = default);
    }
}
