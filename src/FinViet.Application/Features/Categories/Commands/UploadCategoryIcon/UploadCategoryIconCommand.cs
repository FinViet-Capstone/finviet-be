using MediatR;

namespace FinViet.Application.Features.Categories.Commands.UploadCategoryIcon;

public record UploadCategoryIconCommand(byte[] FileContent, string FileName, string ContentType) : IRequest<string>;
