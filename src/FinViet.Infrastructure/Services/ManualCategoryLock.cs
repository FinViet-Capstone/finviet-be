using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// The single place a customer's category choice is written to a transaction. Marks the row
/// <c>manual</c> so the SePay worker and merchant rules leave it alone, and records the
/// correction when the choice replaced an AI result. A null category clears it (a dismissal). Every category-edit route goes through here.
/// </summary>
internal static class ManualCategoryLock
{
    private static bool IsAiSource(string? source) => source is "ai_suggestion" or "ai_auto";

    /// <summary>Stages the change on the context; the caller saves.</summary>
    /// <param name="alwaysLogCorrection">Log even when the previous source was not AI (the override route always has).</param>
    public static async Task ApplyAsync(
        FinVietDbContext db,
        Transaction transaction,
        Guid customerId,
        string? categoryId,
        bool alwaysLogCorrection,
        CancellationToken cancellationToken)
    {
        if (categoryId is not null
            && (alwaysLogCorrection || IsAiSource(transaction.AiClassificationSource)))
        {
            var originalGuessName = transaction.AiCategoryGuess is null
                ? null
                : await db.Categories.Where(c => c.CategoryId == transaction.AiCategoryGuess)
                    .Select(c => c.CategoryName).FirstOrDefaultAsync(cancellationToken);

            db.CategoryCorrectionLogs.Add(new CategoryCorrectionLog
            {
                LogId = Guid.NewGuid(),
                CustomerId = customerId,
                TransactionId = transaction.TransactionId,
                CorrectedCategoryId = categoryId,
                OriginalAiGuess = originalGuessName,
                CreatedAt = DateTime.UtcNow
            });
        }

        var now = DateTime.UtcNow;
        transaction.CategoryId = categoryId;
        transaction.IsAiClassified = false;
        transaction.AiConfidence = null;
        transaction.AiClassificationSource = "manual";
        transaction.AiClassifiedAt = now;
        transaction.UpdatedAt = now;
    }
}
