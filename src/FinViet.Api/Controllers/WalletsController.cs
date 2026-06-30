using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/wallets")]
public class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletsController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<WalletListResponse>>> GetWallets(
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var wallets = await _walletService.GetWalletsAsync(customerId, cancellationToken);

        return Ok(ApiResponse<WalletListResponse>.Ok(
            wallets,
            "Wallets retrieved successfully"));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> CreateWallet(
        [FromBody] CreateWalletRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var wallet = await _walletService.CreateWalletAsync(
            customerId,
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetWalletById),
            new { id = wallet.WalletId },
            ApiResponse<WalletResponse>.Ok(
                wallet,
                "Wallet created successfully"));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> GetWalletById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var wallet = await _walletService.GetWalletByIdAsync(
            customerId,
            id,
            cancellationToken);

        if (wallet is null)
            return NotFound(ApiResponse<WalletResponse>.Fail("Wallet not found."));

        return Ok(ApiResponse<WalletResponse>.Ok(
            wallet,
            "Wallet retrieved successfully"));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> UpdateWallet(
        [FromRoute] Guid id,
        [FromBody] UpdateWalletRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var wallet = await _walletService.UpdateWalletAsync(
            customerId,
            id,
            request,
            cancellationToken);

        if (wallet is null)
            return NotFound(ApiResponse<WalletResponse>.Fail("Wallet not found."));

        return Ok(ApiResponse<WalletResponse>.Ok(
            wallet,
            "Wallet updated successfully"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWallet(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var deleted = await _walletService.DeleteWalletAsync(
            customerId,
            id,
            cancellationToken);

        if (!deleted)
            return NotFound(ApiResponse<object?>.Fail("Wallet not found."));

        return NoContent();
    }

    [HttpPost("transfer")]
    public async Task<ActionResult<ApiResponse<TransferWalletResponse>>> TransferBetweenWallets(
        [FromBody] TransferWalletRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _walletService.TransferAsync(
            customerId,
            request,
            idempotencyKey,
            cancellationToken);

        return Ok(ApiResponse<TransferWalletResponse>.Ok(
            result,
            "Transfer completed successfully"));
    }

    [HttpPost("withdraw")]
    public async Task<ActionResult<ApiResponse<TransferWalletResponse>>> Withdraw(
        [FromBody] WithdrawWalletRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _walletService.WithdrawAsync(
            customerId,
            request,
            idempotencyKey,
            cancellationToken);

        return Ok(ApiResponse<TransferWalletResponse>.Ok(
            result,
            "Withdrawal completed successfully"));
    }

    [HttpGet("{id:guid}/transactions")]
    public async Task<ActionResult<ApiResponse<PagedResult<WalletTransactionResponse>>>> GetWalletTransactions(
        [FromRoute] Guid id,
        [FromQuery] WalletTransactionQuery query,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _walletService.GetWalletTransactionsAsync(
            customerId,
            id,
            query,
            cancellationToken);

        return Ok(ApiResponse<PagedResult<WalletTransactionResponse>>.Ok(
            result,
            "Wallet transactions retrieved successfully"));
    }

}
