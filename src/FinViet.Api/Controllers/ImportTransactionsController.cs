using FinViet.Application.DTOs;
using FinViet.Application.Features.TransactionImports.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/import-transactions")]
[Authorize(Roles = "Customer")]
public class ImportTransactionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ImportTransactionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("bank-excel")]
    public async Task<ActionResult<ImportTransactionsResponseDto>> ImportBankExcel([FromForm] BankExcelImportFormRequestDto request, CancellationToken cancellationToken)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(new { message = "File is required" });

        await using var stream = request.File.OpenReadStream();
        var command = new ImportBankExcelCommand
        {
            WalletId = request.WalletId,
            CustomerId = request.CustomerId,
            FileName = request.File.FileName,
            FileStream = stream,
            MaxRows = request.MaxRows
        };

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("sms-paste")]
    public async Task<ActionResult<ImportTransactionsResponseDto>> ImportSmsPaste([FromBody] SmsImportRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Content is required" });

        var command = new ImportSmsPasteCommand
        {
            WalletId = request.WalletId,
            CustomerId = request.CustomerId,
            Content = request.Content
        };

        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}

public class BankExcelImportFormRequestDto : BankExcelImportRequestDto
{
    public IFormFile File { get; set; } = null!;
}
