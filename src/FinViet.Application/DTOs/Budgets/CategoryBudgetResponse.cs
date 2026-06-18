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

        public string? CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        // null = budget áp dụng cho mọi ví; có giá trị = budget riêng cho ví này.
        public Guid? WalletId { get; set; }

        public decimal AmountLimit { get; set; }

        public decimal CurrentSpent { get; set; }

        public decimal RemainingAmount { get; set; }

        // % đã dùng tuyệt đối — dùng cho progress bar (task #25).
        public decimal UsedPercentage { get; set; }

        public decimal? ThresholdPct { get; set; }

        // Trạng thái theo % đã dùng: GREEN / YELLOW / RED (cho progress bar).
        public string Status { get; set; } = string.Empty;

        // ---- Pacing (business logic mục 6) ----

        // Số tiền "đáng lẽ đã tiêu" tới thời điểm hiện tại theo tốc độ đều.
        public decimal ExpectedSpent { get; set; }

        // Độ lệch so với pacing: (Actual - Expected) / Expected.
        public decimal PaceDeviation { get; set; }

        // Trạng thái pacing: ON_TRACK / OVER_PACE / UNDER_PACE.
        public string PaceStatus { get; set; } = string.Empty;
    }
}
