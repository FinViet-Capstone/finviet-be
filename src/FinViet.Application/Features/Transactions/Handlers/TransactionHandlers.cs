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
}

public class CreateTransactionHandler : IRequestHandler<CreateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;

    public CreateTransactionHandler(ITransactionRepository transactionRepository, IWalletRepository walletRepository)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
    }

    public async Task<TransactionResponseDto> Handle(CreateTransactionCommand request, CancellationToken cancellationToken)
    {
        TransactionRules.ValidateInput(request.TransactionType, request.Amount);

        var wallet = await _walletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", request.WalletId);

        var transaction = await _transactionRepository.CreateAsync(
            request.WalletId,
            wallet.CustomerId,
            request.CategoryId,
            request.TransactionType,
            request.Amount,
            request.TransactionDate,
            request.Description,
            request.Merchant,
            request.EntryMethod,
            cancellationToken
        );

        decimal newBalance = wallet.Balance;
        if (TransactionRules.IsCredit(request.TransactionType))
            newBalance += request.Amount;
        else
            newBalance -= request.Amount;

        await _walletRepository.UpdateBalanceAsync(request.WalletId, newBalance, cancellationToken);

        return transaction;
    }
}

public class UpdateTransactionHandler : IRequestHandler<UpdateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;

    public UpdateTransactionHandler(ITransactionRepository transactionRepository, IWalletRepository walletRepository)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
    }

    public async Task<TransactionResponseDto> Handle(UpdateTransactionCommand request, CancellationToken cancellationToken)
    {
        TransactionRules.ValidateInput(request.TransactionType, request.Amount);

        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction == null)
            throw new NotFoundException("Transaction", request.TransactionId);

        var wallet = await _walletRepository.GetByIdAsync(transaction.WalletId, cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", transaction.WalletId);

        decimal newBalance = wallet.Balance;

        // Reverse the old transaction's effect.
        if (TransactionRules.IsCredit(transaction.TransactionType))
            newBalance -= transaction.Amount;
        else
            newBalance += transaction.Amount;

        // Apply the new transaction's effect.
        if (TransactionRules.IsCredit(request.TransactionType))
            newBalance += request.Amount;
        else
            newBalance -= request.Amount;

        var updatedTransaction = await _transactionRepository.UpdateAsync(
            request.TransactionId,
            request.CategoryId,
            request.TransactionType,
            request.Amount,
            request.TransactionDate,
            request.Description,
            request.Merchant,
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

        decimal newBalance = wallet.Balance;
        if (TransactionRules.IsCredit(transaction.TransactionType))
            newBalance -= transaction.Amount;
        else
            newBalance += transaction.Amount;

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

    public ClassifyTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        ICategoryService categoryService)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _categoryService = categoryService;
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

        var classified = await _transactionRepository.ClassifyAsync(
            request.TransactionId,
            request.CategoryId,
            cancellationToken);

        return classified!;
    }
}
