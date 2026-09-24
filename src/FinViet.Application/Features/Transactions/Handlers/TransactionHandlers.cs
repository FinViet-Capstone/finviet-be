using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.DTOs.Rules;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinViet.Application.Features.Transactions.Handlers;

internal static class TransactionRules
{
    public static readonly string[] ValidTypes = { "income", "expense", "transfer_out", "transfer_in" };

    public const string GoalCategoryId = "cat_savings_goal";

    public static string Normalize(string transactionType)
        => (transactionType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "income" or "in" => "income",
            "expense" or "out" => "expense",
            "transfer_out" or "transfer" => "transfer_out",
            "transfer_in" => "transfer_in",
            var other => other
        };

    public static string NormalizeEntryMethod(string? entryMethod)
        => (entryMethod ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "csv" or "csv_import" => "csv_import",
            "sms" or "sms_paste" => "sms_paste",
            "sepay_sync" => "sepay_sync",
            "finverse_sync" => "finverse_sync",
            "photo" => "photo",
            _ => "manual"
        };

    // Keep persisted values aligned with ck_transactions_ai_source. Older mobile clients
    // label reviewed batch suggestions AI_BATCH and client merchant rules RULE.
    public static string? NormalizeAiSource(string? source)
        => (source ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => null,
            "ai_batch" => "ai_suggestion",
            "rule" => "merchant_rule",
            "manual" => "manual",
            "merchant_rule" => "merchant_rule",
            "ai_auto" => "ai_auto",
            "ai_suggestion" => "ai_suggestion",
            "fallback" => "fallback",
            _ => throw new BadRequestException("Invalid aiSource. Allowed values: manual, merchant_rule, ai_auto, ai_suggestion, fallback (legacy AI_BATCH and RULE are also supported).")
        };

    public static string ValidateManualInput(string transactionType, decimal amount)
    {
        var normalized = Normalize(transactionType);
        if (!ValidTypes.Contains(normalized))
            throw new BadRequestException(
                $"Invalid transaction type '{transactionType}'. Allowed values: income, expense.");

        if (normalized is "transfer_out" or "transfer_in")
            throw new BusinessRuleException("Transfer legs are created only by the wallet transfer flow.", "transfer_managed");

        if (amount <= 0)
            throw new BadRequestException("Amount must be greater than zero.");

        return normalized;
    }

    public static async Task ValidateCategoryAsync(
        ICategoryService categoryService,
        string? categoryId,
        string normalizedType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return;

        if (string.Equals(categoryId, GoalCategoryId, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException(
                "cat_savings_goal is reserved for savings-goal contributions and cannot be set manually.",
                "goal_transaction_locked");

        var category = await categoryService.GetCategoryByIdAsync(categoryId, cancellationToken: cancellationToken);
        if (category is null)
            throw new NotFoundException("Category", categoryId);

        if ((normalizedType == "income" || normalizedType == "expense")
            && !string.Equals(category.Type, normalizedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                $"Category '{categoryId}' is of type '{category.Type}', which does not match transaction type '{normalizedType}'.",
                "category_type_mismatch");
        }
    }

    public static void EnsureNotTransfer(string transactionType)
    {
        var type = Normalize(transactionType);
        if (type is "transfer_out" or "transfer_in")
            throw new BusinessRuleException("Transfer legs cannot be reclassified.", "transfer_managed");
    }

    // amount/merchant/transactionDate are provider-authoritative on a sepay_linked wallet
    // (bank-sync owns them); only categoryId may still change there.
    public static void EnsureEditableFieldsAllowed(string? walletType, bool amountProvided, bool merchantProvided, bool dateProvided)
    {
        if (!amountProvided && !merchantProvided && !dateProvided)
            return;

        if (string.Equals(walletType, "sepay_linked", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException(
                "Only the category can be changed for transactions from a bank-synced wallet.",
                "synced_transaction_fields_locked");
    }
}

public class CreateTransactionHandler : IRequestHandler<CreateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICategoryService _categoryService;
    private readonly IMerchantRuleService _ruleService;
    private readonly IBudgetService _budgetService;
    private readonly IAiTelemetryRecorder _aiTelemetry;
    private readonly ILogger<CreateTransactionHandler> _logger;

    public CreateTransactionHandler(
        ITransactionRepository transactionRepository,
        ICategoryService categoryService,
        IMerchantRuleService ruleService,
        IBudgetService budgetService,
        IAiTelemetryRecorder aiTelemetry,
        ILogger<CreateTransactionHandler> logger)
    {
        _transactionRepository = transactionRepository;
        _categoryService = categoryService;
        _ruleService = ruleService;
        _budgetService = budgetService;
        _aiTelemetry = aiTelemetry;
        _logger = logger;
    }

    public async Task<TransactionResponseDto> Handle(CreateTransactionCommand request, CancellationToken cancellationToken)
    {
        var normalizedType = TransactionRules.ValidateManualInput(request.TransactionType, request.Amount);
        var normalizedAiSource = TransactionRules.NormalizeAiSource(request.AiSource);

        // Auto-apply a merchant rule when the caller did not choose a category (manual entry keeps
        // the user's choice; uncategorized expenses get the matching rule's category — §2b). A rule
        // pointing at an incompatible category type is ignored so it never blocks the create.
        var categoryId = request.CategoryId;
        if (categoryId == "cat_income")
            categoryId = "cat_income_other";

        RuleMatch? match = null;
        if (string.IsNullOrWhiteSpace(categoryId) && normalizedType == "expense")
        {
            match = await _ruleService.ResolveAsync(request.CustomerId, merchant: null, description: request.Note, cancellationToken);
            if (match is not null)
            {
                try
                {
                    await TransactionRules.ValidateCategoryAsync(_categoryService, match.CategoryId, normalizedType, cancellationToken);
                    categoryId = match.CategoryId;
                }
                catch (BusinessRuleException)
                {
                    match = null; // incompatible rule category → leave uncategorized
                }
            }
        }

        await TransactionRules.ValidateCategoryAsync(_categoryService, categoryId, normalizedType, cancellationToken);

        // A merchant rule matched above always wins over a client-supplied AI suggestion, since
        // the rule replaced categoryId before this point — don't log a stale AI decision for it.
        var effectiveAiSource = match is null ? normalizedAiSource : null;
        var effectiveAiConfidence = match is null ? request.AiConfidence : null;

        var result = await _transactionRepository.CreateManualForCustomerAsync(
            request.CustomerId,
            request.WalletId,
            categoryId,
            normalizedType,
            request.Amount,
            request.TransactionDate,
            request.Note,
            request.IdempotencyKey,
            TransactionRules.NormalizeEntryMethod(request.EntryMethod),
            request.Merchant,
            effectiveAiSource,
            effectiveAiConfidence,
            cancellationToken);

        if (match is not null)
            await _ruleService.IncrementAppliedAsync(match.RuleId, 1, cancellationToken);

        // First durable categorization-decision record for any import path (CSV/SMS/photo) — the
        // interactive suggest-category flow already writes this via AiCategorizationService, but
        // batch/preview classification during extraction runs before a transaction exists to
        // correlate against, so this is the first opportunity to log it.
        if (!string.IsNullOrWhiteSpace(effectiveAiSource))
        {
            await _aiTelemetry.RecordAuditAsync(
                new AiAuditRecord(
                    "categorization_decision",
                    "system",
                    request.CustomerId,
                    CorrelationId: result.TransactionId,
                    Metadata: new Dictionary<string, object?>
                    {
                        ["source"] = effectiveAiSource,
                        ["confidence"] = effectiveAiConfidence,
                        ["applied"] = true,
                        ["reason"] = null
                    }),
                cancellationToken);
        }
        else if (match is not null)
        {
            await _aiTelemetry.RecordAuditAsync(
                new AiAuditRecord(
                    "categorization_decision",
                    "system",
                    request.CustomerId,
                    CorrelationId: result.TransactionId,
                    Metadata: new Dictionary<string, object?>
                    {
                        ["source"] = "merchant_rule",
                        ["confidence"] = null,
                        ["applied"] = true,
                        ["reason"] = null
                    }),
                cancellationToken);
        }

        // Re-evaluate budgets for the affected month so a crossed threshold raises an alert
        // notification. Only expenses can push a category over budget. Swallows its own errors.
        if (normalizedType == "expense")
            await _budgetService.SyncBudgetOnTransactionChangeAsync(
                request.CustomerId,
                DateOnly.FromDateTime(request.TransactionDate),
                cancellationToken);

        _logger.LogInformation(
            "Created transaction {TransactionId} for customer {CustomerId}, wallet {WalletId}, amount {Amount}, entryMethod {EntryMethod}, category {CategoryId}.",
            result.TransactionId,
            request.CustomerId,
            request.WalletId,
            request.Amount,
            request.EntryMethod,
            categoryId);

        return result;
    }
}

public class UpdateTransactionHandler : IRequestHandler<UpdateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly ICategoryService _categoryService;
    private readonly IBudgetService _budgetService;
    private readonly ILogger<UpdateTransactionHandler> _logger;

    public UpdateTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        ICategoryService categoryService,
        IBudgetService budgetService,
        ILogger<UpdateTransactionHandler> logger)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _categoryService = categoryService;
        _budgetService = budgetService;
        _logger = logger;
    }

    public async Task<TransactionResponseDto> Handle(UpdateTransactionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdForCustomerAsync(
            request.CustomerId,
            request.TransactionId,
            cancellationToken);
        if (transaction is null)
            throw new NotFoundException("Transaction", request.TransactionId);

        var type = TransactionRules.Normalize(transaction.TransactionType);
        TransactionRules.EnsureNotTransfer(type);

        var amountProvided = request.Amount.HasValue;
        var merchantProvided = request.Merchant is not null;
        var dateProvided = request.TransactionDate.HasValue;

        // Wallet type is immutable after creation (WalletRules.ValidateUpdate rejects changing
        // it), so this pre-lock read is race-free — no need to re-check under the repo's lock.
        if (amountProvided || merchantProvided || dateProvided)
        {
            var wallet = await _walletRepository.GetByIdAsync(transaction.WalletId, cancellationToken);
            TransactionRules.EnsureEditableFieldsAllowed(wallet?.WalletType, amountProvided, merchantProvided, dateProvided);
        }

        if (amountProvided && request.Amount!.Value <= 0)
            throw new BadRequestException("Amount must be greater than zero.");

        var categoryId = request.CategoryId;
        if (categoryId == "cat_income")
            categoryId = "cat_income_other";
        if (categoryId is not null)
            await TransactionRules.ValidateCategoryAsync(_categoryService, categoryId, type, cancellationToken);

        var result = await _transactionRepository.EditForCustomerAsync(
            request.CustomerId,
            request.TransactionId,
            categoryId,
            request.Amount,
            request.Merchant,
            request.TransactionDate,
            cancellationToken);
        if (result is null)
            throw new NotFoundException("Transaction", request.TransactionId);

        // Recategorizing/reamounting an expense may push a category over its budget → re-evaluate alerts.
        if (type == "expense")
            await _budgetService.SyncBudgetOnTransactionChangeAsync(
                request.CustomerId,
                DateOnly.FromDateTime(result.TransactionDate),
                cancellationToken);

        _logger.LogInformation(
            "Updated transaction {TransactionId} for customer {CustomerId} (amountChanged={AmountChanged}, merchantChanged={MerchantChanged}, categoryChanged={CategoryChanged}).",
            request.TransactionId,
            request.CustomerId,
            amountProvided,
            merchantProvided,
            categoryId is not null);

        return result;
    }
}

public class DeleteTransactionHandler : IRequestHandler<DeleteTransactionCommand, bool>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ILogger<DeleteTransactionHandler> _logger;

    public DeleteTransactionHandler(
        ITransactionRepository transactionRepository,
        ILogger<DeleteTransactionHandler> logger)
    {
        _transactionRepository = transactionRepository;
        _logger = logger;
    }

    public async Task<bool> Handle(DeleteTransactionCommand request, CancellationToken cancellationToken)
    {
        var deleted = await _transactionRepository.DeleteForCustomerAsync(
            request.CustomerId,
            request.TransactionId,
            cancellationToken);
        if (!deleted)
            throw new NotFoundException("Transaction", request.TransactionId);

        _logger.LogInformation(
            "Deleted transaction {TransactionId} for customer {CustomerId}.",
            request.TransactionId,
            request.CustomerId);

        return true;
    }
}

public class ClassifyTransactionHandler : IRequestHandler<ClassifyTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICategoryService _categoryService;
    private readonly IBudgetService _budgetService;

    public ClassifyTransactionHandler(
        ITransactionRepository transactionRepository,
        ICategoryService categoryService,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _categoryService = categoryService;
        _budgetService = budgetService;
    }

    public async Task<TransactionResponseDto> Handle(ClassifyTransactionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdForCustomerAsync(
            request.CustomerId,
            request.TransactionId,
            cancellationToken);
        if (transaction is null)
            throw new NotFoundException("Transaction", request.TransactionId);

        var type = TransactionRules.Normalize(transaction.TransactionType);
        TransactionRules.EnsureNotTransfer(type);
        await TransactionRules.ValidateCategoryAsync(_categoryService, request.CategoryId, type, cancellationToken);

        var result = (await _transactionRepository.ClassifyAsync(
            request.CustomerId,
            request.TransactionId,
            request.CategoryId,
            cancellationToken))!;

        if (type == "expense")
            await _budgetService.SyncBudgetOnTransactionChangeAsync(
                request.CustomerId,
                DateOnly.FromDateTime(result.TransactionDate),
                cancellationToken);

        return result;
    }
}
