namespace FinViet.Application.Interfaces;

public interface IAvatarService
{
    /// <summary>
    /// Saves avatar file to local storage and returns the public URL path.
    /// </summary>
    Task<string> UploadAsync(byte[] fileContent, string fileName, string contentType);

    /// <summary>Deletes avatar file from local storage if it exists.</summary>
    Task DeleteAsync(string avatarUrl);
}
