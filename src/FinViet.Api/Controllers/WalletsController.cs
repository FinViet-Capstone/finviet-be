using System.Security.Claims;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize]
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
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var wallets = await _walletService.GetWalletsAsync(customerId, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<WalletResponse>>.Ok(
            wallets,
            "Wallets retrieved successfully"));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> CreateWallet(
        [FromBody] CreateWalletRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
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
        var customerId = GetCustomerId();
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
        var customerId = GetCustomerId();
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
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
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
        [FromBody] TransferWalletRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
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
        [FromRoute] Guid id,
        [FromQuery] WalletTransactionQuery query,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var result = await _walletService.GetWalletTransactionsAsync(
            customerId,
            id,
            query,
            cancellationToken);

        return Ok(ApiResponse<PagedResult<WalletTransactionResponse>>.Ok(
            result,
            "Wallet transactions retrieved successfully"));
    }

    private Guid GetCustomerId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(claimValue, out var customerId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid customer identifier claim.");

        return customerId;
    }
}
