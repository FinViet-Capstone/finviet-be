using FinViet.Application.DTOs.Budgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.Interfaces
{
    public interface IBudgetService
    {
        Task<IReadOnlyList<BudgetPlanResponse>> GetBudgetPlansAsync(
            Guid customerId,
            CancellationToken cancellationToken = default);

        Task<BudgetPlanResponse> CreateBudgetPlanAsync(
            Guid customerId,
            CreateBudgetPlanRequest request,
            CancellationToken cancellationToken = default);

        Task<BudgetTrackingResponse> GetBudgetTrackingAsync(
            Guid customerId,
            Guid planId,
            CancellationToken cancellationToken = default);

        Task<BudgetTrackingResponse> GetCurrentBudgetTrackingAsync(
            Guid customerId,
            CancellationToken cancellationToken = default);

        Task<CategoryBudgetResponse> UpdateCategoryBudgetAsync(
            Guid customerId,
            Guid categoryBudgetId,
            UpdateCategoryBudgetRequest request,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteBudgetPlanAsync(
            Guid customerId,
            Guid planId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<BudgetHistoryResponse>> GetBudgetHistoryAsync(
            Guid customerId,
            int year,
            CancellationToken cancellationToken = default);

        Task<BudgetPlanResponse> ResetCurrentMonthBudgetAsync(
            Guid customerId,
            CancellationToken cancellationToken = default);
        // Theo dõi ngân sách theo mô hình 50-30-20 cho một plan cụ thể.
        Task<BucketTrackingResponse> GetBucketTrackingAsync(
            Guid customerId,
            Guid planId,
            CancellationToken cancellationToken = default);

        // Theo dõi 50-30-20 cho plan đang hoạt động của tháng hiện tại.
        Task<BucketTrackingResponse> GetCurrentBucketTrackingAsync(
            Guid customerId,
            CancellationToken cancellationToken = default);
    }
}
