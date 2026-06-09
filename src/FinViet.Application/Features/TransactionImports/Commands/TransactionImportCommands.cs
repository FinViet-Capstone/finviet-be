using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.TransactionImports.Commands;

public class ImportBankExcelCommand : IRequest<ImportTransactionsResponseDto>
{
    public Guid WalletId { get; set; }
    public Guid CustomerId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Stream FileStream { get; set; } = Stream.Null;
    public int? MaxRows { get; set; }
}

public class ImportSmsPasteCommand : IRequest<ImportTransactionsResponseDto>
{
    public Guid WalletId { get; set; }
    public Guid CustomerId { get; set; }
    public string Content { get; set; } = string.Empty;
}
