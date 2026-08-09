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
    private readonly ISepayWalletService _sepayWalletService;

    public WalletsController(
        IWalletService walletService,
        ISepayWalletService sepayWalletService)
    {
        _walletService = walletService;
        _sepayWalletService = sepayWalletService;
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
    public async Task<ActionResult<ApiResponse<WithdrawWalletResponse>>> Withdraw(
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

        return Ok(ApiResponse<WithdrawWalletResponse>.Ok(
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

    // ── SePay ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Start the OAuth2 flow: returns the SePay authorization URL plus a signed <c>state</c> the
    /// client must echo back when linking.
    /// </summary>
    [HttpGet("sepay/authorize-url")]
    public ActionResult<ApiResponse<SepayAuthorizeUrlResponse>> GetSepayAuthorizeUrl()
    {
        var result = _sepayWalletService.CreateAuthorizeUrl(User.GetCustomerId());

        return Ok(ApiResponse<SepayAuthorizeUrlResponse>.Ok(
            result,
            "SePay authorization URL created successfully"));
    }

    [HttpPost("sepay/link")]
    public async Task<ActionResult<ApiResponse<SepayLinkResult>>> LinkSepayAccount(
        [FromBody] LinkSepayAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.LinkAccountAsync(
            User.GetCustomerId(),
            request,
            cancellationToken);

        return Ok(ApiResponse<SepayLinkResult>.Ok(
            result,
            "SePay bank account linked successfully"));
    }

    [HttpPost("sepay/link-token")]
    public async Task<ActionResult<ApiResponse<SepayLinkResult>>> LinkSepayWithToken(
        [FromBody] LinkSepayTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.LinkWithTokenAsync(
            User.GetCustomerId(),
            request,
            cancellationToken);

        return Ok(ApiResponse<SepayLinkResult>.Ok(
            result,
            "SePay bank account linked successfully"));
    }

    [HttpPost("sepay/bank-accounts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SepayBankAccountResponse>>>> GetSepayBankAccounts(
        [FromBody] SepayBankAccountsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.GetBankAccountsAsync(
            User.GetCustomerId(),
            request,
            cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<SepayBankAccountResponse>>.Ok(
            result,
            "SePay bank accounts retrieved successfully"));
    }

    /// <summary>Connection state of every SePay-linked wallet (bank, mask, last sync, re-link flag).</summary>
    [HttpGet("sepay/links")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SepayLinkStatusResponse>>>> GetSepayLinks(
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.GetLinksAsync(
            User.GetCustomerId(),
            cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<SepayLinkStatusResponse>>.Ok(
            result,
            "SePay links retrieved successfully"));
    }

    [HttpPost("{id:guid}/sepay-sync")]
    public async Task<ActionResult<ApiResponse<SepayWalletSyncResponse>>> SyncSepayWallet(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.SyncWalletAsync(
            User.GetCustomerId(),
            id,
            cancellationToken);

        return Ok(ApiResponse<SepayWalletSyncResponse>.Ok(
            result,
            "SePay wallet synchronized successfully"));
    }

    /// <summary>Sync every SePay-linked wallet at once; per-wallet failures are reported, not fatal.</summary>
    [HttpPost("sepay/sync-all")]
    public async Task<ActionResult<ApiResponse<SepaySyncAllResponse>>> SyncAllSepayWallets(
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.SyncAllWalletsAsync(
            User.GetCustomerId(),
            cancellationToken);

        return Ok(ApiResponse<SepaySyncAllResponse>.Ok(
            result,
            "SePay wallets synchronized successfully"));
    }

    /// <summary>
    /// Register this API's receiver as a webhook on SePay for a linked wallet, so transactions
    /// arrive in real time. Idempotent — an existing registration for the same account and URL is
    /// adopted instead of duplicated. Runs automatically on link when SePay:WebhookUrl is set.
    /// </summary>
    [HttpPost("{id:guid}/sepay-webhook")]
    public async Task<ActionResult<ApiResponse<SepayWebhookRegistrationResponse>>> RegisterSepayWebhook(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.RegisterWebhookAsync(
            User.GetCustomerId(),
            id,
            cancellationToken);

        return Ok(ApiResponse<SepayWebhookRegistrationResponse>.Ok(
            result,
            result.AlreadyExisted
                ? "SePay webhook already registered"
                : "SePay webhook registered successfully"));
    }

    /// <summary>Delete the webhook FinViet registered on SePay for this wallet.</summary>
    [HttpDelete("{id:guid}/sepay-webhook")]
    public async Task<ActionResult<ApiResponse<SepayWebhookRegistrationResponse>>> UnregisterSepayWebhook(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.UnregisterWebhookAsync(
            User.GetCustomerId(),
            id,
            cancellationToken);

        return Ok(ApiResponse<SepayWebhookRegistrationResponse>.Ok(
            result,
            "SePay webhook unregistered successfully"));
    }

    /// <summary>Drop the SePay authorization and turn the wallet back into a manual one.</summary>
    [HttpDelete("{id:guid}/sepay-link")]
    public async Task<ActionResult<ApiResponse<SepayUnlinkResponse>>> UnlinkSepayWallet(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.UnlinkWalletAsync(
            User.GetCustomerId(),
            id,
            cancellationToken);

        return Ok(ApiResponse<SepayUnlinkResponse>.Ok(
            result,
            "SePay bank account unlinked successfully"));
    }

    /// <summary>
    /// SePay webhook receiver — called by SePay the moment the bank posts a transaction, so a
    /// linked wallet stays current without waiting for a manual sync. Authenticated by the shared
    /// <c>Authorization: Apikey &lt;SePay:WebhookApiKey&gt;</c> header, not by a customer JWT.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("sepay/webhook")]
    public async Task<ActionResult<ApiResponse<SepayWebhookResult>>> SepayWebhook(
        [FromBody] SepayWebhookRequest payload,
        CancellationToken cancellationToken)
    {
        var result = await _sepayWalletService.HandleWebhookAsync(
            Request.Headers.Authorization.ToString(),
            payload,
            cancellationToken);

        return Ok(ApiResponse<SepayWebhookResult>.Ok(
            result,
            "SePay webhook processed successfully"));
    }
}
