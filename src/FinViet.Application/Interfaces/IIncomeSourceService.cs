using FinViet.Application.DTOs.IncomeSources;

namespace FinViet.Application.Interfaces;

public interface IIncomeSourceService
{
    Task<IReadOnlyList<IncomeSourceResponse>> GetIncomeSourcesAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<IncomeSourceResponse?> GetIncomeSourceByIdAsync(
        Guid customerId,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    Task<IncomeSourceResponse> CreateIncomeSourceAsync(
        Guid customerId,
        CreateIncomeSourceRequest request,
        CancellationToken cancellationToken = default);

    Task<IncomeSourceResponse?> UpdateIncomeSourceAsync(
        Guid customerId,
        Guid sourceId,
        UpdateIncomeSourceRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteIncomeSourceAsync(
        Guid customerId,
        Guid sourceId,
        CancellationToken cancellationToken = default);
}
