using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FinViet.Api.OpenApi;

public sealed class CreateBudgetPlanRequestExampleFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!IsCreateBudgetPlan(context))
            return;

        if (!operation.RequestBody.Content.TryGetValue("application/json", out var jsonContent))
            return;

        jsonContent.Example = new OpenApiObject
        {
            ["planName"] = new OpenApiString("Monthly budget - June 2026"),
            ["startDate"] = new OpenApiString("2026-06-12"),
            ["endDate"] = new OpenApiString("2026-06-30"),
            ["needsPct"] = new OpenApiDouble(50),
            ["wantsPct"] = new OpenApiDouble(30),
            ["savingsPct"] = new OpenApiDouble(20),
            ["categoryBudgets"] = new OpenApiArray
            {
                new OpenApiObject
                {
                    ["categoryId"] = new OpenApiString("813ef954-a26e-4efd-ba84-d2e5c8b94475"),
                    ["walletId"] = new OpenApiString("edf52db-f8b9-4039-a678-1d039adc303a"),
                    ["amountLimit"] = new OpenApiDouble(2300000),
                    ["thresholdPct"] = new OpenApiDouble(80),
                    ["thresholdType"] = new OpenApiString("PERCENT")
                },
                new OpenApiObject
                {
                    ["categoryId"] = new OpenApiString("7d8f63c8-2d76-43ed-b601-f4db29f9b7f1"),
                    ["walletId"] = new OpenApiString("edf52db-f8b9-4039-a678-1d039adc303a"),
                    ["amountLimit"] = new OpenApiDouble(1200000),
                    ["thresholdPct"] = new OpenApiDouble(80),
                    ["thresholdType"] = new OpenApiString("PERCENT")
                },
                new OpenApiObject
                {
                    ["categoryId"] = new OpenApiString("a0f1aa46-beb8-44c8-9b61-07c40f5a6c65"),
                    ["walletId"] = new OpenApiNull(),
                    ["amountLimit"] = new OpenApiDouble(800000),
                    ["thresholdPct"] = new OpenApiDouble(90),
                    ["thresholdType"] = new OpenApiString("PERCENT")
                }
            }
        };
    }

    private static bool IsCreateBudgetPlan(OperationFilterContext context)
        => string.Equals(context.MethodInfo.DeclaringType?.Name, "BudgetPlansController", StringComparison.Ordinal)
           && string.Equals(context.MethodInfo.Name, "CreateBudgetPlan", StringComparison.Ordinal);
}
