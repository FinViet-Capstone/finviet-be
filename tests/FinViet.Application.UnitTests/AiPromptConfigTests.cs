using FinViet.Application.Common;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.AiConfigs.Commands.UpdateAiPromptConfig;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Features.AiConfigs.Commands.UpdateAiPromptConfig;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FinViet.Application.UnitTests;

public class AiPromptConfigTests
{
    // ── Validator ─────────────────────────────────────────────

    [Theory]
    [InlineData("chat")]
    [InlineData("weekly_report")]
    [InlineData("score_comment")]
    [InlineData("classification")]
    public void Validator_KnownFeatureAndValidValues_Passes(string featureKey)
    {
        var validator = new UpdateAiPromptConfigCommandValidator();

        var result = validator.Validate(new UpdateAiPromptConfigCommand(
            featureKey,
            Guid.NewGuid(),
            new UpdateAiPromptConfigRequest("Bạn là trợ lý thân thiện.", 0.5m, 512)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_UnknownFeatureKey_Fails()
    {
        var validator = new UpdateAiPromptConfigCommandValidator();

        var result = validator.Validate(new UpdateAiPromptConfigCommand(
            "not_a_feature",
            Guid.NewGuid(),
            new UpdateAiPromptConfigRequest("Persona", 0.5m, 512)));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void Validator_TemperatureOutOfRange_Fails(double temperature)
    {
        var validator = new UpdateAiPromptConfigCommandValidator();

        var result = validator.Validate(new UpdateAiPromptConfigCommand(
            AiPromptFeatures.Chat,
            Guid.NewGuid(),
            new UpdateAiPromptConfigRequest("Persona", (decimal)temperature, 512)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_EmptyPersona_Fails()
    {
        var validator = new UpdateAiPromptConfigCommandValidator();

        var result = validator.Validate(new UpdateAiPromptConfigCommand(
            AiPromptFeatures.Chat,
            Guid.NewGuid(),
            new UpdateAiPromptConfigRequest("  ", 0.5m, 512)));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(8193)]
    public void Validator_MaxOutputTokensOutOfRange_Fails(int maxOutputTokens)
    {
        var validator = new UpdateAiPromptConfigCommandValidator();

        var result = validator.Validate(new UpdateAiPromptConfigCommand(
            AiPromptFeatures.Chat,
            Guid.NewGuid(),
            new UpdateAiPromptConfigRequest("Persona", 0.5m, maxOutputTokens)));

        Assert.False(result.IsValid);
    }

    // ── Update handler ────────────────────────────────────────

    [Fact]
    public async Task UpdateHandler_ExistingFeature_UpdatesRowWritesHistoryAndInvalidatesCache()
    {
        await using var db = TestDbContextFactory.Create();
        var adminId = Guid.NewGuid();
        db.Admins.Add(new Admin
        {
            AdminId = adminId,
            Username = "master",
            PasswordHash = "hash",
            Email = "master@finviet.local"
        });
        db.AiPromptConfigs.Add(new AiPromptConfig
        {
            FeatureKey = AiPromptFeatures.Chat,
            DisplayName = "Trợ lý chat",
            PersonaInstruction = "Persona cũ.",
            Temperature = 0.4m,
            MaxOutputTokens = 768,
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var provider = new Mock<IAiPromptConfigProvider>();
        var handler = new UpdateAiPromptConfigCommandHandler(db, provider.Object);

        var result = await handler.Handle(
            new UpdateAiPromptConfigCommand(
                AiPromptFeatures.Chat,
                adminId,
                new UpdateAiPromptConfigRequest("  Persona mới.  ", 0.7m, 900)),
            CancellationToken.None);

        Assert.Equal("Persona mới.", result.PersonaInstruction);
        Assert.Equal(0.7m, result.Temperature);
        Assert.Equal(900, result.MaxOutputTokens);
        Assert.Equal(adminId, result.UpdatedBy);
        Assert.Equal("master", result.UpdatedByUsername);

        var row = await db.AiPromptConfigs.SingleAsync(c => c.FeatureKey == AiPromptFeatures.Chat);
        Assert.Equal("Persona mới.", row.PersonaInstruction);
        Assert.Equal(adminId, row.UpdatedBy);

        var history = await db.AiPromptConfigHistories
            .Where(h => h.FeatureKey == AiPromptFeatures.Chat)
            .ToListAsync();
        var snapshot = Assert.Single(history);
        Assert.Equal("Persona mới.", snapshot.PersonaInstruction);
        Assert.Equal(adminId, snapshot.ChangedBy);

        provider.Verify(p => p.Invalidate(AiPromptFeatures.Chat), Times.Once);
    }

    [Fact]
    public async Task UpdateHandler_MissingFeatureRow_ThrowsNotFound()
    {
        await using var db = TestDbContextFactory.Create();
        var handler = new UpdateAiPromptConfigCommandHandler(
            db,
            Mock.Of<IAiPromptConfigProvider>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateAiPromptConfigCommand(
                AiPromptFeatures.Chat,
                Guid.NewGuid(),
                new UpdateAiPromptConfigRequest("Persona", 0.5m, 512)),
            CancellationToken.None));
    }
}
