using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Infrastructure.Persistence.Repositories;

public class TransactionImportRepository : ITransactionImportRepository
{
    private readonly FinVietDbContext _context;

    public TransactionImportRepository(FinVietDbContext context)
    {
        _context = context;
    }

    public async Task<ImportTransactionsResponseDto> SaveImportedTransactionsAsync(
        Guid walletId,
        Guid? customerId,
        string fileName,
        List<ParsedTransactionDto> rows,
        CancellationToken cancellationToken = default)
    {
        var response = new ImportTransactionsResponseDto
        {
            TotalParsed = rows.Count
        };

        var wallet = await _context.Wallets.FindAsync(new object[] { walletId }, cancellationToken: cancellationToken);
        if (wallet == null)
            throw new Exception($"Wallet {walletId} not found");

        if (rows.Count == 0)
            return response;

        var batch = new ImportBatch
        {
            BatchId = Guid.NewGuid(),
            CustomerId = customerId ?? GetRequiredCustomerId(wallet),
            WalletId = wallet.WalletId,
            FileName = fileName,
            ImportDate = DateTime.UtcNow
        };

        _context.ImportBatches.Add(batch);

        var balance = GetRequiredBalance(wallet);
        foreach (var row in rows)
        {
            if (row.Amount <= 0)
            {
                response.Skipped++;
                response.Errors.Add($"Skipped invalid amount: {row.RawText}");
                continue;
            }

            var transaction = new Transaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = wallet.WalletId,
                CategoryId = null,
                SourceId = null,
                BatchId = batch.BatchId,
                TransactionType = row.TransactionType,
                Amount = row.Amount,
                TransactionDate = DateTime.SpecifyKind(row.TransactionDate, DateTimeKind.Utc),
                Note = row.Note
            };

            _context.Transactions.Add(transaction);
            response.Imported++;
            response.Transactions.Add(new ImportedTransactionDto
            {
                TransactionId = transaction.TransactionId,
                TransactionType = transaction.TransactionType,
                Amount = transaction.Amount,
                TransactionDate = transaction.TransactionDate.Value,
                Note = transaction.Note
            });

            if (transaction.TransactionType == "INCOME")
                balance += transaction.Amount;
            else
                balance -= transaction.Amount;
        }

        wallet.Balance = balance;
        _context.Wallets.Update(wallet);
        await _context.SaveChangesAsync(cancellationToken);

        response.BatchId = batch.BatchId;
        response.NewWalletBalance = GetRequiredBalance(wallet);
        return response;
    }

    private static Guid GetRequiredCustomerId(Wallet wallet)
        => wallet.CustomerId
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing customer_id.");

    private static decimal GetRequiredBalance(Wallet wallet)
        => wallet.Balance
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing balance.");
}
