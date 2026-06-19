using MediatR;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Application.Common.Exceptions;

namespace FinViet.Application.Features.Transactions.Handlers;

internal static class TransactionRules
{
    public static readonly string[] ValidTypes = { "income", "expense", "transfer_out", "transfer_in" };

    /// <summary>
    /// Accepts both the v2.1 lowercase vocabulary and the legacy uppercase aliases,
    /// returning the canonical lowercase value used by the persistence layer.
    /// </summary>
    public static string Normalize(string transactionType)
        => (transactionType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "income" or "in" => "income",
            "expense" or "out" => "expense",
            "transfer_out" or "transfer" => "transfer_out",
            "transfer_in" => "transfer_in",
            var other => other
        };

    public static string ValidateInput(string transactionType, decimal amount)
    {
        var normalized = Normalize(transactionType);
        if (!ValidTypes.Contains(normalized))
            throw new BadRequestException(
                $"Invalid transaction type '{transactionType}'. Allowed values: {string.Join(", ", ValidTypes)}.");

        if (amount <= 0)
            throw new BadRequestException("Amount must be greater than zero.");

        return normalized;
    }

    /// <summary>Income increases the wallet balance; everything else decreases it.</summary>
    public static decimal SignedDelta(string normalizedType, decimal amount)
        => normalizedType == "income" ? amount : -amount;

    /// <summary>Category that may only be attached by automated goal contributions.</summary>
    public const string GoalCategoryId = "cat_savings_goal";

    /// <summary>
    /// Enforces the v2.1 category rules for a manually created/edited transaction:
    /// the goal category is auto-only, and category.type must match the transaction type
    /// for income/expense rows.
    /// </summary>
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

        // Only income/expense rows carry a category that must match; transfers have none.
        if ((normalizedType == "income" || normalizedType == "expense")
            && !string.Equals(category.Type, normalizedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                $"Category '{categoryId}' is of type '{category.Type}', which does not match transaction type '{normalizedType}'.",
                "category_type_mismatch");
        }
    }
}

public class CreateTransactionHandler : IRequestHandler<CreateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly ICategoryService _categoryService;

    public CreateTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        ICategoryService categoryService)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _categoryService = categoryService;
    }

    public async Task<TransactionResponseDto> Handle(CreateTransactionCommand request, CancellationToken cancellationToken)
    {
        var normalizedType = TransactionRules.ValidateInput(request.TransactionType, request.Amount);

        var wallet = await _walletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", request.WalletId);

        await TransactionRules.ValidateCategoryAsync(_categoryService, request.CategoryId, normalizedType, cancellationToken);

        var transaction = await _transactionRepository.CreateAsync(
            request.WalletId,
            request.CategoryId,
            request.SourceId,
            normalizedType,
            request.Amount,
            request.TransactionDate,
            request.Note,
            cancellationToken
        );

        var newBalance = wallet.Balance + TransactionRules.SignedDelta(normalizedType, request.Amount);

        await _walletRepository.UpdateBalanceAsync(request.WalletId, newBalance, cancellationToken);

        return transaction;
    }
}

public class UpdateTransactionHandler : IRequestHandler<UpdateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly ICategoryService _categoryService;

    public UpdateTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        ICategoryService categoryService)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _categoryService = categoryService;
    }

    public async Task<TransactionResponseDto> Handle(UpdateTransactionCommand request, CancellationToken cancellationToken)
    {
        var normalizedType = TransactionRules.ValidateInput(request.TransactionType, request.Amount);

        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction == null)
            throw new NotFoundException("Transaction", request.TransactionId);

        await TransactionRules.ValidateCategoryAsync(_categoryService, request.CategoryId, normalizedType, cancellationToken);

        var wallet = await _walletRepository.GetByIdAsync(transaction.WalletId, cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", transaction.WalletId);

        // Reverse the existing entry, then apply the new one.
        var newBalance = wallet.Balance
            - TransactionRules.SignedDelta(TransactionRules.Normalize(transaction.TransactionType), transaction.Amount)
            + TransactionRules.SignedDelta(normalizedType, request.Amount);

        var updatedTransaction = await _transactionRepository.UpdateAsync(
            request.TransactionId,
            request.CategoryId,
            request.SourceId,
            normalizedType,
            request.Amount,
            request.TransactionDate,
            request.Note,
            cancellationToken
        );

        await _walletRepository.UpdateBalanceAsync(transaction.WalletId, newBalance, cancellationToken);

        return updatedTransaction;
    }
}

public class DeleteTransactionHandler : IRequestHandler<DeleteTransactionCommand, bool>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;

    public DeleteTransactionHandler(ITransactionRepository transactionRepository, IWalletRepository walletRepository)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
    }

    public async Task<bool> Handle(DeleteTransactionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction == null)
            throw new NotFoundException("Transaction", request.TransactionId);

        var wallet = await _walletRepository.GetByIdAsync(transaction.WalletId, cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", transaction.WalletId);

        // Reverse the transaction's effect on the wallet balance.
        var newBalance = wallet.Balance
            - TransactionRules.SignedDelta(TransactionRules.Normalize(transaction.TransactionType), transaction.Amount);

        await _transactionRepository.DeleteAsync(request.TransactionId, cancellationToken);
        await _walletRepository.UpdateBalanceAsync(transaction.WalletId, newBalance, cancellationToken);

        return true;
    }
}

public class ClassifyTransactionHandler : IRequestHandler<ClassifyTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly ICategoryService _categoryService;
    private readonly IIncomeSourceService _incomeSourceService;

    public ClassifyTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        ICategoryService categoryService,
        IIncomeSourceService incomeSourceService)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _categoryService = categoryService;
        _incomeSourceService = incomeSourceService;
    }

    public async Task<TransactionResponseDto> Handle(ClassifyTransactionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction == null)
            throw new NotFoundException("Transaction", request.TransactionId);

        // Ownership: the transaction's wallet must belong to the authenticated customer.
        var wallet = await _walletRepository.GetByIdAsync(transaction.WalletId, cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", transaction.WalletId);

        if (wallet.CustomerId != request.CustomerId)
            throw new ForbiddenException("You do not have access to this transaction.");

        if (!string.IsNullOrWhiteSpace(request.CategoryId))
        {
            var category = await _categoryService.GetCategoryByIdAsync(request.CategoryId, cancellationToken);
            if (category == null)
                throw new NotFoundException("Category", request.CategoryId);
        }

        if (request.SourceId.HasValue)
        {
            // GetIncomeSourceByIdAsync is scoped by customerId, so this also enforces ownership of the source.
            var source = await _incomeSourceService.GetIncomeSourceByIdAsync(request.CustomerId, request.SourceId.Value, cancellationToken);
            if (source == null)
                throw new NotFoundException("Income source", request.SourceId.Value);
        }

        var classified = await _transactionRepository.ClassifyAsync(
            request.TransactionId,
            request.CategoryId,
            request.SourceId,
            cancellationToken);

        return classified!;
    }
}
