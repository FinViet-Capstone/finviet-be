using FinViet.Application.Common;
using FinViet.Application.DTOs.Users;
using FinViet.Application.Features.Users.Queries.GetUsers;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Users.Queries.GetUsers;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, PagedResult<UserResponseDto>>
{
    private readonly FinVietDbContext _db;
    public GetUsersQueryHandler(FinVietDbContext db) => _db = db;

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
                CreatedAt = c.CreatedAt
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
