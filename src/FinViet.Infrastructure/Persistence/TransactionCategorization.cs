using System.Linq.Expressions;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Infrastructure.Persistence;

/// <summary>
/// Single source of truth for a transaction's derived categorization status. The ordered rule
/// table below is used both in-memory (<see cref="Derive"/>) and as a query predicate
/// (<see cref="Matches"/>), so DTO mapping and list filtering cannot disagree.
/// </summary>
public static class TransactionCategorization
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Suggested = "suggested";
    public const string Unsure = "unsure";
    public const string Failed = "failed";
    public const string Applied = "applied";
    public const string Reviewed = "reviewed";

    /// <summary>Statuses in rule order: the first matching rule wins.</summary>
    public static readonly IReadOnlyList<string> AllStatuses =
        new[] { Reviewed, Applied, None, Suggested, Unsure, Pending, Failed };

    /// <summary>How long a queued row (source NULL) counts as pending before it is treated as failed.</summary>
    public static readonly TimeSpan PendingWindow = TimeSpan.FromMinutes(10);

    public static DateTime PendingCutoff(DateTime utcNow) => utcNow - PendingWindow;

    public static bool IsValid(string status) => AllStatuses.Contains(status);

    private static Expression<Func<Transaction, DateTime, bool>> Condition(string status) => status switch
    {
        Reviewed => (t, _) => t.AiClassificationSource == "manual",
        Applied => (t, _) => t.CategoryId != null,
        None => (t, _) => t.EntryMethod != "sepay_sync" || t.TransactionType != "expense",
        Suggested => (t, _) => t.AiClassificationSource == "ai_suggestion" && t.AiCategoryGuess != null,
        Unsure => (t, _) => t.AiClassificationSource == "ai_suggestion" && t.AiCategoryGuess == null,
        Pending => (t, cutoff) => t.AiClassificationSource == null && t.AiClassifiedAt != null && t.AiClassifiedAt >= cutoff,
        Failed => (t, _) => true,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    // Compiled once; Derive is called per row.
    private static readonly (string Status, Func<Transaction, DateTime, bool> Test)[] Compiled =
        AllStatuses.Select(s => (s, Condition(s).Compile())).ToArray();

    private sealed record CutoffHolder(DateTime Value);

    /// <summary>Derives the status of an in-memory entity.</summary>
    public static string Derive(Transaction transaction, DateTime utcNow)
    {
        var cutoff = PendingCutoff(utcNow);
        foreach (var (status, test) in Compiled)
        {
            if (test(transaction, cutoff))
                return status;
        }
        return Failed;
    }

    /// <summary>Query predicate matching rows whose derived status is any of <paramref name="statuses"/>.</summary>
    public static Expression<Func<Transaction, bool>> Matches(IReadOnlyCollection<string> statuses, DateTime utcNow)
    {
        // A member access on a holder keeps the cutoff a query parameter rather than an inlined literal.
        var cutoff = Expression.Property(Expression.Constant(new CutoffHolder(PendingCutoff(utcNow))), nameof(CutoffHolder.Value));
        var parameter = Expression.Parameter(typeof(Transaction), "t");
        Expression? result = null;
        var earlier = new List<Expression>();

        foreach (var status in AllStatuses)
        {
            var lambda = Condition(status);
            var condition = new ParameterReplacer(
                new Dictionary<ParameterExpression, Expression>
                {
                    [lambda.Parameters[0]] = parameter,
                    [lambda.Parameters[1]] = cutoff
                }).Visit(lambda.Body);
            if (statuses.Contains(status))
            {
                // First match wins: the status applies only if no earlier rule matched.
                var term = condition;
                foreach (var e in earlier)
                    term = Expression.AndAlso(Expression.Not(e), term);
                result = result is null ? term : Expression.OrElse(result, term);
            }
            earlier.Add(condition);
        }

        return Expression.Lambda<Func<Transaction, bool>>(result ?? Expression.Constant(false), parameter);
    }

    private sealed class ParameterReplacer(IReadOnlyDictionary<ParameterExpression, Expression> map) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            map.TryGetValue(node, out var replacement) ? replacement : node;
    }
}
