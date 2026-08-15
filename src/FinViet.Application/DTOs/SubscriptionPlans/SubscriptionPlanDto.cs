namespace FinViet.Application.DTOs.SubscriptionPlans;

public sealed class SubscriptionPlanDto
{
    public Guid PlanId { get; init; }
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
    public decimal Price { get; init; }
    public short BillingIntervalMonths { get; init; }
    public string[] Features { get; init; } = [];
    public bool IsActive { get; init; }
}
