using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Budgets
{
    public class BudgetHistoryResponse
    {
        public Guid PlanId { get; set; }

        public string PlanName { get; set; } = string.Empty;

        public int Month { get; set; }

        public int Year { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        public decimal TotalLimit { get; set; }

        public decimal TotalSpent { get; set; }

        public decimal UsedPercentage { get; set; }

        public decimal? PreviousMonthTotalSpent { get; set; }

        public decimal? TotalSpentChange { get; set; }

        public decimal? PreviousMonthUsedPercentage { get; set; }

        public decimal? UsedPercentageChange { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
