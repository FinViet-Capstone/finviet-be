using System.Text.Json;
using FinViet.Application.DTOs.SubscriptionPlans;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Infrastructure.Features.SubscriptionPlans;

internal static class SubscriptionPlanDtoMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        PlanId = plan.PlanId,
        Code = plan.Code,
        Name = plan.Name,
        Price = plan.Price,
        BillingIntervalMonths = plan.BillingIntervalMonths,
        Features = ParseFeatures(plan.FeaturesJson),
        IsActive = plan.IsActive,
    };

    public static string SerializeFeatures(string[] features) => JsonSerializer.Serialize(features);

    private static string[] ParseFeatures(string? featuresJson)
    {
        if (string.IsNullOrWhiteSpace(featuresJson)) return [];
        try { return JsonSerializer.Deserialize<string[]>(featuresJson) ?? []; }
        catch (JsonException) { return []; }
    }
}
