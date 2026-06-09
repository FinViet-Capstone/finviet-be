using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.DTOs;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Customer")]
public class TransactionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TransactionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<ActionResult<TransactionResponseDto>> CreateTransaction([FromBody] CreateTransactionDto dto)
    {
        var command = new CreateTransactionCommand
        {
            WalletId = dto.WalletId,
            CategoryId = dto.CategoryId,
            SourceId = dto.SourceId,
            TransactionType = dto.TransactionType,
            Amount = dto.Amount,
            TransactionDate = dto.TransactionDate,
            Note = dto.Note
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(CreateTransaction), result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TransactionResponseDto>> UpdateTransaction(Guid id, [FromBody] UpdateTransactionDto dto)
    {
        var command = new UpdateTransactionCommand
        {
            TransactionId = id,
            CategoryId = dto.CategoryId,
            SourceId = dto.SourceId,
            TransactionType = dto.TransactionType,
            Amount = dto.Amount,
            TransactionDate = dto.TransactionDate,
            Note = dto.Note
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<bool>> DeleteTransaction(Guid id)
    {
        var command = new DeleteTransactionCommand { TransactionId = id };
        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
