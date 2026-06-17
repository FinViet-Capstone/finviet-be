using System;

namespace FinViet.Infrastructure.Persistence.Entities;

// Flat recurring budget (schema v2.1 §5): một dòng cho mỗi (customer, category, wallet),
// áp dụng mọi tháng. `spent` KHÔNG lưu — tính động theo tháng đang xem (ICT).
public partial class Budget
{
    public Guid BudgetId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid CategoryId { get; set; }

    // null = áp dụng cho mọi ví; có giá trị = chỉ ví cụ thể.
    public Guid? WalletId { get; set; }

    public decimal MonthlyLimit { get; set; }

    // Mốc đã cảnh báo trong tháng hiện tại (0/80/100) — dedup alert.
    public decimal LastAlertThreshold { get; set; }

    // 'YYYY-MM' (ICT) của lần alert gần nhất → sang tháng mới thì reset cờ.
    public string? LastAlertMonth { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Category? Category { get; set; }

    public virtual Wallet? Wallet { get; set; }
}
