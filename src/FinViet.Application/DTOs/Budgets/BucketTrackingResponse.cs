using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Budgets
{
    // Kết quả theo dõi ngân sách theo mô hình 50-30-20.
    public class BucketTrackingResponse
    {
        public Guid PlanId { get; set; }

        public string PlanName { get; set; } = string.Empty;

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        // Thu nhập tháng dùng làm cơ sở phân bổ.
        public decimal MonthlyIncome { get; set; }

        // % phân bổ từng bucket.
        public decimal NeedsPct { get; set; }

        public decimal WantsPct { get; set; }

        public decimal SavingsPct { get; set; }

        // 3 bucket: Needs / Wants / Savings.
        public List<BucketBudgetResponse> Buckets { get; set; } = new();

        // Điểm Budget Adherence tổng (0-100), trung bình có trọng số Needs/Wants.
        public decimal BudgetAdherenceScore { get; set; }

        // Cảnh báo nếu tỉ lệ "Chưa phân loại" > 20% tổng chi.
        public decimal UncategorizedRatio { get; set; }

        public bool UncategorizedWarning { get; set; }
    }

    public class BucketBudgetResponse
    {
        // NEEDS / WANTS / SAVINGS.
        public string Bucket { get; set; } = string.Empty;

        public decimal AllocationPct { get; set; }

        // Hạn mức = income × allocation%.
        public decimal LimitAmount { get; set; }

        // Tổng hạn mức category budget trong bucket này.
        public decimal CategoryLimitTotal { get; set; }

        // true khi tổng hạn mức category vượt allocationCap; FE chỉ cảnh báo, không hard-block.
        public bool OverAllocated { get; set; }

        public decimal SpentAmount { get; set; }

        public decimal RemainingAmount { get; set; }

        // % đã dùng — cho progress bar.
        public decimal UsedPercentage { get; set; }

        // GREEN / YELLOW / RED.
        public string Status { get; set; } = string.Empty;

        // ---- Pacing ----
        public decimal ExpectedSpent { get; set; }

        public decimal PaceDeviation { get; set; }

        public string PaceStatus { get; set; } = string.Empty;

        // Các danh mục thuộc bucket này (để hiển thị breakdown).
        public List<string> Categories { get; set; } = new();
    }
}
