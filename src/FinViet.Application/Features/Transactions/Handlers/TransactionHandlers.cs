using MediatR;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Application.Common.Exceptions;

namespace FinViet.Application.Features.Transactions.Handlers;

internal static class TransactionRules
{
    public static readonly string[] ValidTypes = { "INCOME", "EXPENSE", "TRANSFER", "DEBT_PAYMENT" };

    public static void ValidateInput(string transactionType, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(transactionType) || !ValidTypes.Contains(transactionType))
            throw new BadRequestException(
                $"Invalid transaction type '{transactionType}'. Allowed values: {string.Join(", ", ValidTypes)}.");

        if (amount <= 0)
            throw new BadRequestException("Amount must be greater than zero.");
    }
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
            request.CategoryId,
            request.SourceId,
            request.TransactionType,
            request.Amount,
            request.TransactionDate,
            request.Note,
            cancellationToken
        );

        decimal newBalance = wallet.Balance;
        if (request.TransactionType == "INCOME")
            newBalance += request.Amount;
        else if (request.TransactionType == "EXPENSE" || request.TransactionType == "TRANSFER" || request.TransactionType == "DEBT_PAYMENT")
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
        
        if (transaction.TransactionType == "INCOME")
            newBalance -= transaction.Amount;
        else if (transaction.TransactionType == "EXPENSE" || transaction.TransactionType == "TRANSFER" || transaction.TransactionType == "DEBT_PAYMENT")
            newBalance += transaction.Amount;

        if (request.TransactionType == "INCOME")
            newBalance += request.Amount;
        else if (request.TransactionType == "EXPENSE" || request.TransactionType == "TRANSFER" || request.TransactionType == "DEBT_PAYMENT")
            newBalance -= request.Amount;

        var updatedTransaction = await _transactionRepository.UpdateAsync(
            request.TransactionId,
            request.CategoryId,
            request.SourceId,
            request.TransactionType,
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

        decimal newBalance = wallet.Balance;
        if (transaction.TransactionType == "INCOME")
            newBalance -= transaction.Amount;
        else if (transaction.TransactionType == "EXPENSE" || transaction.TransactionType == "TRANSFER" || transaction.TransactionType == "DEBT_PAYMENT")
            newBalance += transaction.Amount;

        await _transactionRepository.DeleteAsync(request.TransactionId, cancellationToken);
        await _walletRepository.UpdateBalanceAsync(transaction.WalletId, newBalance, cancellationToken);

        return true;
    }
}
