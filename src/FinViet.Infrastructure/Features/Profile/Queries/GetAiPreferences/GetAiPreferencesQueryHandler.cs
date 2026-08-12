using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.Profile.Queries.GetAiPreferences;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Profile.Queries.GetAiPreferences;

public class GetAiPreferencesQueryHandler : IRequestHandler<GetAiPreferencesQuery, AiPreferenceDto>
{
    private readonly FinVietDbContext _db;

    public GetAiPreferencesQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<AiPreferenceDto> Handle(
        GetAiPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        var customerExists = await _db.Customers.AsNoTracking().AnyAsync(
            c => c.CustomerId == request.CustomerId && c.IsActive,
            cancellationToken);
        if (!customerExists)
            throw new NotFoundException("Customer", request.CustomerId);

        var preference = await _db.AiCustomerPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CustomerId == request.CustomerId, cancellationToken);
        return preference is null ? AiPreferenceMapper.Default() : AiPreferenceMapper.Map(preference);
    }
}
