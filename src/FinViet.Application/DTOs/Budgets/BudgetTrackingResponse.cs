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

        // % đã dùng tuyệt đối — cho progress bar tổng (task #25).
        public decimal UsedPercentage { get; set; }

        // Trạng thái theo % đã dùng: GREEN / YELLOW / RED.
        public string Status { get; set; } = string.Empty;

        // ---- Pacing tổng (business logic mục 6) ----

        // Tổng số tiền đáng lẽ đã tiêu theo pacing tới hôm nay.
        public decimal ExpectedSpent { get; set; }

        // Độ lệch pacing tổng.
        public decimal PaceDeviation { get; set; }

        // Trạng thái pacing tổng: ON_TRACK / OVER_PACE / UNDER_PACE.
        public string PaceStatus { get; set; } = string.Empty;

        public List<CategoryBudgetResponse> Categories { get; set; } = new();
    }
}
