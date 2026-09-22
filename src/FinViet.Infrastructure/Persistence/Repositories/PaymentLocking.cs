using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Persistence.Repositories;

/// <summary>
/// Row-locks a Payment by orderCode within an open transaction so the PayOS webhook and the
/// GetPaymentStatus reconciliation fallback serialize on the same row instead of both reading
/// it as "pending" - see SubscriptionPaymentResultService's ApplyResultAsync contract, which
/// requires callers to lock before calling it.
/// </summary>
internal static class PaymentLocking
{
    internal static async Task<Payment?> LockByOrderCodeAsync(
        FinVietDbContext db, long orderCode, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            // Test doubles (EF Core's InMemory provider) can't execute raw SQL / FOR UPDATE;
            // fall back to a plain tracking read so unit tests can exercise handler logic
            // without a real PostgreSQL lock. Production always runs against Npgsql, which is
            // relational, so this branch never applies there.
            return await db.Payments
                .Where(p => p.OrderCode == orderCode)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return await db.Payments
            .FromSqlInterpolated($"""
                SELECT id, subscription_id, customer_id, plan_id, amount, charge_type, status,
                       order_code, payos_transaction_id, paid_at, raw_webhook_payload,
                       idempotency_key, created_at, updated_at
                FROM payments
                WHERE order_code = {orderCode}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
