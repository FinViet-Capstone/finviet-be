namespace FinViet.Application.Interfaces;

public interface ICategoryIconService
{
    /// <summary>
    /// Saves a custom category icon (SVG) to local storage and returns the public URL path.
    /// </summary>
    Task<string> UploadAsync(byte[] fileContent, string fileName, string contentType);
}
