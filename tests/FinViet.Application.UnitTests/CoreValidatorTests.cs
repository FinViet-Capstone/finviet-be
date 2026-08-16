using FinViet.Application.Features.Auth.Commands.Login;
using FinViet.Application.Features.Auth.Commands.Register;
using FinViet.Application.Features.Auth.Commands.ResetPassword;
using FinViet.Application.Features.Profile.Commands.UpdateProfile;

namespace FinViet.Application.UnitTests;

public class CoreValidatorTests
{
    // TC-AUTH-U01
    [Fact]
    public void RegisterValidator_ValidRegistration_IsValid()
    {
        var result = new RegisterCommandValidator().Validate(
            new RegisterCommand("Nguyen Van A", "user@example.com", "Password1"));

        Assert.True(result.IsValid);
    }

    // TC-AUTH-U02
    [Theory]
    [InlineData("", "user@example.com", "Password1", "FullName")]
    [InlineData("User", "not-an-email", "Password1", "Email")]
    [InlineData("User", "user@example.com", "short1A", "Password")]
    [InlineData("User", "user@example.com", "password1", "Password")]
    [InlineData("User", "user@example.com", "Password", "Password")]
    public void RegisterValidator_InvalidCoreInput_HasExpectedPropertyError(
        string fullName, string email, string password, string property)
    {
        var result = new RegisterCommandValidator().Validate(new RegisterCommand(fullName, email, password));

        Assert.Contains(result.Errors, error => error.PropertyName == property);
    }

    // TC-AUTH-U03
    [Theory]
    [InlineData("", "Password1")]
    [InlineData("bad-email", "Password1")]
    [InlineData("user@example.com", "")]
    public void LoginValidator_InvalidCredentialsShape_IsInvalid(string email, string password)
        => Assert.False(new LoginCommandValidator().Validate(new LoginCommand(email, password)).IsValid);

    // TC-AUTH-U04
    [Theory]
    [InlineData("", "Password1", "Password1")]
    [InlineData("ABC123", "weak", "weak")]
    [InlineData("ABC123", "Password1", "Password2")]
    public void ResetPasswordValidator_InvalidRequest_IsInvalid(string token, string password, string confirmation)
        => Assert.False(new ResetPasswordCommandValidator()
            .Validate(new ResetPasswordCommand(token, password, confirmation)).IsValid);

    // TC-PROF-U01
    [Fact]
    public void UpdateProfileValidator_ValidAllocation_IsValid()
    {
        var command = new UpdateProfileCommand(Guid.NewGuid(), "User", 10_000_000m,
            NeedsPct: 50, WantsPct: 30, SavingsPct: 20);

        Assert.True(new UpdateProfileCommandValidator().Validate(command).IsValid);
    }

    // TC-PROF-U02
    // decimal isn't a valid attribute-argument type, so InlineData can't carry it —
    // MemberData sidesteps that (also lets Nullable<decimal> box the actual decimal
    // values directly, avoiding a reflection Invoke quirk where InlineData's boxed
    // int literals fail to convert into a decimal? parameter).
    public static IEnumerable<object?[]> InvalidIncomeOrAllocationCases =>
        new List<object?[]>
        {
            new object?[] { -1m, null, null, null },
            new object?[] { 1m, 50m, null, 50m },
            new object?[] { 1m, 60m, 30m, 20m },
            new object?[] { 1m, 101m, 0m, -1m },
        };

    [Theory]
    [MemberData(nameof(InvalidIncomeOrAllocationCases))]
    public void UpdateProfileValidator_InvalidIncomeOrAllocation_IsInvalid(
        decimal income, decimal? needs, decimal? wants, decimal? savings)
    {
        var command = new UpdateProfileCommand(Guid.NewGuid(), "User", income,
            NeedsPct: needs, WantsPct: wants, SavingsPct: savings);

        Assert.False(new UpdateProfileCommandValidator().Validate(command).IsValid);
    }
}
