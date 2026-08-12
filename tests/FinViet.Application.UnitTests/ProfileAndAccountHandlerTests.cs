using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Account.Commands.DeactivateAccount;
using FinViet.Application.Features.Account.Commands.DeleteAccount;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.Profile.Commands.UpdateAiPreferences;
using FinViet.Application.Features.Profile.Commands.UpdateProfile;
using FinViet.Application.Features.Profile.Commands.UploadAvatar;
using FinViet.Application.Features.Profile.Queries.GetProfile;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.Features.Account.Commands.DeactivateAccount;
using FinViet.Infrastructure.Features.Account.Commands.DeleteAccount;
using FinViet.Infrastructure.Features.Profile.Commands.UpdateAiPreferences;
using FinViet.Infrastructure.Features.Profile.Commands.UpdateProfile;
using FinViet.Infrastructure.Features.Profile.Commands.UploadAvatar;
using FinViet.Infrastructure.Features.Profile.Queries.GetProfile;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FinViet.Application.UnitTests;

public sealed class ProfileAndAccountHandlerTests
{
    // TC-PROF-U03
    [Fact]
    public async Task GetProfile_ActiveCustomer_ReturnsProfileFields()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        customer.Gender = Gender.Female;
        customer.DateOfBirth = new DateOnly(1999, 2, 3);
        customer.MonthlyIncomeExpected = 15_000_000m;
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var actual = await new GetProfileQueryHandler(db)
            .Handle(new GetProfileQuery(customer.CustomerId), CancellationToken.None);

        Assert.Equal(customer.CustomerId, actual.CustomerId);
        Assert.Equal(customer.Email, actual.Email);
        Assert.Equal(customer.Gender, actual.Gender);
        Assert.Equal(customer.MonthlyIncomeExpected, actual.MonthlyIncomeExpected);
    }

    // TC-PROF-U04
    [Fact]
    public async Task GetProfile_InactiveCustomer_ThrowsNotFoundException()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer(isActive: false);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => new GetProfileQueryHandler(db)
            .Handle(new GetProfileQuery(customer.CustomerId), CancellationToken.None));
    }

    // TC-PROF-U05
    [Fact]
    public async Task UpdateProfile_OptionalValuesOmitted_TrimsNameAndPreservesExistingValues()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        customer.MonthlyIncomeExpected = 4_000_000m;
        customer.Gender = Gender.Male;
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var result = await new UpdateProfileCommandHandler(db).Handle(
            new UpdateProfileCommand(customer.CustomerId, "  Updated name  ", null, OnboardingDone: true),
            CancellationToken.None);

        Assert.Equal("Updated name", result.FullName);
        Assert.Equal(4_000_000m, result.MonthlyIncomeExpected);
        Assert.Equal(Gender.Male, result.Gender);
        Assert.True(result.OnboardingDone);
    }

    // TC-PROF-U06
    [Fact]
    public async Task UpdateProfile_AllocationAfterOnboarding_ThrowsBusinessRuleException()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        customer.OnboardingDone = true;
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => new UpdateProfileCommandHandler(db).Handle(
            new UpdateProfileCommand(customer.CustomerId, "Name", 10m), CancellationToken.None));

        Assert.Equal("allocation_locked_use_schedule_endpoint", error.Code);
    }

    // TC-PROF-U07
    [Theory]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0x00 })]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 })]
    public void AvatarValidation_SupportedMimeWithMatchingSignature_IsAccepted(string contentType, byte[] content)
        => AvatarValidationRules.Validate(content, contentType);

    // TC-PROF-U08
    [Theory]
    [InlineData("image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38 })]
    [InlineData("image/png", new byte[] { 0xFF, 0xD8, 0xFF, 0x00 })]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46 })]
    public void AvatarValidation_UnsupportedMimeOrMismatchedSignature_ThrowsBadRequest(
        string contentType, byte[] content)
        => Assert.Throws<BadRequestException>(() => AvatarValidationRules.Validate(content, contentType));

    // TC-PROF-U09
    [Fact]
    public void AvatarValidation_OverFiveMegabytes_ThrowsBadRequest()
        => Assert.Throws<BadRequestException>(() =>
            AvatarValidationRules.Validate(new byte[(5 * 1024 * 1024) + 1], "image/png"));

    // TC-PROF-U10
    [Fact]
    public async Task UploadAvatar_ExistingAvatar_DeletesOldUploadsNewAndPersistsUrl()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        customer.AvatarUrl = "/avatars/old.png";
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var avatar = new Mock<IAvatarService>(MockBehavior.Strict);
        avatar.Setup(x => x.DeleteAsync("/avatars/old.png")).Returns(Task.CompletedTask);
        avatar.Setup(x => x.UploadAsync(It.IsAny<byte[]>(), "new.png", "image/png"))
            .ReturnsAsync("/avatars/new.png");

        var result = await new UploadAvatarCommandHandler(db, avatar.Object).Handle(
            new UploadAvatarCommand(customer.CustomerId, [0x89, 0x50, 0x4E, 0x47], "new.png", "image/png"),
            CancellationToken.None);

        Assert.Equal("/avatars/new.png", result);
        Assert.Equal("/avatars/new.png", (await db.Customers.SingleAsync()).AvatarUrl);
        avatar.VerifyAll();
    }

    [Fact]
    public async Task UpdateAiPreferences_PersistsPatchAndAuditsOnlyChangedFieldNames()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var telemetry = new Mock<IAiTelemetryRecorder>(MockBehavior.Strict);
        AiAuditRecord? audit = null;
        telemetry.Setup(x => x.RecordAuditAsync(
                It.IsAny<AiAuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiAuditRecord, CancellationToken>((record, _) => audit = record)
            .Returns(Task.CompletedTask);
        var handler = new UpdateAiPreferencesCommandHandler(db, telemetry.Object);

        var result = await handler.Handle(
            new UpdateAiPreferencesCommand(
                customer.CustomerId,
                CategorizationMode: "high_confidence_auto",
                ShareBalances: false),
            CancellationToken.None);

        Assert.Equal("high_confidence_auto", result.CategorizationMode);
        Assert.False(result.ShareBalances);
        Assert.NotNull(audit);
        Assert.Equal("ai_preference_updated", audit!.EventType);
        var metadataJson = System.Text.Json.JsonSerializer.Serialize(audit.Metadata);
        Assert.Contains("categorizationMode", metadataJson);
        Assert.Contains("shareBalances", metadataJson);
        Assert.DoesNotContain("high_confidence_auto", metadataJson);
        Assert.DoesNotContain("false", metadataJson, StringComparison.OrdinalIgnoreCase);
    }

    // TC-ACC-U01
    [Fact]
    public async Task DeleteAccount_ActiveCustomer_SoftDeletesAndRevokesActiveTokens()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        var active = TestData.RefreshToken(customer, isRevoked: false);
        var revoked = TestData.RefreshToken(customer, token: "revoked-token", isRevoked: true);
        db.Customers.Add(customer);
        db.RefreshTokens.AddRange(active, revoked);
        await db.SaveChangesAsync();

        await new DeleteAccountCommandHandler(db)
            .Handle(new DeleteAccountCommand(customer.CustomerId), CancellationToken.None);

        Assert.False(customer.IsActive);
        Assert.NotNull(customer.DeletedAt);
        Assert.True(active.IsRevoked);
        Assert.NotNull(active.RevokedAt);
        Assert.Null(revoked.RevokedAt);
    }

    // TC-ACC-U02
    [Fact]
    public async Task DeactivateAccount_TargetExists_DeactivatesWithoutDeletedAtAndRevokesTokens()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        var token = TestData.RefreshToken(customer, isRevoked: false);
        db.Customers.Add(customer);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        await new DeactivateAccountCommandHandler(db)
            .Handle(new DeactivateAccountCommand(customer.CustomerId), CancellationToken.None);

        Assert.False(customer.IsActive);
        Assert.Null(customer.DeletedAt);
        Assert.True(token.IsRevoked);
    }

    // TC-ACC-U03
    [Fact]
    public async Task DeactivateAccount_UnknownCustomer_ThrowsNotFoundException()
    {
        await using var db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() => new DeactivateAccountCommandHandler(db)
            .Handle(new DeactivateAccountCommand(Guid.NewGuid()), CancellationToken.None));
    }

}
