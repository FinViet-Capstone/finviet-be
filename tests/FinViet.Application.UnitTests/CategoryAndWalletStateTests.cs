using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Categories;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Application.UnitTests;

public class CategoryServiceStateTests
{
    // TC-CAT-U10
    [Fact]
    public async Task GetCategories_CustomerOverrideExists_OverrideWinsOverGlobalDefault()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Categories.AddRange(
            Category("cat_travel", "Travel", "expense", "needs", 2),
            Category("cat_food", "Food", "expense", "needs", 1),
            Category("cat_savings_goal", "Savings goal", "expense", "savings", 0));
        db.CustomerCategories.Add(Override(customerId, "cat_food", "wants"));
        await db.SaveChangesAsync();

        var result = await new CategoryService(db).GetCategoriesAsync("expense", customerId);

        Assert.Equal(new[] { "cat_food", "cat_travel" }, result.Select(x => x.CategoryId));
        Assert.Equal("wants", result[0].ExpenseClass);
        Assert.DoesNotContain(result, x => x.CategoryId == "cat_savings_goal");
    }

    // TC-CAT-U11
    [Fact]
    public async Task GetCategoryById_CustomCategoryOwnedByAnotherCustomer_ReturnsNull()
    {
        await using var db = TestDbContextFactory.Create();
        var categoryId = $"custom_{Guid.NewGuid()}";
        db.Categories.Add(Category(categoryId, "Private", "expense", "needs", 1));
        db.CustomerCategories.Add(Override(Guid.NewGuid(), categoryId, "needs"));
        await db.SaveChangesAsync();

        var result = await new CategoryService(db).GetCategoryByIdAsync(categoryId, Guid.NewGuid());

        Assert.Null(result);
    }

    // TC-BKT-U01
    [Fact]
    public async Task SetCustomerBucket_NewOverride_CreatesActivePersonaOverride()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Categories.Add(Category("cat_food", "Food", "expense", "needs", 1));
        await db.SaveChangesAsync();

        var result = await new CategoryService(db).SetCustomerBucketAsync(customerId, "cat_food", " WANTS ");

        var row = await db.CustomerCategories.SingleAsync();
        Assert.Equal("wants", result.ExpenseClass);
        Assert.Equal("wants", row.BucketId);
        Assert.True(row.IsActive);
        Assert.Equal("persona", row.Source);
    }

    // TC-BKT-U02
    [Fact]
    public async Task ResetCustomerBucket_ActiveOverride_DeactivatesAndReturnsGlobalDefault()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Categories.Add(Category("cat_food", "Food", "expense", "needs", 1));
        db.CustomerCategories.Add(Override(customerId, "cat_food", "wants"));
        await db.SaveChangesAsync();

        var result = await new CategoryService(db).ResetCustomerBucketAsync(customerId, "cat_food");

        Assert.Equal("needs", result.ExpenseClass);
        Assert.False((await db.CustomerCategories.SingleAsync()).IsActive);
    }

    // TC-BKT-U03
    [Fact]
    public async Task SetCustomerBucket_MissingCategory_ThrowsNotFoundException()
    {
        await using var db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<FinViet.Application.Exceptions.NotFoundException>(() =>
            new CategoryService(db).SetCustomerBucketAsync(Guid.NewGuid(), "missing", "needs"));
    }

    private static Category Category(string id, string name, string type, string? bucket, int order)
        => new()
        {
            CategoryId = id,
            CategoryName = name,
            NameEn = name,
            Type = type,
            DefaultBucket = bucket,
            SortOrder = order
        };

    private static CustomerCategory Override(Guid customerId, string categoryId, string bucket)
        => new()
        {
            Id = Guid.NewGuid(), CustomerId = customerId, CategoryId = categoryId,
            BucketId = bucket, Source = "persona", IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
}

public class WalletServiceStateTests
{
    // TC-WAL-U10
    [Fact]
    public async Task GetWallets_MixedOwnershipAndDeletion_ReturnsOwnedActiveSortedWithTotal()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Wallets.AddRange(
            Wallet(customerId, "Zulu", "basic", 200m),
            Wallet(customerId, "Alpha", "basic", 100m),
            Wallet(customerId, "Deleted", "basic", 999m, true),
            Wallet(Guid.NewGuid(), "Other", "basic", 500m));
        await db.SaveChangesAsync();

        var result = await new WalletService(db).GetWalletsAsync(customerId);

        Assert.Equal(300m, result.TotalBalance);
        Assert.Equal(new[] { "Alpha", "Zulu" }, result.Wallets.Select(x => x.WalletName));
        Assert.All(result.Wallets, x => Assert.Equal("basic", x.WalletType));
    }

    // TC-WAL-U11
    [Fact]
    public async Task GetWalletById_OtherCustomersWallet_ReturnsNull()
    {
        await using var db = TestDbContextFactory.Create();
        var wallet = Wallet(Guid.NewGuid(), "Private", "basic", 100m);
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();

        Assert.Null(await new WalletService(db).GetWalletByIdAsync(Guid.NewGuid(), wallet.WalletId));
    }

    // TC-WAL-U12
    [Fact]
    public async Task DeleteWallet_NonLastWallet_SoftDeletesIt()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var deleted = Wallet(customerId, "Delete", "basic", 100m);
        db.Wallets.AddRange(deleted, Wallet(customerId, "Keep", "basic", 0m));
        await db.SaveChangesAsync();

        var result = await new WalletService(db).DeleteWalletAsync(customerId, deleted.WalletId);

        Assert.True(result);
        Assert.True((await db.Wallets.SingleAsync(x => x.WalletId == deleted.WalletId)).IsDeleted);
    }

    // TC-WAL-U13
    [Fact]
    public async Task DeleteWallet_LastActiveWallet_ThrowsLastWalletBusinessRule()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var wallet = Wallet(customerId, "Only", "basic", 0m);
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new WalletService(db).DeleteWalletAsync(customerId, wallet.WalletId));

        Assert.Equal("last_wallet", error.Code);
        Assert.False(wallet.IsDeleted);
    }

    // TC-WAL-U14
    [Fact]
    public async Task DeleteWallet_MissingOrCrossCustomerWallet_ReturnsFalse()
    {
        await using var db = TestDbContextFactory.Create();
        var wallet = Wallet(Guid.NewGuid(), "Other", "basic", 0m);
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();

        Assert.False(await new WalletService(db).DeleteWalletAsync(Guid.NewGuid(), wallet.WalletId));
        Assert.False(wallet.IsDeleted);
    }

    private static Wallet Wallet(Guid customerId, string name, string type, decimal balance, bool deleted = false)
        => new()
        {
            WalletId = Guid.NewGuid(), CustomerId = customerId, WalletName = name,
            WalletType = type, Balance = balance, IsDeleted = deleted
        };
}
