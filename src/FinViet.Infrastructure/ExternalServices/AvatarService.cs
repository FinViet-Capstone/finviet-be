using FinViet.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FinViet.Infrastructure.ExternalServices;

public class AvatarService : IAvatarService
{
    private readonly string _avatarsDirectory;
    private readonly string _baseUrlPath;

    public AvatarService(IConfiguration config)
    {
        // WebRootPath equivalent: configured via appsettings, defaults to wwwroot/avatars
        var webRoot      = config["AppSettings:WebRootPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        _avatarsDirectory= Path.Combine(webRoot, "avatars");
        _baseUrlPath     = "/avatars";

        Directory.CreateDirectory(_avatarsDirectory);
    }

    public async Task<string> UploadAsync(byte[] fileContent, string fileName, string contentType)
    {
        var extension  = GetExtension(contentType);
        var uniqueName = $"{Guid.NewGuid():N}{extension}";
        var filePath   = Path.Combine(_avatarsDirectory, uniqueName);

        await File.WriteAllBytesAsync(filePath, fileContent);

        return $"{_baseUrlPath}/{uniqueName}";
    }

    public Task DeleteAsync(string avatarUrl)
    {
        if (string.IsNullOrEmpty(avatarUrl)) return Task.CompletedTask;

        var fileName = Path.GetFileName(avatarUrl);
        var filePath = Path.Combine(_avatarsDirectory, fileName);

        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    private static string GetExtension(string contentType) => contentType.ToLower() switch
    {
        "image/jpeg" => ".jpg",
        "image/png"  => ".png",
        "image/webp" => ".webp",
        _            => ".jpg"
    };
}
