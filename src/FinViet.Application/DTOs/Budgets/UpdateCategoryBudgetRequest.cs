using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Budgets
{
    public class UpdateCategoryBudgetRequest
    {
        public decimal? AmountLimit { get; set; }

        public decimal? ThresholdPct { get; set; }

        public string? ThresholdType { get; set; }
    }
}
