using FinViet.Domain.Enums;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Persistence;

/// <summary>
/// Seeds default data (admin + demo customers) into an already-provisioned database.
/// The database schema is the source of truth and is created externally from
/// <c>db/schema_v2.1.sql</c>; this initializer NO LONGER runs SQL migrations.
/// Seeding goes through EF entities, so it stays in sync with the mapped schema.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await SeedAsync(db, logger, cancellationToken);
    }

    private static async Task SeedAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await SeedAdminAsync(db, logger, cancellationToken);
        await SeedCategoriesAsync(db, logger, cancellationToken);
        await SeedCustomersAsync(db, logger, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedAdminAsync(FinVietDbContext db, ILogger logger, CancellationToken ct)
    {
        const string adminUsername = "admin";
        var existing = await db.Admins.FirstOrDefaultAsync(a => a.Username == adminUsername, ct);
        if (existing is not null) return;

        db.Admins.Add(new Admin
        {
            AdminId = Guid.NewGuid(),
            Username = adminUsername,
            Email = "admin@finviet.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            CreatedAt = DateTime.UtcNow
        });

        logger.LogInformation("Seeded admin account: username={Username} password=Admin@123", adminUsername);
    }

    private static async Task SeedCategoriesAsync(FinVietDbContext db, ILogger logger, CancellationToken ct)
    {
        // (id, name_vi, name_en, type, icon, color, default_bucket, sort_order)
        var seedCategories = new[]
        {
            new Category { CategoryId = "cat_food",              CategoryName = "Ăn uống",              NameEn = "Food",               Type = CategoryType.Expense, Icon = "restaurant",      Color = "#4EDEA3", ExpenseClass = "needs",   SortOrder = 1 },
            new Category { CategoryId = "cat_housing",           CategoryName = "Nhà ở & Tiện ích",     NameEn = "Housing & Utilities",Type = CategoryType.Expense, Icon = "home",            Color = "#D0BCFF", ExpenseClass = "needs",   SortOrder = 2 },
            new Category { CategoryId = "cat_transport",         CategoryName = "Di chuyển",            NameEn = "Transport",          Type = CategoryType.Expense, Icon = "directions_car",  Color = "#90CAF9", ExpenseClass = "needs",   SortOrder = 3 },
            new Category { CategoryId = "cat_health",            CategoryName = "Sức khỏe & Y tế",      NameEn = "Health",             Type = CategoryType.Expense, Icon = "local_hospital",  Color = "#EF9A9A", ExpenseClass = "needs",   SortOrder = 4 },
            new Category { CategoryId = "cat_education",         CategoryName = "Giáo dục",             NameEn = "Education",          Type = CategoryType.Expense, Icon = "school",          Color = "#FFE082", ExpenseClass = "needs",   SortOrder = 5 },
            new Category { CategoryId = "cat_family",            CategoryName = "Gửi tiền gia đình",    NameEn = "Family support",     Type = CategoryType.Expense, Icon = "family_restroom", Color = "#BCAAA4", ExpenseClass = "needs",   SortOrder = 6 },
            new Category { CategoryId = "cat_entertain",         CategoryName = "Giải trí",             NameEn = "Entertainment",      Type = CategoryType.Expense, Icon = "sports_esports",  Color = "#CE93D8", ExpenseClass = "wants",   SortOrder = 7 },
            new Category { CategoryId = "cat_beauty",            CategoryName = "Quần áo & Thời trang", NameEn = "Fashion",            Type = CategoryType.Expense, Icon = "checkroom",       Color = "#F8BBD0", ExpenseClass = "wants",   SortOrder = 8 },
            new Category { CategoryId = "cat_shopping",          CategoryName = "Mua sắm online",       NameEn = "Shopping",           Type = CategoryType.Expense, Icon = "shopping_bag",    Color = "#FFB690", ExpenseClass = "wants",   SortOrder = 9 },
            new Category { CategoryId = "cat_dining",            CategoryName = "Ăn ngoài & Cà phê",    NameEn = "Dining & Coffee",    Type = CategoryType.Expense, Icon = "local_cafe",      Color = "#FFCC80", ExpenseClass = "wants",   SortOrder = 10 },
            new Category { CategoryId = "cat_savings",           CategoryName = "Tiết kiệm",            NameEn = "Savings",            Type = CategoryType.Expense, Icon = "savings",         Color = "#4EDEA3", ExpenseClass = "savings", SortOrder = 11 },
            new Category { CategoryId = "cat_invest",            CategoryName = "Đầu tư",               NameEn = "Investment",         Type = CategoryType.Expense, Icon = "trending_up",     Color = "#A5D6A7", ExpenseClass = "savings", SortOrder = 12 },
            new Category { CategoryId = "cat_savings_goal",      CategoryName = "Mục tiêu tiết kiệm",   NameEn = "Saving goal",        Type = CategoryType.Expense, Icon = "flag",            Color = "#80CBC4", ExpenseClass = "savings", SortOrder = 13 },
            new Category { CategoryId = "cat_salary",            CategoryName = "Lương",                NameEn = "Salary",             Type = CategoryType.Income,  Icon = "payments",        Color = "#81C784", ExpenseClass = null,      SortOrder = 101 },
            new Category { CategoryId = "cat_freelance",         CategoryName = "Freelance",            NameEn = "Freelance",          Type = CategoryType.Income,  Icon = "work",            Color = "#64B5F6", ExpenseClass = null,      SortOrder = 102 },
            new Category { CategoryId = "cat_investment_return", CategoryName = "Lợi nhuận đầu tư",     NameEn = "Investment return",  Type = CategoryType.Income,  Icon = "trending_up",     Color = "#A5D6A7", ExpenseClass = null,      SortOrder = 103 },
            new Category { CategoryId = "cat_gift",              CategoryName = "Quà tặng",             NameEn = "Gift",               Type = CategoryType.Income,  Icon = "redeem",          Color = "#F48FB1", ExpenseClass = null,      SortOrder = 104 },
            new Category { CategoryId = "cat_income_other",      CategoryName = "Thu nhập khác",        NameEn = "Other income",       Type = CategoryType.Income,  Icon = "more_horiz",      Color = "#B0BEC5", ExpenseClass = null,      SortOrder = 105 },
        };

        var ids = seedCategories.Select(c => c.CategoryId).ToArray();
        var existingIds = await db.Categories
            .Where(c => ids.Contains(c.CategoryId))
            .Select(c => c.CategoryId)
            .ToListAsync(ct);

        var added = 0;
        foreach (var category in seedCategories)
        {
            if (existingIds.Contains(category.CategoryId)) continue;
            db.Categories.Add(category);
            added++;
        }

        if (added > 0)
            logger.LogInformation("Seeded {Count} categories", added);
    }

    private static async Task SeedCustomersAsync(FinVietDbContext db, ILogger logger, CancellationToken ct)
    {
        var seedCustomers = new[]
        {
            new
            {
                Email                 = "demo@finviet.local",
                FullName              = "Demo User",
                Password              = "Demo@1234",
                MonthlyIncomeExpected = 15_000_000m
            },
            new
            {
                Email                 = "alice@finviet.local",
                FullName              = "Alice Nguyen",
                Password              = "Alice@1234",
                MonthlyIncomeExpected = 20_000_000m
            },
            new
            {
                Email                 = "bob@finviet.local",
                FullName              = "Bob Tran",
                Password              = "Bob@12345",
                MonthlyIncomeExpected = 12_000_000m
            }
        };

        foreach (var s in seedCustomers)
        {
            var email = s.Email.ToLower();
            var exists = await db.Customers.AnyAsync(c => c.Email == email, ct);
            if (exists) continue;

            db.Customers.Add(new Customer
            {
                CustomerId = Guid.NewGuid(),
                FullName = s.FullName,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(s.Password),
                IsEmailVerified = true,
                EmailVerifiedAt = DateTime.UtcNow,
                IsActive = true,
                MonthlyIncomeExpected = s.MonthlyIncomeExpected,
                CreatedAt = DateTime.UtcNow
            });

            logger.LogInformation("Seeded customer: {Email} password={Password}", email, s.Password);
        }
    }
}