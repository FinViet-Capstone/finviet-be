using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Features.Profile.Commands.UpdateProfileSettings;
using FinViet.Infrastructure.Features.Profile;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Profile.Commands.UpdateProfileSettings;

public class UpdateProfileSettingsCommandHandler : IRequestHandler<UpdateProfileSettingsCommand, ProfileDto>
{
    private readonly FinVietDbContext _db;
    public UpdateProfileSettingsCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<ProfileDto> Handle(UpdateProfileSettingsCommand request, CancellationToken cancellationToken)
    {
        var c = await _db.Customers
            .Include(x => x.Setting)
            .FirstOrDefaultAsync(x => x.CustomerId == request.CustomerId && x.IsActive, cancellationToken);

        if (c is null) throw new NotFoundException("Customer", request.CustomerId);

        // No code path creates a customer_settings row today, so this is the first-write-wins
        // upsert for every customer until they change a setting for the first time.
        if (c.Setting is null)
        {
            c.Setting = new CustomerSetting
            {
                CustomerId = c.CustomerId,
                CreatedAt = DateTime.UtcNow
            };
            _db.CustomerSettings.Add(c.Setting);
        }

        if (request.Theme.HasValue)
            c.Setting.Theme = request.Theme.Value;

        if (request.NotifBudgetThresholds is not null)
            c.Setting.NotifBudgetThresholds = request.NotifBudgetThresholds;

        c.Setting.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return c.ToProfileDto();
    }
}
