using FinViet.Application.DTOs;
using FinViet.Application.Features.Profile.Commands.ApplySavingsPlanRecommendation;
using FinViet.Application.Features.Profile.Queries.GetSavingsPlanRecommendation;
using FinViet.Infrastructure.Services;
using MediatR;

namespace FinViet.Application.UnitTests;

// TC-SAVEPLAN-15..16 — the savings-plan request/handler pair is wired where MediatR actually
// looks. Program.cs registers MediatR against the *Infrastructure* assembly only, so a handler
// left in Application still compiles and then fails at runtime with "no handler registered" the
// first time the endpoint is called. That gap is invisible to every other test here, hence this
// one.
public class SavingsPlanHandlerWiringTests
{
    private static readonly Type[] InfrastructureTypes =
        typeof(IncomeAllocationService).Assembly.GetTypes();

    private static Type? HandlerFor(Type requestType, Type responseType)
    {
        var handlerInterface = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
        return Array.Find(
            InfrastructureTypes,
            t => t is { IsAbstract: false, IsInterface: false }
                 && handlerInterface.IsAssignableFrom(t));
    }

    [Fact]
    public void GetSavingsPlanRecommendationQuery_HasHandlerInTheScannedAssembly()
    {
        var handler = HandlerFor(
            typeof(GetSavingsPlanRecommendationQuery), typeof(SavingsPlanRecommendationDto));

        Assert.NotNull(handler);
    }

    [Fact]
    public void ApplySavingsPlanRecommendationCommand_HasHandlerInTheScannedAssembly()
    {
        var handler = HandlerFor(
            typeof(ApplySavingsPlanRecommendationCommand), typeof(IncomeAllocationEntryDto));

        Assert.NotNull(handler);
    }
}
