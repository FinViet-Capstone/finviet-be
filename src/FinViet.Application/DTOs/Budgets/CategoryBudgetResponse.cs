using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Budgets
{
    public class CategoryBudgetResponse
    {
        public Guid CategoryBudgetId { get; set; }

        public Guid? CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public decimal AmountLimit { get; set; }

        public decimal CurrentSpent { get; set; }

        public decimal RemainingAmount { get; set; }

        public decimal UsedPercentage { get; set; }

        public decimal? ThresholdPct { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
