using FinViet.Application.DTOs;
using FinViet.Application.Features.TransactionImports.Commands;
using FinViet.Application.Interfaces;
using MediatR;

namespace FinViet.Application.Features.TransactionImports.Handlers;

public class ImportBankExcelHandler : IRequestHandler<ImportBankExcelCommand, ImportTransactionsResponseDto>
{
    private readonly IBankStatementParser _bankStatementParser;
    private readonly ITransactionImportRepository _transactionImportRepository;

    public ImportBankExcelHandler(
        IBankStatementParser bankStatementParser,
        ITransactionImportRepository transactionImportRepository)
    {
        _bankStatementParser = bankStatementParser;
        _transactionImportRepository = transactionImportRepository;
    }

    public Task<ImportTransactionsResponseDto> Handle(ImportBankExcelCommand request, CancellationToken cancellationToken)
    {
        var rows = _bankStatementParser.Parse(request.FileStream, request.MaxRows);
        return _transactionImportRepository.SaveImportedTransactionsAsync(
            request.WalletId,
            request.CustomerId,
            request.FileName,
            rows,
            cancellationToken);
    }
}

public class ImportSmsPasteHandler : IRequestHandler<ImportSmsPasteCommand, ImportTransactionsResponseDto>
{
    private readonly ISmsTransactionParser _smsTransactionParser;
    private readonly ITransactionImportRepository _transactionImportRepository;

    public ImportSmsPasteHandler(
        ISmsTransactionParser smsTransactionParser,
        ITransactionImportRepository transactionImportRepository)
    {
        _smsTransactionParser = smsTransactionParser;
        _transactionImportRepository = transactionImportRepository;
    }

    public Task<ImportTransactionsResponseDto> Handle(ImportSmsPasteCommand request, CancellationToken cancellationToken)
    {
        var rows = _smsTransactionParser.Parse(request.Content);
        return _transactionImportRepository.SaveImportedTransactionsAsync(
            request.WalletId,
            request.CustomerId,
            "sms-paste",
            rows,
            cancellationToken);
    }
}
