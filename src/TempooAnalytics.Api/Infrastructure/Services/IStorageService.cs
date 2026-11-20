namespace TempooAnalytics.Api.Infrastructure.Services;

public interface IStorageService
{
    /// <summary>
    /// Uploads a file to the configured storage.
    /// </summary>
    /// <param name="key">Unique name of the file (path in the bucket).</param>
    /// <param name="content">Content of the file.</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <returns>The key of the uploaded file.</returns>
    Task<string> UploadFileAsync(string key, Stream content, string contentType);

    /// <summary>
    /// Generates a temporary URL to download the file securely.
    /// </summary>
    /// <param name="key">Key of the file.</param>
    /// <param name="expiration">How long the URL stays valid.</param>
    /// <returns>Signed download URL.</returns>
    Task<string> GenerateDownloadUrlAsync(string key, TimeSpan expiration);

    /// <summary>
    /// Deletes a file from the storage.
    /// </summary>
    Task DeleteFileAsync(string key);
}
