using FinViet.Application.Common;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/wallets")]
public class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletsController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WalletResponse>>>> GetWallets(
        [FromHeader(Name = "X-Customer-Id")] Guid customerId,
        CancellationToken cancellationToken)
    {
        var wallets = await _walletService.GetWalletsAsync(customerId, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<WalletResponse>>.Ok(
            wallets,
            "Wallets retrieved successfully"));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> CreateWallet(
        [FromHeader(Name = "X-Customer-Id")] Guid customerId,
        [FromBody] CreateWalletRequest request,
        CancellationToken cancellationToken)
    {
        var wallet = await _walletService.CreateWalletAsync(
            customerId,
            request,
            cancellationToken);

        return Ok(ApiResponse<WalletResponse>.Ok(
            wallet,
            "Wallet created successfully"));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> GetWalletById(
        [FromHeader(Name = "X-Customer-Id")] Guid customerId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
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
        [FromHeader(Name = "X-Customer-Id")] Guid customerId,
        [FromRoute] Guid id,
        [FromBody] UpdateWalletRequest request,
        CancellationToken cancellationToken)
    {
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
    public async Task<ActionResult<ApiResponse<object?>>> DeleteWallet(
        [FromHeader(Name = "X-Customer-Id")] Guid customerId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await _walletService.DeleteWalletAsync(
            customerId,
            id,
            cancellationToken);

        if (!deleted)
            return NotFound(ApiResponse<object?>.Fail("Wallet not found."));

        return Ok(ApiResponse<object?>.Ok(
            null,
            "Wallet deleted successfully"));
    }

    [HttpPost("transfer")]
    public async Task<ActionResult<ApiResponse<TransferWalletResponse>>> TransferBetweenWallets(
        [FromHeader(Name = "X-Customer-Id")] Guid customerId,
        [FromBody] TransferWalletRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _walletService.TransferAsync(
            customerId,
            request,
            cancellationToken);

        return Ok(ApiResponse<TransferWalletResponse>.Ok(
            result,
            "Transfer completed successfully"));
    }

    [HttpGet("{id:guid}/transactions")]
    public async Task<ActionResult<ApiResponse<PagedResult<WalletTransactionResponse>>>> GetWalletTransactions(
        [FromHeader(Name = "X-Customer-Id")] Guid customerId,
        [FromRoute] Guid id,
        [FromQuery] WalletTransactionQuery query,
        CancellationToken cancellationToken)
    {
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
