using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Features.Profile.Commands.UpdateProfile;
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
        c.MonthlyIncomeExpected = request.MonthlyIncomeExpected;

        await _db.SaveChangesAsync(cancellationToken);

        return new ProfileDto
        {
            CustomerId            = c.CustomerId,
            FullName              = c.FullName,
            Email                 = c.Email,
            AvatarUrl             = c.AvatarUrl,
            MonthlyIncomeExpected = c.MonthlyIncomeExpected,
            IsEmailVerified       = c.IsEmailVerified,
            IsActive              = c.IsActive,
            CreatedAt             = c.CreatedAt
        };
    }
}
