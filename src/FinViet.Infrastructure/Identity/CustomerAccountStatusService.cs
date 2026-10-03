using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FinViet.Infrastructure.Identity;

/// <summary>
/// DB-backed customer status lookup with a short in-memory cache so the per-request
/// check costs one indexed primary-key query at most every <see cref="CacheTtl"/>
/// per customer. An admin lock therefore takes effect within that window.
/// </summary>
public class CustomerAccountStatusService : ICustomerAccountStatusService
{
    /// <summary>Upper bound on how long a lock/unlock can take to be enforced.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

    private readonly FinVietDbContext _db;
    private readonly IMemoryCache _cache;

    public CustomerAccountStatusService(FinVietDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsActiveAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var key = CacheKey(customerId);
        if (_cache.TryGetValue(key, out bool cached))
            return cached;

        var isActive = await _db.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerId == customerId && c.IsActive && c.DeletedAt == null, cancellationToken);

        _cache.Set(key, isActive, CacheTtl);
        return isActive;
    }

    private static string CacheKey(Guid customerId) => $"customer-active:{customerId}";
}
