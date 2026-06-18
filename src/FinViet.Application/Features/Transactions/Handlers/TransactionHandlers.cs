using MediatR;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Application.Common.Exceptions;

namespace FinViet.Application.Features.Transactions.Handlers;

internal static class TransactionRules
{
    public static readonly string[] ValidTypes = { "expense", "income", "transfer_out", "transfer_in" };

    public static void ValidateInput(string transactionType, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(transactionType) || !ValidTypes.Contains(transactionType))
            throw new BadRequestException(
                $"Invalid transaction type '{transactionType}'. Allowed values: {string.Join(", ", ValidTypes)}.");

        if (amount <= 0)
            throw new BadRequestException("Amount must be greater than zero.");
    }

    /// <summary>income and transfer_in add to the wallet; expense and transfer_out subtract.</summary>
    public static bool IsCredit(string transactionType)
        => transactionType is "income" or "transfer_in";

    public static async Task EnsureCategoryExistsAsync(
        ICategoryService categoryService,
        string? categoryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return;

        var category = await categoryService.GetCategoryByIdAsync(categoryId, cancellationToken);
        if (category is null)
            throw new NotFoundException("Category", categoryId);
    }
}

public class CreateTransactionHandler : IRequestHandler<CreateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICategoryService _categoryService;
    private readonly IBudgetService _budgetService;

    public CreateTransactionHandler(
        ITransactionRepository transactionRepository,
        ICategoryService categoryService,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _categoryService = categoryService;
        _budgetService = budgetService;
    }

    public async Task<TransactionResponseDto> Handle(CreateTransactionCommand request, CancellationToken cancellationToken)
    {
        TransactionRules.ValidateInput(request.TransactionType, request.Amount);
        await TransactionRules.EnsureCategoryExistsAsync(_categoryService, request.CategoryId, cancellationToken);

        var transaction = await _transactionRepository.CreateAsync(
            request.CustomerId,
            request.WalletId,
            request.CategoryId,
            request.TransactionType,
            request.Amount,
            request.TransactionDate,
            request.Description,
            request.Merchant,
            request.EntryMethod,
            cancellationToken
        );

        await _budgetService.SyncBudgetOnTransactionChangeAsync(
            request.CustomerId,
            DateOnly.FromDateTime(request.TransactionDate),
            cancellationToken);

        return transaction;
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
        TransactionRules.ValidateInput(request.TransactionType, request.Amount);
        await TransactionRules.EnsureCategoryExistsAsync(_categoryService, request.CategoryId, cancellationToken);

        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction == null)
            throw new NotFoundException("Transaction", request.TransactionId);

        var updatedTransaction = await _transactionRepository.UpdateAsync(
            request.CustomerId,
            request.TransactionId,
            request.CategoryId,
            request.TransactionType,
            request.Amount,
            request.TransactionDate,
            request.Description,
            request.Merchant,
            cancellationToken
        );

        await _budgetService.SyncBudgetOnTransactionChangeAsync(
            request.CustomerId,
            DateOnly.FromDateTime(transaction.TransactionDate),
            cancellationToken);

        if (DateOnly.FromDateTime(transaction.TransactionDate) != DateOnly.FromDateTime(request.TransactionDate))
        {
            await _budgetService.SyncBudgetOnTransactionChangeAsync(
                request.CustomerId,
                DateOnly.FromDateTime(request.TransactionDate),
                cancellationToken);
        }

        return updatedTransaction;
    }
}

public class DeleteTransactionHandler : IRequestHandler<DeleteTransactionCommand, bool>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IBudgetService _budgetService;

    public DeleteTransactionHandler(
        ITransactionRepository transactionRepository,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _budgetService = budgetService;
    }

    public async Task<bool> Handle(DeleteTransactionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction == null)
            throw new NotFoundException("Transaction", request.TransactionId);

        var deleted = await _transactionRepository.DeleteAsync(
            request.CustomerId,
            request.TransactionId,
            cancellationToken);

        if (!deleted)
            return false;

        await _budgetService.SyncBudgetOnTransactionChangeAsync(
            request.CustomerId,
            DateOnly.FromDateTime(transaction.TransactionDate),
            cancellationToken);

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
        await TransactionRules.EnsureCategoryExistsAsync(_categoryService, request.CategoryId, cancellationToken);

        var classified = await _transactionRepository.ClassifyAsync(
            request.CustomerId,
            request.TransactionId,
            request.CategoryId,
            cancellationToken);

        if (classified is null)
            throw new NotFoundException("Transaction", request.TransactionId);

        await _budgetService.SyncBudgetOnTransactionChangeAsync(
            request.CustomerId,
            DateOnly.FromDateTime(classified.TransactionDate),
            cancellationToken);

        return classified;
    }
}
