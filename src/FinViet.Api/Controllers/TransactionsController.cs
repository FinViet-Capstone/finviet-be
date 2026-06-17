using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs;
using FinViet.Application.DTOs.Transactions;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize(Roles = "Customer")]
public class TransactionsController : ControllerBase
{
    private readonly ITransactionService _service;
    private readonly ITransactionExtractService _extract;

    public TransactionsController(ITransactionService service, ITransactionExtractService extract)
    {
        _service = service;
        _extract = extract;
    }

    // GET /api/transactions — filter + paging
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<TransactionResponse>>>> GetTransactions(
        [FromQuery] TransactionQuery query, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _service.GetTransactionsAsync(customerId, query, cancellationToken);
        return Ok(ApiResponse<PagedResult<TransactionResponse>>.Ok(result, "Transactions retrieved successfully"));
    }

    // GET /api/transactions/summary?month=YYYY-MM
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<TransactionSummaryResponse>>> GetSummary(
        [FromQuery] string? month, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var (year, m) = ParseMonth(month);
        var result = await _service.GetSummaryAsync(customerId, year, m, cancellationToken);
        return Ok(ApiResponse<TransactionSummaryResponse>.Ok(result, "Transaction summary retrieved successfully"));
    }

    // GET /api/transactions/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<TransactionResponse>>> GetById(
        [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _service.GetByIdAsync(customerId, id, cancellationToken);
        if (result is null)
            return NotFound(ApiResponse<TransactionResponse>.Fail("Transaction not found."));
        return Ok(ApiResponse<TransactionResponse>.Ok(result, "Transaction retrieved successfully"));
    }

    // POST /api/transactions
    [HttpPost]
    public async Task<ActionResult<ApiResponse<TransactionResponse>>> Create(
        [FromBody] CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _service.CreateAsync(customerId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.TransactionId },
            ApiResponse<TransactionResponse>.Ok(result, "Transaction created successfully"));
    }

    // POST /api/transactions/batch — photo "Chấp nhận tất cả" (atomic)
    [HttpPost("batch")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TransactionResponse>>>> CreateBatch(
        [FromBody] BatchTransactionRequest request, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _service.CreateBatchAsync(customerId, request, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<TransactionResponse>>.Ok(result, "Transactions created successfully"));
    }

    // POST /api/transactions/transfer — internal two-leg transfer
    [HttpPost("transfer")]
    public async Task<ActionResult<ApiResponse<TransferResponse>>> Transfer(
        [FromBody] TransferRequest request, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _service.TransferAsync(customerId, request, cancellationToken);
        return Ok(ApiResponse<TransferResponse>.Ok(result, "Transfer completed successfully"));
    }

    // PATCH /api/transactions/{id} — partial update (category, amount, wallet, description, merchant, date)
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ApiResponse<TransactionResponse>>> Update(
        [FromRoute] Guid id, [FromBody] UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _service.UpdateAsync(customerId, id, request, cancellationToken);
        if (result is null)
            return NotFound(ApiResponse<TransactionResponse>.Fail("Transaction not found."));
        return Ok(ApiResponse<TransactionResponse>.Ok(result, "Transaction updated successfully"));
    }

    // DELETE /api/transactions/{id}
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(
        [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var deleted = await _service.DeleteAsync(customerId, id, cancellationToken);
        if (!deleted)
            return NotFound(ApiResponse<object?>.Fail("Transaction not found."));
        return Ok(ApiResponse<object?>.Ok(null, "Transaction deleted successfully"));
    }

    private static (int Year, int Month) ParseMonth(string? month)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            var now = DateTime.UtcNow;
            return (now.Year, now.Month);
        }

        // Accept "YYYY-MM".
        var parts = month.Split('-');
        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
            return (y, m);

        throw new Application.Common.Exceptions.BadRequestException("month must be in 'YYYY-MM' format.");
    }
}
