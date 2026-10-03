using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Persistence.Repositories;

/// <summary>
/// Serializes every change to one customer's subscription (payment results and the lifecycle
/// job) with a transaction-scoped PostgreSQL advisory lock. Payment rows are locked individually
/// (see <see cref="PaymentLocking"/>), so without this two different paid orders for the same
/// customer could both see "no active subscription" and race on uq_active_subscription.
/// Must be called inside an open transaction; the lock is released on commit/rollback.
/// </summary>
internal static class SubscriptionLocking
{
    internal static async Task LockCustomerAsync(FinVietDbContext db, Guid customerId, CancellationToken cancellationToken)
    {
        // EF Core's InMemory provider (unit tests) has no SQL or locks; production is always Npgsql.
        if (!db.Database.IsRelational())
            return;

        var key = $"customer-subscription:{customerId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}
