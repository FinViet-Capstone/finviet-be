using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.Profile.Commands.UpdateAiPreferences;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Profile.Commands.UpdateAiPreferences;

public class UpdateAiPreferencesCommandHandler
    : IRequestHandler<UpdateAiPreferencesCommand, AiPreferenceDto>
{
    private readonly FinVietDbContext _db;
    private readonly IAiTelemetryRecorder _telemetry;

    public UpdateAiPreferencesCommandHandler(
        FinVietDbContext db,
        IAiTelemetryRecorder telemetry)
    {
        _db = db;
        _telemetry = telemetry;
    }

    public async Task<AiPreferenceDto> Handle(
        UpdateAiPreferencesCommand request,
        CancellationToken cancellationToken)
    {
        var customerExists = await _db.Customers.AnyAsync(
            c => c.CustomerId == request.CustomerId && c.IsActive,
            cancellationToken);
        if (!customerExists)
            throw new NotFoundException("Customer", request.CustomerId);

        var preference = await _db.AiCustomerPreferences
            .FirstOrDefaultAsync(p => p.CustomerId == request.CustomerId, cancellationToken);
        if (preference is null)
        {
            preference = new AiCustomerPreference
            {
                CustomerId = request.CustomerId,
                CreatedAt = DateTime.UtcNow
            };
            _db.AiCustomerPreferences.Add(preference);
        }

        if (request.CategorizationMode is not null)
            preference.CategorizationMode = request.CategorizationMode.Trim().ToLowerInvariant();
        if (request.AutoCategorizationThreshold.HasValue)
            preference.AutoCategorizationThreshold = request.AutoCategorizationThreshold.Value;
        if (request.DefaultHistoryEnabled.HasValue)
            preference.DefaultHistoryEnabled = request.DefaultHistoryEnabled.Value;
        if (request.WeeklyReportEnabled.HasValue)
            preference.WeeklyReportEnabled = request.WeeklyReportEnabled.Value;
        if (request.ShareBalances.HasValue)
            preference.ShareBalances = request.ShareBalances.Value;
        if (request.ShareTransactions.HasValue)
            preference.ShareTransactions = request.ShareTransactions.Value;
        if (request.ShareBudgets.HasValue)
            preference.ShareBudgets = request.ShareBudgets.Value;
        if (request.ShareGoals.HasValue)
            preference.ShareGoals = request.ShareGoals.Value;
        if (request.ShareReports.HasValue)
            preference.ShareReports = request.ShareReports.Value;
        if (request.RagEnabled.HasValue)
            preference.RagEnabled = request.RagEnabled.Value;

        preference.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _telemetry.RecordAuditAsync(
            new AiAuditRecord(
                "ai_preference_updated",
                "customer",
                request.CustomerId,
                Metadata: ChangedFields(request)),
            cancellationToken);
        return AiPreferenceMapper.Map(preference);
    }

    private static IReadOnlyDictionary<string, object?> ChangedFields(UpdateAiPreferencesCommand request)
    {
        var changed = new List<string>();
        if (request.CategorizationMode is not null) changed.Add("categorizationMode");
        if (request.AutoCategorizationThreshold.HasValue) changed.Add("autoCategorizationThreshold");
        if (request.DefaultHistoryEnabled.HasValue) changed.Add("defaultHistoryEnabled");
        if (request.WeeklyReportEnabled.HasValue) changed.Add("weeklyReportEnabled");
        if (request.ShareBalances.HasValue) changed.Add("shareBalances");
        if (request.ShareTransactions.HasValue) changed.Add("shareTransactions");
        if (request.ShareBudgets.HasValue) changed.Add("shareBudgets");
        if (request.ShareGoals.HasValue) changed.Add("shareGoals");
        if (request.ShareReports.HasValue) changed.Add("shareReports");
        if (request.RagEnabled.HasValue) changed.Add("ragEnabled");
        return new Dictionary<string, object?> { ["changedFields"] = changed };
    }
}
