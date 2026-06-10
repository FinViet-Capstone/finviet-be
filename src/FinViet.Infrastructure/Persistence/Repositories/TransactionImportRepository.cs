using FinViet.Application.Common.Exceptions;
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
        Guid customerId,
        string fileName,
        string sourceChannel,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        var rows = parseResult.Rows;
        var response = new ImportTransactionsResponseDto
        {
            TotalRowsScanned = parseResult.TotalRowsScanned,
            TotalParsed = rows.Count,
            Skipped = parseResult.SkippedDuringParse,
            Errors = new List<string>(parseResult.ParseErrors)
        };

        var wallet = await _context.Wallets.FindAsync(new object[] { walletId }, cancellationToken: cancellationToken);
        if (wallet == null)
            throw new NotFoundException("Wallet", walletId);

        // Ownership check: the wallet must belong to the authenticated customer.
        if (wallet.CustomerId != customerId)
            throw new ForbiddenException("You do not have access to this wallet.");

        if (rows.Count == 0)
            return response;

        var batch = new ImportBatch
        {
            BatchId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = wallet.WalletId,
            FileName = fileName,
            ImportDate = DateTime.UtcNow
        };

        _context.ImportBatches.Add(batch);

        decimal balance = wallet.Balance;
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
                SourceChannel = sourceChannel,
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
        response.NewWalletBalance = wallet.Balance;
        return response;
    }
}
