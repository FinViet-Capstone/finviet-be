using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Rules;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Merchant-keyword auto-categorization rules. Creating a rule retro-applies it to the
/// customer's matching transactions (substring, case-insensitive, transfers excluded) and,
/// per BUSINESS_LOGIC §8, the most specific (longest) matching keyword wins.
/// </summary>
public class MerchantRuleService : IMerchantRuleService
{
    private readonly FinVietDbContext _db;

    public MerchantRuleService(FinVietDbContext db) => _db = db;

    public async Task<IReadOnlyList<RuleResponse>> GetRulesAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        var rules = await _db.MerchantRules
            .AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.MerchantKeyword.Length)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var categoryIds = rules.Select(r => r.CategoryId).Distinct().ToList();
        var names = await _db.Categories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.CategoryId))
            .ToDictionaryAsync(c => c.CategoryId, c => c.CategoryName, cancellationToken);

        return rules.Select(r => ToResponse(r, names.GetValueOrDefault(r.CategoryId))).ToList();
    }

    public async Task<CreateRuleResponse> CreateRuleAsync(
        Guid customerId, CreateRuleRequest request, CancellationToken cancellationToken = default)
    {
        var keyword = request.MerchantKeyword?.Trim() ?? string.Empty;
        if (keyword.Length == 0)
            throw new BadRequestException("Merchant keyword is required.");
        if (string.IsNullOrWhiteSpace(request.CategoryId))
            throw new BadRequestException("Category id is required.");

        var category = await _db.Categories
            .FirstOrDefaultAsync(c => c.CategoryId == request.CategoryId, cancellationToken)
            ?? throw new NotFoundException("Category", request.CategoryId);

        // cat_savings_goal is auto-only (goal contributions); it must not be assignable by a rule.
        if (string.Equals(category.CategoryId, "cat_savings_goal", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("This category cannot be assigned by a rule.");

        var lowered = keyword.ToLowerInvariant();
        var duplicate = await _db.MerchantRules.AnyAsync(
            r => r.CustomerId == customerId && r.MerchantKeyword.ToLower() == lowered, cancellationToken);
        if (duplicate)
            throw new ConflictException("A rule for this merchant keyword already exists.");

        var rule = new MerchantRule
        {
            RuleId = Guid.NewGuid(),
            CustomerId = customerId,
            MerchantKeyword = keyword,
            CategoryId = request.CategoryId,
            AppliedCount = 0,
            CreatedAt = DateTime.UtcNow
        };
        _db.MerchantRules.Add(rule);

        rule.AppliedCount = await RetroApplyAsync(customerId, rule, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return new CreateRuleResponse
        {
            Rule = ToResponse(rule, category.CategoryName),
            AppliedCount = rule.AppliedCount
        };
    }

    public async Task<bool> DeleteRuleAsync(
        Guid customerId, Guid ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await _db.MerchantRules.FirstOrDefaultAsync(r => r.RuleId == ruleId, cancellationToken);
        if (rule is null)
            return false;
        if (rule.CustomerId != customerId)
            throw new ForbiddenException("You do not own this rule.");

        _db.MerchantRules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<RuleMatch?> ResolveAsync(
        Guid customerId, string? merchant, string? description, CancellationToken cancellationToken = default)
    {
        var haystack = $"{merchant} {description}";
        if (string.IsNullOrWhiteSpace(haystack))
            return null;

        var rules = await _db.MerchantRules
            .AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var winner = rules
            .Where(r => haystack.Contains(r.MerchantKeyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.MerchantKeyword.Length)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefault();

        if (winner is null)
            return null;

        var categoryName = await _db.Categories
            .AsNoTracking()
            .Where(c => c.CategoryId == winner.CategoryId)
            .Select(c => c.CategoryName)
            .FirstOrDefaultAsync(cancellationToken);

        return new RuleMatch(winner.RuleId, winner.CategoryId, categoryName, winner.MerchantKeyword);
    }

    public async Task IncrementAppliedAsync(Guid ruleId, int by = 1, CancellationToken cancellationToken = default)
    {
        var rule = await _db.MerchantRules.FirstOrDefaultAsync(r => r.RuleId == ruleId, cancellationToken);
        if (rule is null)
            return;

        rule.AppliedCount += by;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Retroactively assign <paramref name="newRule"/>'s category to the customer's matching
    /// transactions. The keyword is matched (substring, case-insensitive) against the
    /// merchant/beneficiary name AND the description — manual entries carry the payee text in
    /// the description (the create API has no merchant field), and SePay-synced rows carry the
    /// bank transfer content in the description too, while SMS imports set the merchant.
    /// Skips transfer legs (TransferPairId != null). When several of the customer's keywords
    /// match a transaction, the longest keyword wins (§8), so a freshly created shorter rule
    /// never overrides a more specific existing one.
    /// </summary>
    private async Task<int> RetroApplyAsync(Guid customerId, MerchantRule newRule, CancellationToken cancellationToken)
    {
        var pattern = "%" + newRule.MerchantKeyword + "%";

        var candidates = await _db.Transactions
            .Where(t => t.TransferPairId == null
                        && (t.CustomerId == customerId || (t.Wallet != null && t.Wallet.CustomerId == customerId))
                        && ((t.Merchant != null && EF.Functions.ILike(t.Merchant!, pattern))
                            || (t.Description != null && EF.Functions.ILike(t.Description!, pattern))))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return 0;

        var otherRules = await _db.MerchantRules
            .AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.RuleId != newRule.RuleId)
            .ToListAsync(cancellationToken);
        var allRules = otherRules.Append(newRule).ToList();

        var applied = 0;
        foreach (var txn in candidates)
        {
            var haystack = $"{txn.Merchant} {txn.Description}";
            var winner = allRules
                .Where(r => haystack.Contains(r.MerchantKeyword, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.MerchantKeyword.Length)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefault();

            if (winner is null || winner.RuleId != newRule.RuleId)
                continue;
            if (string.Equals(txn.CategoryId, newRule.CategoryId, StringComparison.Ordinal))
                continue;

            txn.CategoryId = newRule.CategoryId;
            txn.IsAiClassified = false;
            txn.AiConfidence = null;
            applied++;
        }

        return applied;
    }

    private static RuleResponse ToResponse(MerchantRule r, string? categoryName) => new()
    {
        RuleId = r.RuleId,
        MerchantKeyword = r.MerchantKeyword,
        CategoryId = r.CategoryId,
        CategoryName = categoryName,
        AppliedCount = r.AppliedCount,
        CreatedAt = r.CreatedAt
    };
}
