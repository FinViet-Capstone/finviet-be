using FinViet.Application.Common.Exceptions;

namespace FinViet.Infrastructure.Features.Profile.Commands.UploadAvatar;

internal static class AvatarValidationRules
{
    private const int MaximumFileSize = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    internal static void Validate(byte[] content, string contentType)
    {
        var normalizedContentType = contentType.ToLowerInvariant();
        if (!AllowedContentTypes.Contains(normalizedContentType))
            throw new BadRequestException("Only JPEG, PNG, and WebP images are allowed.");

        if (content.Length > MaximumFileSize)
            throw new BadRequestException("Avatar file size must not exceed 5 MB.");

        if (!HasValidMagicBytes(content, normalizedContentType))
            throw new BadRequestException("File content does not match the declared image type.");
    }

    internal static bool HasValidMagicBytes(byte[] content, string contentType)
    {
        if (content.Length < 4) return false;

        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF,
            "image/png" => content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47,
            "image/webp" => content.Length >= 12
                            && content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46
                            && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50,
            _ => false
        };
    }
}
