using FinViet.Application.Common;
using FinViet.Application.DTOs.CategoryCorrections;
using MediatR;

namespace FinViet.Application.Features.CategoryCorrections.Queries.GetCategoryCorrections;

public record GetCategoryCorrectionsQuery(CategoryCorrectionQueryDto Query)
    : IRequest<PagedResult<CategoryCorrectionResponseDto>>;
