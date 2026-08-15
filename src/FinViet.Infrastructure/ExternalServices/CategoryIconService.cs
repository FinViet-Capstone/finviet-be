using FinViet.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FinViet.Infrastructure.ExternalServices;

public class CategoryIconService : ICategoryIconService
{
    private readonly string _iconsDirectory;
    private readonly string _baseUrlPath;

    public CategoryIconService(IConfiguration config)
    {
        // AppSettings:WebRootPath is configured as "" (not absent), so a plain `??` fallback
        // never triggers — an empty string is a valid non-null value.
        var configuredWebRoot = config["AppSettings:WebRootPath"];
        var webRoot = string.IsNullOrWhiteSpace(configuredWebRoot)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : configuredWebRoot;
        _iconsDirectory = Path.Combine(webRoot, "category-icons");
        _baseUrlPath    = "/category-icons";

        Directory.CreateDirectory(_iconsDirectory);
    }

    public async Task<string> UploadAsync(byte[] fileContent, string fileName, string contentType)
    {
        var uniqueName = $"{Guid.NewGuid():N}.svg";
        var filePath   = Path.Combine(_iconsDirectory, uniqueName);

        await File.WriteAllBytesAsync(filePath, fileContent);

        return $"{_baseUrlPath}/{uniqueName}";
    }
}
