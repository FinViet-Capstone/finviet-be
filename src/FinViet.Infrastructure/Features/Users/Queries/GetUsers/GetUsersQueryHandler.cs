using FinViet.Application.Common;
using FinViet.Application.DTOs.Users;
using FinViet.Application.Features.Users.Queries.GetUsers;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Services;
using FinViet.Infrastructure.Services.Subscriptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Users.Queries.GetUsers;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, PagedResult<UserResponseDto>>
{
    private readonly FinVietDbContext _db;
    private readonly TimeProvider _time;

    public GetUsersQueryHandler(FinVietDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<PagedResult<UserResponseDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var filter = request.Query;
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

        var query = _db.Customers.AsNoTracking().Where(c => c.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = $"%{filter.Search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.Email, term) || EF.Functions.ILike(c.FullName, term));
        }

        var total = await query.CountAsync(cancellationToken);
        var activeOnToday = SubscriptionPeriods.ActiveOn(VietnamClock.Today(_time));

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new UserResponseDto
            {
                CustomerId = c.CustomerId,
                Email = c.Email,
                FullName = c.FullName,
                IsActive = c.IsActive,
                IsEmailVerified = c.IsEmailVerified,
                CreatedAt = c.CreatedAt,
                TotalTransactions = _db.Transactions.Count(t => t.CustomerId == c.CustomerId),
                TotalWallets = _db.Wallets.Count(w => w.CustomerId == c.CustomerId && !w.IsDeleted),
                SubscriptionPlanCode = c.CustomerSubscriptions
                    .AsQueryable()
                    .Where(activeOnToday)
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => s.Plan!.Code)
                    .FirstOrDefault() ?? "free"
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<UserResponseDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            Items = items
        };
    }
}
