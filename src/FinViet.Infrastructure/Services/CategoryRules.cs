using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinViet.Application.Exceptions;

namespace FinViet.Infrastructure.Services;

internal static class CategoryRules
{
    internal const string SavingsGoalCategoryId = "cat_savings_goal";

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "income", "expense"
    };

    private static readonly HashSet<string> AllowedExpenseClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "needs", "wants", "savings"
    };

    internal static string NormalizeType(string type)
    {
        var normalized = type.Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(normalized))
            throw new ValidationException("Category type must be one of: income, expense.");

        return normalized;
    }

    internal static string? NormalizeExpenseClass(string? expenseClass, string type)
    {
        if (type == "income")
            return null;

        if (string.IsNullOrWhiteSpace(expenseClass))
            throw new ValidationException("Expense class is required for expense categories.");

        var normalized = expenseClass.Trim().ToLowerInvariant();
        if (!AllowedExpenseClasses.Contains(normalized))
            throw new ValidationException("Expense class must be one of: needs, wants, savings.");

        return normalized;
    }

    internal static string NormalizeCustomerBucket(string? bucketId)
    {
        var normalized = bucketId?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !AllowedExpenseClasses.Contains(normalized))
            throw new ValidationException("Bucket must be one of: needs, wants, savings.");

        return normalized;
    }

    internal static void EnsureCustomerBucketCanBeSet(string categoryId, string categoryType)
    {
        if (!string.Equals(categoryType, "expense", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Only expense categories can be assigned to a bucket.");

        if (string.Equals(categoryId, SavingsGoalCategoryId, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Saving goal contributions cannot be reassigned to a different bucket.");
    }

    internal static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    internal static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        var ascii = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        ascii = Regex.Replace(ascii, "[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(ascii) ? Guid.NewGuid().ToString("N")[..8] : ascii;
    }
}
