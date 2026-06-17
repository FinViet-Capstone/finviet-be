using System;
using System.Collections.Generic;

namespace FinViet.Application.DTOs.Budgets
{
    // Tóm tắt theo hũ 50-30-20 cho GET /budgets/buckets.
    // (UpsertBudgetRequest / UpdateBudgetRequest / BudgetResponse nằm ở BudgetAliasDtos.cs.)

    public class BucketSummaryResponse
    {
        // NEEDS / WANTS / SAVINGS.
        public string Bucket { get; set; } = string.Empty;

        public int AllocationPct { get; set; }

        // allocationCap = income × pct (mẫu số chuẩn — KHÔNG dùng Σ category limits).
        public decimal AllocationCap { get; set; }

        public decimal Spent { get; set; }

        public decimal Remaining { get; set; }

        public decimal Percentage { get; set; }

        public string Status { get; set; } = string.Empty;

        // Pacing.
        public decimal ExpectedSpent { get; set; }

        public decimal PaceDeviation { get; set; }

        public string PaceStatus { get; set; } = string.Empty;

        // Tổng hạn mức các category trong hũ; nếu > allocationCap → cờ "vượt phân bổ".
        public decimal CategoryLimitsTotal { get; set; }

        public bool OverAllocation { get; set; }

        public List<string> Categories { get; set; } = new();
    }

    public class BucketSummaryListResponse
    {
        // 'YYYY-MM' (ICT) tháng đang xem.
        public string Month { get; set; } = string.Empty;

        public decimal MonthlyIncome { get; set; }

        public int NeedsPct { get; set; }

        public int WantsPct { get; set; }

        public int SavingsPct { get; set; }

        public List<BucketSummaryResponse> Buckets { get; set; } = new();

        // Điểm Budget Adherence (0-100), trung bình có trọng số Needs/Wants.
        public decimal BudgetAdherenceScore { get; set; }

        public decimal UncategorizedRatio { get; set; }

        public bool UncategorizedWarning { get; set; }
    }
}
