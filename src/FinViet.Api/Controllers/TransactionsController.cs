using FinViet.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FinViet.Application.Common;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.DTOs;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("v1/transactions")]
[Authorize(Roles = "Customer")]
public class TransactionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWalletService _walletService;

    public TransactionsController(IMediator mediator, IWalletService walletService)
    {
        _mediator = mediator;
        _walletService = walletService;
    }

    [HttpPost]
    public async Task<ActionResult<TransactionResponseDto>> CreateTransaction([FromBody] CreateTransactionDto dto)
    {
        var command = new CreateTransactionCommand
        {
            CustomerId = User.GetCustomerId(),
            WalletId = dto.WalletId,
            CategoryId = dto.CategoryId,
            TransactionType = dto.EffectiveType,
            Amount = dto.Amount,
            TransactionDate = dto.TransactionDate,
            Description = dto.Description,
            Merchant = dto.Merchant,
            EntryMethod = dto.EntryMethod
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(CreateTransaction), result);
    }

    [HttpPut("{id}")]
    [HttpPatch("{id}")]
    public async Task<ActionResult<TransactionResponseDto>> UpdateTransaction(Guid id, [FromBody] UpdateTransactionDto dto)
    {
        var command = new UpdateTransactionCommand
        {
            CustomerId = User.GetCustomerId(),
            TransactionId = id,
            CategoryId = dto.CategoryId,
            TransactionType = dto.EffectiveType,
            Amount = dto.Amount,
            TransactionDate = dto.TransactionDate,
            Description = dto.Description,
            Merchant = dto.Merchant
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<bool>> DeleteTransaction(Guid id)
    {
        var command = new DeleteTransactionCommand
        {
            CustomerId = User.GetCustomerId(),
            TransactionId = id
        };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpPatch("{id}/classify")]
    public async Task<ActionResult<TransactionResponseDto>> ClassifyTransaction(Guid id, [FromBody] ClassifyTransactionDto dto)
    {
        var command = new ClassifyTransactionCommand
        {
            CustomerId = User.GetCustomerId(),
            TransactionId = id,
            CategoryId = dto.CategoryId
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("transfer")]
    public async Task<ActionResult<ApiResponse<TransferWalletResponse>>> TransferBetweenWallets(
        [FromBody] TransferWalletRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _walletService.TransferAsync(
            User.GetCustomerId(),
            request,
            cancellationToken);

        return Ok(ApiResponse<TransferWalletResponse>.Ok(
            result,
            "Transfer completed successfully"));
    }
}
