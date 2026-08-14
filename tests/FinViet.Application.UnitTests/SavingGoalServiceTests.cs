using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FinViet.Application.UnitTests;

public class SavingGoalServiceTests
{
    // TC-GOAL-U01
    [Fact]
    public async Task GetContributions_MixOfContributionsAndWithdrawals_ReturnsNewestFirst()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var goal = Goal(customerId, "Trip");
        db.SavingGoals.Add(goal);
        db.SavingGoalContributions.AddRange(
            Contribution(goal.GoalId, 100m, "contribution", DateTime.UtcNow.AddDays(-2)),
            Contribution(goal.GoalId, 40m, "withdrawal", DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await new SavingGoalService(db, Mock.Of<INotificationService>())
            .GetContributionsAsync(customerId, goal.GoalId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("withdrawal", result[0].Type);
        Assert.Equal("contribution", result[1].Type);
    }

    // TC-GOAL-U02
    [Fact]
    public async Task GetContributions_GoalNotOwnedByCaller_ReturnsNull()
    {
        await using var db = TestDbContextFactory.Create();
        var goal = Goal(Guid.NewGuid(), "Private");
        db.SavingGoals.Add(goal);
        await db.SaveChangesAsync();

        var result = await new SavingGoalService(db, Mock.Of<INotificationService>())
            .GetContributionsAsync(Guid.NewGuid(), goal.GoalId);

        Assert.Null(result);
    }

    // TC-GOAL-U03
    [Fact]
    public async Task GetContributions_ArchivedGoal_ReturnsPreservedLedger()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var goal = Goal(customerId, "Archived");
        goal.IsDeleted = true;
        db.SavingGoals.Add(goal);
        db.SavingGoalContributions.Add(Contribution(
            goal.GoalId,
            100m,
            "contribution",
            DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await new SavingGoalService(db, Mock.Of<INotificationService>())
            .GetContributionsAsync(customerId, goal.GoalId);

        Assert.NotNull(result);
        Assert.Single(result!);
    }

    // TC-GOAL-U04
    [Fact]
    public void ValidateNote_WithinLimit_ReturnsTrimmedValue()
    {
        var result = SavingGoalService.ValidateNote("  monthly top-up  ");

        Assert.Equal("monthly top-up", result);
    }

    // TC-GOAL-U05
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNote_NullOrBlank_ReturnsNull(string? note)
    {
        Assert.Null(SavingGoalService.ValidateNote(note));
    }

    // TC-GOAL-U06
    [Fact]
    public void ValidateNote_ExceedsMaxLength_ThrowsValidationException()
    {
        var tooLong = new string('a', 256);

        Assert.Throws<ValidationException>(() => SavingGoalService.ValidateNote(tooLong));
    }

    // TC-GOAL-U07
    [Fact]
    public async Task GetGoals_FiltersActiveAndArchivedGoals()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var active = Goal(customerId, "Active");
        var archived = Goal(customerId, "Archived");
        archived.IsDeleted = true;
        db.SavingGoals.AddRange(active, archived);
        await db.SaveChangesAsync();

        var service = new SavingGoalService(db, Mock.Of<INotificationService>());
        var activeResult = await service.GetGoalsAsync(customerId);
        var archivedResult = await service.GetGoalsAsync(customerId, archived: true);

        Assert.Equal(active.GoalId, Assert.Single(activeResult).GoalId);
        Assert.Equal(archived.GoalId, Assert.Single(archivedResult).GoalId);
        Assert.True(archivedResult[0].IsDeleted);
    }

    // TC-GOAL-U08
    [Fact]
    public async Task GetGoalById_ArchivedGoal_ReturnsTruthfulResponseFields()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.AddDays(-2);
        var updatedAt = DateTime.UtcNow.AddDays(-1);
        var goal = Goal(customerId, "Archived");
        goal.IconEmoji = "🎯";
        goal.Deadline = null;
        goal.IsDeleted = true;
        goal.CreatedAt = createdAt;
        goal.UpdatedAt = updatedAt;
        db.SavingGoals.Add(goal);
        await db.SaveChangesAsync();

        var result = await new SavingGoalService(db, Mock.Of<INotificationService>())
            .GetGoalByIdAsync(customerId, goal.GoalId);

        Assert.NotNull(result);
        Assert.Equal("🎯", result!.IconEmoji);
        Assert.Null(result.Deadline);
        Assert.True(result.IsDeleted);
        Assert.Equal(createdAt, result.CreatedAt);
        Assert.Equal(updatedAt, result.UpdatedAt);
    }

    private static SavingGoal Goal(Guid customerId, string name)
        => new()
        {
            GoalId = Guid.NewGuid(),
            CustomerId = customerId,
            GoalName = name,
            TargetAmount = 1000m,
            CurrentAmount = 100m,
            IsDeleted = false
        };

    private static SavingGoalContribution Contribution(Guid goalId, decimal amount, string type, DateTime contributedAt)
        => new()
        {
            ContributionId = Guid.NewGuid(),
            GoalId = goalId,
            Amount = amount,
            Type = type,
            ContributionDate = contributedAt
        };
}
