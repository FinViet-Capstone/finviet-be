using System.Text;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class FinancialContextService : IFinancialContextService
{
    private readonly FinVietDbContext _db;
    private readonly ISpendingScoreService _scoreService;

    public FinancialContextService(FinVietDbContext db, ISpendingScoreService scoreService)
    {
        _db = db;
        _scoreService = scoreService;
    }

    public async Task<FinancialContextResult> BuildCurrentMonthAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var firstDay = new DateOnly(today.Year, today.Month, 1);
        var monthStart = DateRange.StartUtc(firstDay);
        var monthEnd = DateRange.EndUtc(today);
        var period = $"{firstDay:yyyy-MM-dd}/{today:yyyy-MM-dd}";

        var preference = await _db.AiCustomerPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CustomerId == customerId, cancellationToken);
        var shareBalances = preference?.ShareBalances ?? true;
        var shareTransactions = preference?.ShareTransactions ?? true;
        var shareBudgets = preference?.ShareBudgets ?? true;
        var shareGoals = preference?.ShareGoals ?? true;
        var shareReports = preference?.ShareReports ?? true;

        var limitations = new List<string>
        {
            "Thông tin chỉ nhằm hỗ trợ quản lý tài chính cá nhân, không phải tư vấn đầu tư hoặc tín dụng."
        };
        var citations = new List<ChatCitation>();
        var context = new StringBuilder();
        context.AppendLine($"Kỳ dữ liệu: {firstDay:yyyy-MM-dd} đến {today:yyyy-MM-dd} (Asia/Ho_Chi_Minh).");

        var incomeExpected = await _db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => c.MonthlyIncomeExpected)
            .FirstOrDefaultAsync(cancellationToken);
        context.AppendLine(
            $"Thu nhập kỳ vọng hàng tháng: {(incomeExpected.HasValue ? $"{incomeExpected.Value:N0}đ" : "chưa thiết lập")}.");

        if (shareBalances)
        {
            var totalBalance = await _db.Wallets.AsNoTracking()
                .Where(w => w.CustomerId == customerId && !w.IsDeleted)
                .SumAsync(w => (decimal?)w.Balance, cancellationToken) ?? 0m;
            context.AppendLine($"Tổng số dư ví đang hoạt động: {totalBalance:N0}đ.");
            citations.Add(new ChatCitation("wallet_summary", "Tổng số dư ví", period));
        }
        else
        {
            context.AppendLine("Phạm vi số dư đã bị khách hàng tắt.");
            limitations.Add("Khách hàng đã tắt quyền dùng dữ liệu số dư ví.");
        }

        Dictionary<string, decimal> monthSpend = [];
        if (shareTransactions)
        {
            var aggregates = await _db.Transactions.AsNoTracking()
                .Where(t => t.CustomerId == customerId
                            && t.TransactionDate >= monthStart
                            && t.TransactionDate <= monthEnd)
                .GroupBy(t => t.TransactionType)
                .Select(g => new { Type = g.Key, Total = g.Sum(t => t.Amount), Count = g.Count() })
                .ToListAsync(cancellationToken);
            var totalIncome = aggregates
                .Where(x => x.Type == "income")
                .Sum(x => x.Total);
            var totalExpense = aggregates
                .Where(x => x.Type == "expense")
                .Sum(x => x.Total);
            var transactionCount = aggregates.Sum(x => x.Count);
            var uncategorizedCount = await _db.Transactions.AsNoTracking()
                .CountAsync(t => t.CustomerId == customerId
                                 && t.TransactionType == "expense"
                                 && t.CategoryId == null
                                 && t.TransactionDate >= monthStart
                                 && t.TransactionDate <= monthEnd,
                    cancellationToken);

            context.AppendLine($"Tổng thu thực tế: {totalIncome:N0}đ.");
            context.AppendLine($"Tổng chi thực tế: {totalExpense:N0}đ.");
            context.AppendLine($"Dòng tiền ròng: {totalIncome - totalExpense:N0}đ.");
            context.AppendLine($"Số giao dịch: {transactionCount}; chưa phân loại: {uncategorizedCount}.");

            var spendRows = await _db.Transactions.AsNoTracking()
                .Where(t => t.CustomerId == customerId
                            && t.TransactionType == "expense"
                            && t.CategoryId != null
                            && t.TransactionDate >= monthStart
                            && t.TransactionDate <= monthEnd)
                .Join(_db.Categories.AsNoTracking(), t => t.CategoryId, c => c.CategoryId,
                    (t, c) => new { c.CategoryId, c.CategoryName, t.Amount })
                .GroupBy(x => new { x.CategoryId, x.CategoryName })
                .Select(g => new
                {
                    g.Key.CategoryId,
                    g.Key.CategoryName,
                    Total = g.Sum(x => x.Amount)
                })
                .OrderByDescending(x => x.Total)
                .ToListAsync(cancellationToken);
            monthSpend = spendRows.ToDictionary(x => x.CategoryId, x => x.Total);
            foreach (var category in spendRows.Take(8))
                context.AppendLine($"- Chi {category.CategoryName}: {category.Total:N0}đ");

            citations.Add(new ChatCitation("transaction_summary", "Tổng hợp giao dịch tháng", period));
            if (uncategorizedCount > 0)
                limitations.Add($"Có {uncategorizedCount} giao dịch chi chưa phân loại trong kỳ.");

            try
            {
                var score = await _scoreService.ComputeCurrentAsync(customerId, "MONTHLY", cancellationToken);
                context.AppendLine($"Điểm quản lý chi tiêu hiện tại: {score.FinalScore:0}/100 ({score.ColorBadge}).");
                citations.Add(new ChatCitation("spending_score", "Điểm quản lý chi tiêu tháng", period));
            }
            catch
            {
                limitations.Add("Không thể tính điểm quản lý chi tiêu trong lần trả lời này.");
            }
        }
        else
        {
            context.AppendLine("Phạm vi giao dịch đã bị khách hàng tắt.");
            limitations.Add("Khách hàng đã tắt quyền dùng dữ liệu giao dịch.");
        }

        if (shareBudgets)
        {
            if (!shareTransactions)
            {
                context.AppendLine("Không thể tính mức sử dụng ngân sách vì phạm vi giao dịch đang tắt.");
                limitations.Add("Dữ liệu ngân sách không gồm số đã chi vì quyền dùng giao dịch đang tắt.");
            }
            else
            {
                var budgets = await _db.Budgets.AsNoTracking()
                    .Where(b => b.CustomerId == customerId)
                    .Join(_db.Categories.AsNoTracking(), b => b.CategoryId, c => c.CategoryId,
                        (b, c) => new { c.CategoryName, b.CategoryId, b.MonthlyLimit })
                    .ToListAsync(cancellationToken);
                foreach (var budget in budgets.OrderByDescending(b => monthSpend.GetValueOrDefault(b.CategoryId)))
                {
                    var spent = monthSpend.GetValueOrDefault(budget.CategoryId);
                    var remaining = budget.MonthlyLimit - spent;
                    var overrun = Math.Max(0m, spent - budget.MonthlyLimit);
                    context.AppendLine(
                        $"- Ngân sách {budget.CategoryName}: hạn mức {budget.MonthlyLimit:N0}đ; " +
                        $"đã chi {spent:N0}đ; còn lại {remaining:N0}đ; vượt {overrun:N0}đ.");
                }
                citations.Add(new ChatCitation("budget_summary", "Ngân sách và mức sử dụng tháng", period));
            }
        }
        else
        {
            context.AppendLine("Phạm vi ngân sách đã bị khách hàng tắt.");
            limitations.Add("Khách hàng đã tắt quyền dùng dữ liệu ngân sách.");
        }

        if (shareGoals)
        {
            var goals = await _db.SavingGoals.AsNoTracking()
                .Where(g => g.CustomerId == customerId && !g.IsDeleted)
                .OrderBy(g => g.IsCompleted)
                .ThenBy(g => g.Deadline)
                .Take(8)
                .Select(g => new
                {
                    g.GoalName,
                    g.TargetAmount,
                    CurrentAmount = g.CurrentAmount ?? 0m,
                    g.Deadline,
                    g.IsCompleted
                })
                .ToListAsync(cancellationToken);
            foreach (var goal in goals)
            {
                var progress = goal.TargetAmount <= 0
                    ? 0m
                    : Math.Round(goal.CurrentAmount / goal.TargetAmount * 100m, 1);
                context.AppendLine(
                    $"- Mục tiêu {goal.GoalName}: {goal.CurrentAmount:N0}/{goal.TargetAmount:N0}đ " +
                    $"({progress:N1}%); hạn {(goal.Deadline.HasValue ? goal.Deadline.Value.ToString("yyyy-MM-dd") : "chưa đặt")}; " +
                    $"trạng thái {(goal.IsCompleted ? "đã hoàn thành" : "đang thực hiện")}.");
            }
            citations.Add(new ChatCitation("saving_goal_summary", "Tiến độ mục tiêu tiết kiệm", period));
        }
        else
        {
            context.AppendLine("Phạm vi mục tiêu tiết kiệm đã bị khách hàng tắt.");
            limitations.Add("Khách hàng đã tắt quyền dùng dữ liệu mục tiêu tiết kiệm.");
        }

        if (shareReports)
        {
            var latestReport = await _db.AiWeeklyReports.AsNoTracking()
                .Where(r => r.CustomerId == customerId)
                .OrderByDescending(r => r.WeekStart)
                .Select(r => new { r.WeekStart, r.GeneratedAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (latestReport is not null)
            {
                var reportPeriod = $"{latestReport.WeekStart:yyyy-MM-dd}/{latestReport.WeekStart.AddDays(6):yyyy-MM-dd}";
                context.AppendLine($"Báo cáo tuần gần nhất: kỳ {reportPeriod}, tạo lúc {latestReport.GeneratedAt:O}.");
                citations.Add(new ChatCitation("weekly_report", "Báo cáo tuần gần nhất", reportPeriod));
            }
        }
        else
        {
            context.AppendLine("Phạm vi báo cáo AI đã bị khách hàng tắt.");
            limitations.Add("Khách hàng đã tắt quyền dùng dữ liệu báo cáo tuần.");
        }

        return new FinancialContextResult(context.ToString(), period, citations, limitations);
    }
}
