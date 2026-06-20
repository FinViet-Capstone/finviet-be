using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FinViet.Application.Common;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.Features.Transactions.Queries;
using FinViet.Application.DTOs;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/[controller]")]
public class TransactionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TransactionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TransactionResponseDto>>> GetTransactions(
        [FromQuery] TransactionQueryDto query)
    {
        var result = await _mediator.Send(new GetTransactionsQuery
        {
            CustomerId = GetCustomerId(),
            Filter = query
        });

        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<TransactionSummaryResponseDto>> GetSummary(
        [FromQuery] int year, [FromQuery] int month)
    {
        var result = await _mediator.Send(new GetTransactionSummaryQuery
        {
            CustomerId = GetCustomerId(),
            Year = year,
            Month = month
        });

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransactionResponseDto>> GetTransactionById(Guid id)
    {
        var result = await _mediator.Send(new GetTransactionByIdQuery
        {
            CustomerId = GetCustomerId(),
            TransactionId = id
        });

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TransactionResponseDto>> CreateTransaction([FromBody] CreateTransactionDto dto)
    {
        var command = new CreateTransactionCommand
        {
            WalletId = dto.WalletId,
            CategoryId = dto.CategoryId,
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

    [HttpPatch("{id}/classify")]
    public async Task<ActionResult<TransactionResponseDto>> ClassifyTransaction(Guid id, [FromBody] ClassifyTransactionDto dto)
    {
        var command = new ClassifyTransactionCommand
        {
            CustomerId = GetCustomerId(),
            TransactionId = id,
            CategoryId = dto.CategoryId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    private Guid GetCustomerId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(claimValue, out var customerId))
            return Guid.Empty;

        return customerId;
    }
}
