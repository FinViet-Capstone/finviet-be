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
    private readonly IBudgetService _budgetService;

    public CreateTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _budgetService = budgetService;
    }

    public async Task<TransactionResponseDto> Handle(CreateTransactionCommand request, CancellationToken cancellationToken)
    {
        TransactionRules.ValidateInput(request.TransactionType, request.Amount);

        var wallet = await _walletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", request.WalletId);

        // Ownership: ví phải thuộc về customer đang đăng nhập.
        if (wallet.CustomerId != request.CustomerId)
            throw new ForbiddenException("You do not have access to this wallet.");

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

        // Chặn chi vượt số dư (đẩy ví xuống âm và làm xấu đi). Thu nhập / thao tác cải thiện không bị chặn.
        if (newBalance < 0 && newBalance < wallet.Balance)
            throw new BusinessRuleException("Wallet balance is insufficient for this transaction.", "insufficient_balance");

        await _walletRepository.UpdateBalanceAsync(request.WalletId, newBalance, cancellationToken);

        // Cập nhật ngân sách + bắn alert 80%/100% theo business logic 2b.
        await _budgetService.SyncBudgetOnTransactionChangeAsync(
            wallet.CustomerId, DateOnly.FromDateTime(request.TransactionDate), cancellationToken);

        return transaction;
    }
}

public class UpdateTransactionHandler : IRequestHandler<UpdateTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly IBudgetService _budgetService;

    public UpdateTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _budgetService = budgetService;
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

        // Ownership: giao dịch phải thuộc ví của customer đang đăng nhập.
        if (wallet.CustomerId != request.CustomerId)
            throw new ForbiddenException("You do not have access to this transaction.");

        decimal newBalance = wallet.Balance;

        if (transaction.TransactionType == "INCOME")
            newBalance -= transaction.Amount;
        else if (transaction.TransactionType == "EXPENSE" || transaction.TransactionType == "TRANSFER" || transaction.TransactionType == "DEBT_PAYMENT")
            newBalance += transaction.Amount;

        if (request.TransactionType == "INCOME")
            newBalance += request.Amount;
        else if (request.TransactionType == "EXPENSE" || request.TransactionType == "TRANSFER" || request.TransactionType == "DEBT_PAYMENT")
            newBalance -= request.Amount;

        // Chặn sửa giao dịch làm ví xuống âm và xấu đi. Sửa để cải thiện số dư vẫn cho phép.
        if (newBalance < 0 && newBalance < wallet.Balance)
            throw new BusinessRuleException("Wallet balance is insufficient for this transaction.", "insufficient_balance");

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

        // Cập nhật ngân sách + alert sau khi sửa giao dịch.
        await _budgetService.SyncBudgetOnTransactionChangeAsync(
            wallet.CustomerId, DateOnly.FromDateTime(request.TransactionDate), cancellationToken);

        return updatedTransaction;
    }
}

public class DeleteTransactionHandler : IRequestHandler<DeleteTransactionCommand, bool>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly IBudgetService _budgetService;

    public DeleteTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _budgetService = budgetService;
    }

    public async Task<bool> Handle(DeleteTransactionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction == null)
            throw new NotFoundException("Transaction", request.TransactionId);

        // Transfer: xóa CẢ 2 vế + hoàn tiền 2 ví trong 1 DB transaction (FOR UPDATE) — atomic.
        // Repo tự kiểm tra ownership theo customerId. Transfer không tính vào budget nên không sync.
        if (transaction.TransferPairId.HasValue)
            return await _transactionRepository.DeleteTransferPairAsync(
                transaction.TransferPairId.Value, request.CustomerId, cancellationToken);

        var wallet = await _walletRepository.GetByIdAsync(transaction.WalletId, cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", transaction.WalletId);

        // Ownership: giao dịch phải thuộc ví của customer đang đăng nhập.
        if (wallet.CustomerId != request.CustomerId)
            throw new ForbiddenException("You do not have access to this transaction.");

        decimal newBalance = wallet.Balance;
        if (transaction.TransactionType == "INCOME")
            newBalance -= transaction.Amount;
        else if (transaction.TransactionType == "EXPENSE" || transaction.TransactionType == "TRANSFER" || transaction.TransactionType == "DEBT_PAYMENT")
            newBalance += transaction.Amount;

        await _transactionRepository.DeleteAsync(request.TransactionId, cancellationToken);
        await _walletRepository.UpdateBalanceAsync(transaction.WalletId, newBalance, cancellationToken);

        // Cập nhật ngân sách sau khi xóa giao dịch (có thể tụt mốc alert).
        await _budgetService.SyncBudgetOnTransactionChangeAsync(
            wallet.CustomerId, DateOnly.FromDateTime(transaction.TransactionDate), cancellationToken);

        return true;
    }
}

public class ClassifyTransactionHandler : IRequestHandler<ClassifyTransactionCommand, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly ICategoryService _categoryService;
    private readonly IIncomeSourceService _incomeSourceService;
    private readonly IBudgetService _budgetService;

    public ClassifyTransactionHandler(
        ITransactionRepository transactionRepository,
        IWalletRepository walletRepository,
        ICategoryService categoryService,
        IIncomeSourceService incomeSourceService,
        IBudgetService budgetService)
    {
        _transactionRepository = transactionRepository;
        _walletRepository = walletRepository;
        _categoryService = categoryService;
        _incomeSourceService = incomeSourceService;
        _budgetService = budgetService;
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

        if (request.CategoryId.HasValue)
        {
            var category = await _categoryService.GetCategoryByIdAsync(request.CategoryId.Value, cancellationToken);
            if (category == null)
                throw new NotFoundException("Category", request.CategoryId.Value);
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

        // Đổi danh mục → chi tiêu theo ngân sách thay đổi → cập nhật + alert.
        await _budgetService.SyncBudgetOnTransactionChangeAsync(
            request.CustomerId, DateOnly.FromDateTime(classified!.TransactionDate), cancellationToken);

        return classified!;
    }
}
