using FinViet.Application.Features.Categories.Commands.UploadCategoryIcon;
using FinViet.Application.Interfaces;
using MediatR;

namespace FinViet.Infrastructure.Features.Categories.Commands.UploadCategoryIcon;

public class UploadCategoryIconCommandHandler : IRequestHandler<UploadCategoryIconCommand, string>
{
    private readonly ICategoryIconService _icons;

    public UploadCategoryIconCommandHandler(ICategoryIconService icons) => _icons = icons;

    public async Task<string> Handle(UploadCategoryIconCommand request, CancellationToken cancellationToken)
    {
        CategoryIconValidationRules.Validate(request.FileContent, request.ContentType);
        return await _icons.UploadAsync(request.FileContent, request.FileName, request.ContentType);
    }
}
