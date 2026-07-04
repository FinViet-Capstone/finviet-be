using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.DTOs.Rules;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.Interfaces;
using MediatR;

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

        var category = await categoryService.GetCategoryByIdAsync(categoryId, cancellationToken);
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
}

public class CreateTransactionHandler : IRequestHandler<CreateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICategoryService _categoryService;
    private readonly IMerchantRuleService _ruleService;
    private readonly IBudgetService _budgetService;

    public CreateTransactionHandler(
        ITransactionRepository transactionRepository,
        ICategoryService categoryService,
        IMerchantRuleService ruleService,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _categoryService = categoryService;
        _ruleService = ruleService;
        _budgetService = budgetService;
    }

    public async Task<TransactionResponseDto> Handle(CreateTransactionCommand request, CancellationToken cancellationToken)
    {
        var normalizedType = TransactionRules.ValidateManualInput(request.TransactionType, request.Amount);

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
            cancellationToken);

        if (match is not null)
            await _ruleService.IncrementAppliedAsync(match.RuleId, 1, cancellationToken);

        // Re-evaluate budgets for the affected month so a crossed threshold raises an alert
        // notification. Only expenses can push a category over budget. Swallows its own errors.
        if (normalizedType == "expense")
            await _budgetService.SyncBudgetOnTransactionChangeAsync(
                request.CustomerId,
                DateOnly.FromDateTime(request.TransactionDate),
                cancellationToken);

        return result;
    }
}

public class UpdateTransactionHandler : IRequestHandler<UpdateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICategoryService _categoryService;
    private readonly IBudgetService _budgetService;

    public UpdateTransactionHandler(
        ITransactionRepository transactionRepository,
        ICategoryService categoryService,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _categoryService = categoryService;
        _budgetService = budgetService;
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
        await TransactionRules.ValidateCategoryAsync(_categoryService, request.CategoryId, type, cancellationToken);

        var result = (await _transactionRepository.ClassifyAsync(
            request.TransactionId,
            request.CategoryId,
            cancellationToken))!;

        // Recategorizing an expense may push a category over its budget → re-evaluate alerts.
        if (type == "expense")
            await _budgetService.SyncBudgetOnTransactionChangeAsync(
                request.CustomerId,
                DateOnly.FromDateTime(result.TransactionDate),
                cancellationToken);

        return result;
    }
}

public class DeleteTransactionHandler : IRequestHandler<DeleteTransactionCommand, bool>
{
    private readonly ITransactionRepository _transactionRepository;

    public DeleteTransactionHandler(ITransactionRepository transactionRepository)
        => _transactionRepository = transactionRepository;

    public async Task<bool> Handle(DeleteTransactionCommand request, CancellationToken cancellationToken)
    {
        var deleted = await _transactionRepository.DeleteForCustomerAsync(
            request.CustomerId,
            request.TransactionId,
            cancellationToken);
        if (!deleted)
            throw new NotFoundException("Transaction", request.TransactionId);

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
