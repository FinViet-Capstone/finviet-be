using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface IFinancialContextService
{
    Task<FinancialContextResult> BuildCurrentMonthAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}
