using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.GoogleLogin;
using FinViet.Application.Features.Auth.Commands.Login;
using FinViet.Application.Features.Auth.Commands.Logout;
using FinViet.Application.Features.Auth.Commands.RefreshToken;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Features.Auth.Commands.GoogleLogin;
using FinViet.Infrastructure.Features.Auth.Commands.Login;
using FinViet.Infrastructure.Features.Auth.Commands.Logout;
using FinViet.Infrastructure.Features.Auth.Commands.RefreshToken;
using Microsoft.Extensions.Configuration;
using Moq;

namespace FinViet.Application.UnitTests.Authentication;

public class LoginTokenTests
{
    // TC-AUTH-U12
    [Fact]
    public async Task Login_ValidVerifiedCredentials_ReturnsAndPersistsTokens()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var jwt = Jwt("access", "refresh");

        var result = await new LoginCommandHandler(db, jwt.Object, Configuration())
            .Handle(new LoginCommand("CUSTOMER@EXAMPLE.COM", "Password1"), default);

        Assert.Equal("access", result.AccessToken);
        Assert.Equal("refresh", result.RefreshToken);
        Assert.Equal("refresh", Assert.Single(db.RefreshTokens).Token);
    }

    // TC-AUTH-U13
    [Theory]
    [InlineData(false, true, "Password1")]
    [InlineData(true, false, "Password1")]
    [InlineData(true, true, "WrongPassword1")]
    public async Task Login_InvalidAccountStateOrPassword_ThrowsExpectedException(
        bool verified, bool active, string password)
    {
        await using var db = TestDbContextFactory.Create();
        db.Customers.Add(TestData.Customer(isEmailVerified: verified, isActive: active));
        await db.SaveChangesAsync();
        var handler = new LoginCommandHandler(db, Jwt().Object, Configuration());

        if (!verified && active && password == "Password1")
            await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(new LoginCommand("customer@example.com", password), default));
        else
            await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(new LoginCommand("customer@example.com", password), default));
        Assert.Empty(db.RefreshTokens);
    }

    // TC-AUTH-U14
    [Fact]
    public async Task GoogleLogin_InvalidFirebaseToken_ThrowsUnauthorized()
    {
        await using var db = TestDbContextFactory.Create();
        var firebase = new Mock<IFirebaseAuthService>();
        firebase.Setup(x => x.VerifyIdTokenAsync("invalid")).ReturnsAsync((FirebaseUserInfo?)null);
        var handler = new GoogleLoginCommandHandler(db, firebase.Object,
            new LoginCommandHandler(db, Jwt().Object, Configuration()));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new GoogleLoginCommand("invalid"), default));
    }

    // TC-AUTH-U15
    [Fact]
    public async Task GoogleLogin_ExistingEmailAccount_LinksGoogleIdAndIssuesTokens()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var firebase = new Mock<IFirebaseAuthService>();
        firebase.Setup(x => x.VerifyIdTokenAsync("google"))
            .ReturnsAsync(new FirebaseUserInfo("firebase-user", customer.Email, "Name", null, true));

        var result = await new GoogleLoginCommandHandler(db, firebase.Object,
                new LoginCommandHandler(db, Jwt("google-access", "google-refresh").Object, Configuration()))
            .Handle(new GoogleLoginCommand("google"), default);

        Assert.Equal("firebase-user", customer.GoogleId);
        Assert.Equal("google-access", result.AccessToken);
    }

    // TC-AUTH-U16
    [Fact]
    public async Task RefreshToken_ValidToken_RotatesAndRevokesOriginal()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        var old = TestData.RefreshToken(customer, "old-refresh");
        db.AddRange(customer, old);
        await db.SaveChangesAsync();

        var result = await new RefreshTokenCommandHandler(db, Jwt("new-access", "new-refresh").Object, Configuration())
            .Handle(new RefreshTokenCommand("old-refresh"), default);

        Assert.True(old.IsRevoked);
        Assert.NotNull(old.RevokedAt);
        Assert.Equal("new-refresh", result.RefreshToken);
        Assert.Contains(db.RefreshTokens, x => x.Token == "new-refresh" && !x.IsRevoked);
    }

    // TC-AUTH-U17
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RefreshToken_RevokedOrExpired_ThrowsUnauthorized(bool revoked, bool expired)
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        var token = TestData.RefreshToken(customer, isRevoked: revoked,
            expiresAt: expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(10));
        db.AddRange(customer, token);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            new RefreshTokenCommandHandler(db, Jwt().Object, Configuration())
                .Handle(new RefreshTokenCommand(token.Token), default));
    }

    // TC-AUTH-U18
    [Fact]
    public async Task Logout_ActiveToken_RevokesTokenAndRemainsIdempotent()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = TestData.Customer();
        var token = TestData.RefreshToken(customer);
        db.AddRange(customer, token);
        await db.SaveChangesAsync();
        var handler = new LogoutCommandHandler(db);

        await handler.Handle(new LogoutCommand(token.Token), default);
        var revokedAt = token.RevokedAt;
        await handler.Handle(new LogoutCommand(token.Token), default);

        Assert.True(token.IsRevoked);
        Assert.Equal(revokedAt, token.RevokedAt);
    }

    private static Mock<IJwtTokenService> Jwt(string access = "access", string refresh = "refresh")
    {
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(x => x.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), "Customer"))
            .Returns(access);
        jwt.Setup(x => x.GenerateRefreshToken()).Returns(refresh);
        return jwt;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:RefreshTokenExpiryDays"] = "7", ["Jwt:AccessTokenExpiryMinutes"] = "15"
        }).Build();
}
