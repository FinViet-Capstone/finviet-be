using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Exceptions;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;

namespace FinViet.Application.UnitTests;

public class CategoryRulesTests
{
    // TC-CAT-U01
    [Theory]
    [InlineData(" INCOME ", "income")]
    [InlineData("Expense", "expense")]
    public void NormalizeType_AllowedValue_ReturnsCanonicalValue(string input, string expected)
        => Assert.Equal(expected, CategoryRules.NormalizeType(input));

    // TC-CAT-U02
    [Fact]
    public void NormalizeType_UnsupportedValue_ThrowsValidationException()
        => Assert.Throws<ValidationException>(() => CategoryRules.NormalizeType("transfer"));

    // TC-CAT-U03
    [Theory]
    [InlineData(" Needs ", "needs")]
    [InlineData("WANTS", "wants")]
    [InlineData("savings", "savings")]
    public void NormalizeExpenseClass_ExpenseWithAllowedBucket_ReturnsCanonicalValue(string input, string expected)
        => Assert.Equal(expected, CategoryRules.NormalizeExpenseClass(input, "expense"));

    // TC-CAT-U04
    [Fact]
    public void NormalizeExpenseClass_IncomeWithBucket_ClearsBucket()
        => Assert.Null(CategoryRules.NormalizeExpenseClass("needs", "income"));

    // TC-CAT-U05
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("other")]
    public void NormalizeExpenseClass_ExpenseWithoutAllowedBucket_ThrowsValidationException(string? input)
        => Assert.Throws<ValidationException>(() => CategoryRules.NormalizeExpenseClass(input, "expense"));

    // TC-CAT-U06
    [Fact]
    public void EnsureCustomerBucketCanBeSet_IncomeCategory_ThrowsValidationException()
        => Assert.Throws<ValidationException>(() => CategoryRules.EnsureCustomerBucketCanBeSet("cat_salary", "income"));

    // TC-CAT-U07
    [Fact]
    public void EnsureCustomerBucketCanBeSet_SavingsGoalCategory_ThrowsValidationException()
        => Assert.Throws<ValidationException>(() =>
            CategoryRules.EnsureCustomerBucketCanBeSet(CategoryRules.SavingsGoalCategoryId, "expense"));

    // TC-CAT-U08
    [Fact]
    public void Slugify_VietnameseName_ReturnsStableAsciiSlug()
        => Assert.Equal("an_uong_hang_ngay", CategoryRules.Slugify("Ăn uống hằng ngày"));

    // TC-CAT-U09
    [Fact]
    public void FirstNonEmpty_NameViMissing_FallsBackToCategoryName()
        => Assert.Equal("Chi tiêu", CategoryRules.FirstNonEmpty(" ", "Chi tiêu"));
}

public class WalletRulesTests
{
    private static CreateWalletRequest CreateRequest(string name = "Cash", string type = "basic", decimal balance = 0m)
        => new() { WalletName = name, WalletType = type, InitialBalance = balance };

    // TC-WAL-U01
    [Theory]
    [InlineData("", "basic", 0)]
    [InlineData("Cash", "", 0)]
    [InlineData("Cash", "basic", -1)]
    public void ValidateCreate_InvalidCoreInput_ThrowsValidationException(string name, string type, decimal balance)
        => Assert.Throws<ValidationException>(() => WalletRules.ValidateCreate(CreateRequest(name, type, balance)));

    // TC-WAL-U02
    [Fact]
    public void ValidateCreate_ZeroOpeningBalance_IsAllowed()
        => WalletRules.ValidateCreate(CreateRequest(balance: 0m));

    // TC-WAL-U03
    [Theory]
    [InlineData("basic")]
    [InlineData("BASIC")]
    [InlineData("cash")]
    [InlineData("BANK_ACCOUNT")]
    [InlineData("credit_card")]
    [InlineData("E_WALLET")]
    [InlineData("investment")]
    public void NormalizeWalletType_ManualTypeOrLegacyAlias_ReturnsBasic(string type)
        => Assert.Equal("basic", WalletRules.NormalizeWalletType(type));

    // TC-WAL-U04
    [Theory]
    [InlineData("sepay_linked")]
    [InlineData("finverse_linked")]
    [InlineData("nonsense")]
    public void NormalizeWalletType_LinkedOrUnknownManualType_ThrowsValidationException(string type)
        => Assert.Throws<ValidationException>(() => WalletRules.NormalizeWalletType(type));

    // TC-WAL-U05
    [Fact]
    public void ValidateUpdate_BlankName_ThrowsValidationException()
        => Assert.Throws<ValidationException>(() => WalletRules.ValidateUpdate(new UpdateWalletRequest { WalletName = " " }));

    // TC-WAL-U06
    [Fact]
    public void ValidateUpdate_WalletTypeProvided_ThrowsValidationException()
        => Assert.Throws<ValidationException>(() => WalletRules.ValidateUpdate(new UpdateWalletRequest { WalletType = "basic" }));

    // TC-WAL-U07
    [Theory]
    [InlineData("CASH", "basic")]
    [InlineData("BANK_ACCOUNT", "basic")]
    [InlineData("SEPAY_LINKED", "sepay_linked")]
    public void NormalizeStoredWalletType_StoredValue_ReturnsApiValue(string stored, string expected)
        => Assert.Equal(expected, WalletRules.NormalizeStoredWalletType(stored));

    // TC-WAL-U08
    [Fact]
    public void GetRequiredCustomerId_NullValue_ThrowsInvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() => WalletRules.GetRequiredCustomerId(new Wallet { WalletId = Guid.NewGuid() }));

    // TC-WAL-U09
    [Fact]
    public void GetRequiredBalance_NullValue_ThrowsInvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() => WalletRules.GetRequiredBalance(new Wallet { WalletId = Guid.NewGuid() }));
}
