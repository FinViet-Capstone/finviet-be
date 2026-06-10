using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Budgets
{
    public class BudgetTrackingResponse
    {
        public Guid PlanId { get; set; }

        public string PlanName { get; set; } = string.Empty;

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        public decimal TotalLimit { get; set; }

        public decimal TotalSpent { get; set; }

        public decimal TotalRemaining { get; set; }

        public decimal UsedPercentage { get; set; }

        public string Status { get; set; } = string.Empty;

        public List<CategoryBudgetResponse> Categories { get; set; } = new();
    }
}
