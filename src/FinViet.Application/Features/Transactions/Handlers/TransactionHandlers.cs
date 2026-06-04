using MediatR;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;

namespace FinViet.Application.Features.Transactions.Handlers;

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
        var wallet = await _walletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (wallet == null)
            throw new Exception($"Wallet {request.WalletId} not found");

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
        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction == null)
            throw new Exception($"Transaction {request.TransactionId} not found");

        var wallet = await _walletRepository.GetByIdAsync(transaction.WalletId, cancellationToken);
        if (wallet == null)
            throw new Exception($"Wallet not found");

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
            throw new Exception($"Transaction {request.TransactionId} not found");

        var wallet = await _walletRepository.GetByIdAsync(transaction.WalletId, cancellationToken);
        if (wallet == null)
            throw new Exception($"Wallet not found");

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
