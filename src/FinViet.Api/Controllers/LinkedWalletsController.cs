using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs.LinkedWallets;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

/// <summary>
/// SePay-backed linked-wallet endpoints. Adapts a Plaid-style connect/exchange/sync surface over
/// SePay's static Bearer token.
/// </summary>
[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/linked-wallets")]
public class LinkedWalletsController : ControllerBase
{
    private readonly ILinkedWalletService _linkedWalletService;

    public LinkedWalletsController(ILinkedWalletService linkedWalletService)
    {
        _linkedWalletService = linkedWalletService;
    }

    // Finverse (consumer bank aggregation) is served by WalletsController via
    // IFinverseWalletService; this controller keeps the SePay-backed surface only.

    /// <summary>List of banks that can be linked (static catalog).</summary>
    [HttpGet("institutions")]
    public ActionResult<ApiResponse<IReadOnlyList<InstitutionResponse>>> GetInstitutions(
        [FromQuery] string? country)
    {
        var data = _linkedWalletService.GetInstitutions(country);
        return Ok(ApiResponse<IReadOnlyList<InstitutionResponse>>.Ok(data, "Institutions retrieved successfully"));
    }

    /// <summary>Bank accounts discovered through the linked SePay token.</summary>
    [HttpGet("accounts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LinkedAccountResponse>>>> GetAccounts(
        [FromQuery] string accessToken,
        CancellationToken cancellationToken)
    {
        var data = await _linkedWalletService.GetAccountsAsync(accessToken, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<LinkedAccountResponse>>.Ok(data, "Linked accounts retrieved successfully"));
    }

    /// <summary>Validate the caller's SePay token and start a link session (returns accessToken + accounts).</summary>
    [HttpPost("connect")]
    public async Task<ActionResult<ApiResponse<ConnectResponse>>> Connect(
        [FromBody] ConnectRequest request,
        CancellationToken cancellationToken)
    {
        var data = await _linkedWalletService.ConnectAsync(request, cancellationToken);
        return Ok(ApiResponse<ConnectResponse>.Ok(data, "Connected successfully"));
    }

    /// <summary>One-step: create a SePay-linked wallet for the chosen account and bind the token.</summary>
    [HttpPost("link-account")]
    public async Task<ActionResult<ApiResponse<WalletResponse>>> LinkAccount(
        [FromBody] LinkAccountRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var data = await _linkedWalletService.LinkAccountAsync(customerId, request, cancellationToken);
        return Ok(ApiResponse<WalletResponse>.Ok(data, "Bank account linked successfully"));
    }

    /// <summary>Bind a SePay-linked wallet to the token resolved by the access token.</summary>
    [HttpPost("{id:guid}/link")]
    public async Task<ActionResult<ApiResponse<LinkWalletResponse>>> Link(
        [FromRoute] Guid id,
        [FromBody] LinkWalletRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var data = await _linkedWalletService.LinkAsync(customerId, id, request, cancellationToken);
        return Ok(ApiResponse<LinkWalletResponse>.Ok(data, "Wallet linked successfully"));
    }

    /// <summary>Pull SePay transactions into a SePay-linked wallet.</summary>
    [HttpPost("{id:guid}/sync")]
    public async Task<ActionResult<ApiResponse<SyncResultResponse>>> Sync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var data = await _linkedWalletService.SyncAsync(customerId, id, cancellationToken);
        return Ok(ApiResponse<SyncResultResponse>.Ok(data, "Wallet synced successfully"));
    }
}
