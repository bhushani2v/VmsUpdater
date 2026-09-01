namespace VmsUpdater.Services;

/// <summary>
/// Fetches files from a local directory or network share (UNC path).
/// Files are expected at {basePath}\version.txt, {basePath}\update.zip, etc.
/// </summary>
public class LocalFileSource : IFileSource
{
    private readonly string _basePath;

    public LocalFileSource(string basePath)
    {
        _basePath = basePath;
    }

    public static bool IsLocalPath(string url)
    {
        return !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> GetFileContentAsync(string fileName)
    {
        return await File.ReadAllTextAsync(Path.Combine(_basePath, fileName));
    }

    public Task<(Stream Stream, long? ContentLength)> GetFileStreamAsync(string fileName)
    {
        return OpenAsync(fileName, fromByte: 0);
    }

    public Task<(Stream Stream, long? TotalFileSize)> GetFileStreamAsync(string fileName, long fromByte)
    {
        return OpenAsync(fileName, fromByte);
    }

    private Task<(Stream Stream, long? TotalSize)> OpenAsync(string fileName, long fromByte)
    {
        var path = Path.Combine(_basePath, fileName);
        var totalSize = new FileInfo(path).Length;

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        if (fromByte > 0)
            stream.Seek(fromByte, SeekOrigin.Begin);

        return Task.FromResult<(Stream, long?)>((stream, totalSize));
    }
}
