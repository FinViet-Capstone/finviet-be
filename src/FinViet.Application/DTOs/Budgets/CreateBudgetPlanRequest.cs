using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Budgets
{
    public class CreateBudgetPlanRequest
    {
        public string PlanName { get; set; } = string.Empty;

        public Guid? ModelId { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        // Phân bổ 50-30-20 (tùy chỉnh được). Tổng phải = 100.
        // Để null cả 3 → mặc định 50/30/20.
        public decimal? NeedsPct { get; set; }

        public decimal? WantsPct { get; set; }

        public decimal? SavingsPct { get; set; }

        public List<CreateCategoryBudgetRequest> CategoryBudgets { get; set; } = new();
    }

    public class CreateCategoryBudgetRequest
    {
        public Guid CategoryId { get; set; }

        // null = áp dụng cho mọi ví; có giá trị = chỉ cho ví cụ thể.
        public Guid? WalletId { get; set; }

        public decimal AmountLimit { get; set; }

        public decimal? ThresholdPct { get; set; } = 80;

        public string? ThresholdType { get; set; } = "PERCENT";
    }
}
