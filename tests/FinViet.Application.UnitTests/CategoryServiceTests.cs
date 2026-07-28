using FinViet.Infrastructure.Services;

namespace FinViet.Application.UnitTests;

// TC-CUSTOMCAT-01..04 — pure visibility-scoping logic behind custom category creation
// (be-revamp.md item 5). A custom_* category must never leak to a customer who didn't create it.
public class CategoryServiceTests
{
    [Fact]
    public void IsVisibleTo_SeededCategory_AlwaysVisible()
    {
        var overrides = new Dictionary<string, string>();
        Assert.True(CategoryService.IsVisibleTo("cat_food", overrides));
    }

    [Fact]
    public void IsVisibleTo_CustomCategory_NoOverride_NotVisible()
    {
        // The exact bug this check exists to prevent: another customer's custom category, with
        // no active customer_categories row for the caller, must not leak into their list.
        var overrides = new Dictionary<string, string>();
        Assert.False(CategoryService.IsVisibleTo("custom_11111111-1111-1111-1111-111111111111", overrides));
    }

    [Fact]
    public void IsVisibleTo_CustomCategory_WithOverride_Visible()
    {
        var categoryId = "custom_11111111-1111-1111-1111-111111111111";
        var overrides = new Dictionary<string, string> { [categoryId] = "wants" };

        Assert.True(CategoryService.IsVisibleTo(categoryId, overrides));
    }

    [Fact]
    public void IsVisibleTo_CustomCategory_OverrideForDifferentCategory_NotVisible()
    {
        var overrides = new Dictionary<string, string>
        {
            ["custom_22222222-2222-2222-2222-222222222222"] = "needs"
        };

        Assert.False(CategoryService.IsVisibleTo("custom_11111111-1111-1111-1111-111111111111", overrides));
    }
}
