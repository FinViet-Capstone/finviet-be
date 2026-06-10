using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Budgets
{
    public class BudgetPlanResponse
    {
        public Guid PlanId { get; set; }

        public Guid? CustomerId { get; set; }

        public string PlanName { get; set; } = string.Empty;

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        public List<CategoryBudgetResponse> CategoryBudgets { get; set; } = new();
    }
}
