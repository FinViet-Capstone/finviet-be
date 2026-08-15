using FinViet.Application.DTOs.Analytics;
using FinViet.Application.Features.Analytics.Queries.GetAnalyticsSummary;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Analytics.Queries.GetAnalyticsSummary;

public class GetAnalyticsSummaryQueryHandler : IRequestHandler<GetAnalyticsSummaryQuery, AdminAnalyticsSummaryDto>
{
    private readonly FinVietDbContext _db;
    public GetAnalyticsSummaryQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<AdminAnalyticsSummaryDto> Handle(GetAnalyticsSummaryQuery request, CancellationToken cancellationToken)
    {
        var newSince = DateTime.UtcNow.AddDays(-30);

        var totalCustomers = await _db.Customers.AsNoTracking()
            .CountAsync(c => c.DeletedAt == null, cancellationToken);
        var activeCustomers = await _db.Customers.AsNoTracking()
            .CountAsync(c => c.DeletedAt == null && c.IsActive, cancellationToken);
        var newCustomers = await _db.Customers.AsNoTracking()
            .CountAsync(c => c.DeletedAt == null && c.CreatedAt != null && c.CreatedAt >= newSince, cancellationToken);
        var totalTransactions = await _db.Transactions.AsNoTracking().CountAsync(cancellationToken);
        var totalWallets = await _db.Wallets.AsNoTracking().CountAsync(w => !w.IsDeleted, cancellationToken);
        var totalBudgets = await _db.Budgets.AsNoTracking().CountAsync(cancellationToken);

        // Premium = distinct customers holding an active subscription to a paid plan. Distinct-by-
        // customer (not raw row count) so a customer somehow holding >1 active row is still counted
        // once. Valid (returns 0, not an error) when SubscriptionPlan has no rows yet.
        var premiumSubscriptions = await _db.CustomerSubscriptions.AsNoTracking()
            .Where(s => s.Status == "active" && s.CustomerId != null && s.Plan != null && s.Plan.Price > 0)
            .Select(s => s.CustomerId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);

        // Every customer without an active paid subscription is free-tier by default.
        var freeSubscriptions = Math.Max(0, totalCustomers - premiumSubscriptions);

        return new AdminAnalyticsSummaryDto
        {
            TotalCustomers = totalCustomers,
            ActiveCustomers = activeCustomers,
            NewCustomers = newCustomers,
            TotalTransactions = totalTransactions,
            TotalWallets = totalWallets,
            TotalBudgets = totalBudgets,
            FreeSubscriptions = freeSubscriptions,
            PremiumSubscriptions = premiumSubscriptions,
        };
    }
}
