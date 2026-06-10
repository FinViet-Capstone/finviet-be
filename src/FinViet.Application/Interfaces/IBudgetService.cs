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
    }
}
