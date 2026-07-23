using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Features.Profile.Commands.UpdateProfile;
using FinViet.Infrastructure.Features.Profile;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Profile.Commands.UpdateProfile;

public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, ProfileDto>
{
    private readonly FinVietDbContext _db;
    public UpdateProfileCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<ProfileDto> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var c = await _db.Customers
            .FirstOrDefaultAsync(x => x.CustomerId == request.CustomerId && x.IsActive, cancellationToken);

        if (c is null) throw new NotFoundException("Customer", request.CustomerId);

        c.FullName              = request.FullName.Trim();
        if (request.MonthlyIncomeExpected.HasValue) c.MonthlyIncomeExpected = request.MonthlyIncomeExpected;
        if (request.Gender.HasValue)      c.Gender      = request.Gender;
        if (request.DateOfBirth.HasValue) c.DateOfBirth = request.DateOfBirth;
        if (request.OnboardingDone.HasValue) c.OnboardingDone = request.OnboardingDone.Value;

        // Validator enforces all-or-nothing + sum-to-100, so it's safe to assign as a trio.
        if (request.NeedsPct.HasValue && request.WantsPct.HasValue && request.SavingsPct.HasValue)
        {
            c.NeedsPct = request.NeedsPct.Value;
            c.WantsPct = request.WantsPct.Value;
            c.SavingsPct = request.SavingsPct.Value;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return c.ToProfileDto();
    }
}
