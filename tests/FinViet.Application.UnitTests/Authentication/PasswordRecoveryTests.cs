using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.ForgotPassword;
using FinViet.Application.Features.Auth.Commands.ResetPassword;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.Features.Auth.Commands.ForgotPassword;
using FinViet.Infrastructure.Features.Auth.Commands.ResetPassword;
using Moq;

namespace FinViet.Application.UnitTests.Authentication;

public class PasswordRecoveryTests
{
    // TC-AUTH-U19
    [Fact]
    public async Task ForgotPassword_ActiveCustomer_InvalidatesOldAndSendsResetCode()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        var old = TestData.EmailToken(customer, "OLD456", EmailTokenType.ResetPassword);
        db.AddRange(customer, old);
        await db.SaveChangesAsync();
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await new ForgotPasswordCommandHandler(db, email.Object)
            .Handle(new ForgotPasswordCommand("CUSTOMER@EXAMPLE.COM"), default);

        Assert.NotNull(old.UsedAt);
        var created = Assert.Single(db.EmailVerificationTokens.Where(x => x.TokenId != old.TokenId));
        Assert.Equal(EmailTokenType.ResetPassword, created.TokenType);
        email.Verify(x => x.SendPasswordResetEmailAsync(customer.Email, customer.FullName, created.Token), Times.Once);
    }

    // TC-AUTH-U20
    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsGenericResponseWithoutEmail()
    {
        await using var db = TestDbContextFactory.Create();
        var email = new Mock<IEmailService>();

        var result = await new ForgotPasswordCommandHandler(db, email.Object)
            .Handle(new ForgotPasswordCommand("unknown@example.com"), default);

        Assert.Contains("If this email is registered", result);
        Assert.Empty(db.EmailVerificationTokens);
        email.Verify(x => x.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // TC-AUTH-U21
    [Fact]
    public async Task ResetPassword_ValidToken_ChangesPasswordConsumesTokenAndRevokesSessions()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer(password: "OldPassword1");
        var reset = TestData.EmailToken(customer, "reset-code", EmailTokenType.ResetPassword);
        var refresh = TestData.RefreshToken(customer);
        db.AddRange(customer, reset, refresh);
        await db.SaveChangesAsync();

        await new ResetPasswordCommandHandler(db)
            .Handle(new ResetPasswordCommand("reset-code", "NewPassword1", "NewPassword1"), default);

        Assert.True(BCrypt.Net.BCrypt.Verify("NewPassword1", customer.PasswordHash));
        Assert.NotNull(reset.UsedAt);
        Assert.True(refresh.IsRevoked);
    }

    // TC-AUTH-U22
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ResetPassword_UsedOrExpiredToken_ThrowsBadRequest(bool used, bool expired)
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        var reset = TestData.EmailToken(customer, "reset-code", EmailTokenType.ResetPassword,
            expiresAt: expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(10),
            usedAt: used ? DateTime.UtcNow : null);
        db.AddRange(customer, reset);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BadRequestException>(() => new ResetPasswordCommandHandler(db)
            .Handle(new ResetPasswordCommand("reset-code", "NewPassword1", "NewPassword1"), default));
    }
}
