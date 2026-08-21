namespace VmsUpdater.Services;

/// <summary>
/// Abstraction for fetching files from a remote source.
/// Supports both regular HTTP URLs and Google Drive shared folders.
/// </summary>
public interface IFileSource
{
    /// <summary>
    /// Downloads a file's content as a string (used for version.txt).
    /// </summary>
    Task<string> GetFileContentAsync(string fileName);

    /// <summary>
    /// Downloads a file as a stream with content-length info (used for update.zip).
    /// Caller is responsible for disposing the returned stream and response.
    /// </summary>
    Task<(Stream Stream, long? ContentLength)> GetFileStreamAsync(string fileName);

    /// <summary>
    /// Downloads a file starting from a byte offset (for resume after interruption).
    /// Uses HTTP Range header: "bytes={fromByte}-"
    /// Returns the remaining stream and total file size.
    /// </summary>
    Task<(Stream Stream, long? TotalFileSize)> GetFileStreamAsync(string fileName, long fromByte);
}
