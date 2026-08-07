using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.Register;
using FinViet.Application.Features.Auth.Commands.ResendVerificationEmail;
using FinViet.Application.Features.Auth.Commands.VerifyEmail;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.Features.Auth.Commands.Register;
using FinViet.Infrastructure.Features.Auth.Commands.ResendVerificationEmail;
using FinViet.Infrastructure.Features.Auth.Commands.VerifyEmail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinViet.Application.UnitTests.Authentication;

public class RegisterVerificationTests
{
    // TC-AUTH-U05
    [Fact]
    public async Task Register_NewEmail_PersistsCustomerTokenAndSendsVerificationEmail()
    {
        await using var db = TestDbContextFactory.Create();
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var handler = new RegisterCommandHandler(db, email.Object, Configuration(), NullLogger<RegisterCommandHandler>.Instance);

        var result = await handler.Handle(new RegisterCommand("  New Customer  ", "  NEW@EXAMPLE.COM  ", "Password1"), default);

        var customer = Assert.Single(await db.Customers.ToListAsync());
        var token = Assert.Single(await db.EmailVerificationTokens.ToListAsync());
        Assert.Equal("new@example.com", customer.Email);
        Assert.Equal("New Customer", customer.FullName);
        Assert.True(BCrypt.Net.BCrypt.Verify("Password1", customer.PasswordHash));
        Assert.False(customer.IsEmailVerified);
        Assert.Equal(EmailTokenType.VerifyEmail, token.TokenType);
        Assert.Matches("^[A-Z0-9]{6}$", token.Token);
        Assert.Contains("Registration successful", result);
        email.Verify(x => x.SendVerificationEmailAsync(customer.Email, customer.FullName, token.Token), Times.Once);
    }

    // TC-AUTH-U06
    [Fact]
    public async Task Register_DuplicateEmail_ThrowsConflictAndDoesNotSendEmail()
    {
        await using var db = TestDbContextFactory.Create();
        db.Customers.Add(TestData.Customer("existing@example.com"));
        await db.SaveChangesAsync();
        var email = new Mock<IEmailService>();
        var handler = new RegisterCommandHandler(db, email.Object, Configuration(), NullLogger<RegisterCommandHandler>.Instance);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new RegisterCommand("Customer", " EXISTING@EXAMPLE.COM ", "Password1"), default));

        email.Verify(x => x.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // TC-AUTH-U07
    [Fact]
    public async Task Register_EmailProviderFails_PersistsAccountAndReturnsWarning()
    {
        await using var db = TestDbContextFactory.Create();
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));
        var handler = new RegisterCommandHandler(db, email.Object, Configuration(), NullLogger<RegisterCommandHandler>.Instance);

        var result = await handler.Handle(new RegisterCommand("Customer", "new@example.com", "Password1"), default);

        Assert.Single(db.Customers);
        Assert.Single(db.EmailVerificationTokens);
        Assert.Contains("could not be sent", result);
    }

    // TC-AUTH-U08
    [Fact]
    public async Task VerifyEmail_ValidToken_MarksCustomerAndConsumesToken()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer(isEmailVerified: false);
        var token = TestData.EmailToken(customer, "verify-code", EmailTokenType.VerifyEmail);
        db.AddRange(customer, token);
        await db.SaveChangesAsync();

        await new VerifyEmailCommandHandler(db).Handle(new VerifyEmailCommand("verify-code"), default);

        Assert.True(customer.IsEmailVerified);
        Assert.NotNull(customer.EmailVerifiedAt);
        Assert.NotNull(token.UsedAt);
    }

    // TC-AUTH-U09
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task VerifyEmail_UsedOrExpiredToken_ThrowsBadRequest(bool used, bool expired)
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer(isEmailVerified: false);
        var token = TestData.EmailToken(customer, "verify-code", EmailTokenType.VerifyEmail,
            expiresAt: expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(10),
            usedAt: used ? DateTime.UtcNow : null);
        db.AddRange(customer, token);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            new VerifyEmailCommandHandler(db).Handle(new VerifyEmailCommand("verify-code"), default));
        Assert.False(customer.IsEmailVerified);
    }

    // TC-AUTH-U10
    [Fact]
    public async Task ResendVerification_UnverifiedCustomer_InvalidatesOldAndSendsNewCode()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer(isEmailVerified: false);
        var old = TestData.EmailToken(customer, "OLD123", EmailTokenType.VerifyEmail);
        db.AddRange(customer, old);
        await db.SaveChangesAsync();
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await new ResendVerificationEmailCommandHandler(db, email.Object, Configuration(),
            NullLogger<ResendVerificationEmailCommandHandler>.Instance)
            .Handle(new ResendVerificationEmailCommand("CUSTOMER@EXAMPLE.COM"), default);

        Assert.NotNull(old.UsedAt);
        var created = Assert.Single(db.EmailVerificationTokens.Where(x => x.TokenId != old.TokenId));
        email.Verify(x => x.SendVerificationEmailAsync(customer.Email, customer.FullName, created.Token), Times.Once);
    }

    // TC-AUTH-U11
    [Fact]
    public async Task ResendVerification_UnknownEmail_ReturnsGenericResponseWithoutEmail()
    {
        await using var db = TestDbContextFactory.Create();
        var email = new Mock<IEmailService>();

        var result = await new ResendVerificationEmailCommandHandler(db, email.Object, Configuration(),
            NullLogger<ResendVerificationEmailCommandHandler>.Instance)
            .Handle(new ResendVerificationEmailCommand("unknown@example.com"), default);

        Assert.Contains("If this email is registered", result);
        email.Verify(x => x.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().Build();
}
