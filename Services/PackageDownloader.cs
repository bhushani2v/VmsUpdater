using System.IO.Compression;
using VmsUpdater.Models;

namespace VmsUpdater.Services;

public class PackageDownloader
{
    private const int MaxRetries = 5;
    private static readonly int[] RetryDelaySeconds = [5, 10, 30, 60, 120];

    private readonly IFileSource _fileSource;
    private readonly StatusReporter _reporter;

    public PackageDownloader(IFileSource fileSource, StatusReporter reporter)
    {
        _fileSource = fileSource;
        _reporter = reporter;
    }

    public async Task<string> DownloadAndExtractAsync(float version)
    {
        var extractPath = Path.Combine(Path.GetTempPath(), $"vms-update-{version}");

        if (Directory.Exists(extractPath))
            Directory.Delete(extractPath, recursive: true);

        Directory.CreateDirectory(extractPath);

        var zipPath = Path.Combine(Path.GetTempPath(), $"vms-update-{version}.zip");

        await DownloadWithResumeAsync("update.zip", zipPath);

        _reporter.Push(new StatusUpdate
        {
            Phase = "extract",
            Message = $"Extracting to {extractPath}"
        });

        ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
        File.Delete(zipPath);

        _reporter.Push(new StatusUpdate
        {
            Phase = "extract",
            Message = "Extraction complete."
        });

        return extractPath;
    }

    private async Task DownloadWithResumeAsync(string fileName, string destinationPath)
    {
        _reporter.Push(new StatusUpdate
        {
            Phase = "download",
            Message = "Starting download of update.zip...",
            ProgressPercent = 0
        });

        int attempt = 0;

        while (true)
        {
            long bytesAlreadyDownloaded = 0;

            // Check if partial file exists from a previous attempt
            if (File.Exists(destinationPath))
            {
                bytesAlreadyDownloaded = new FileInfo(destinationPath).Length;
            }

            try
            {
                Stream contentStream;
                long? totalBytes;

                if (bytesAlreadyDownloaded > 0)
                {
                    _reporter.Push(new StatusUpdate
                    {
                        Phase = "download",
                        Message = $"Resuming download from {bytesAlreadyDownloaded / (1024 * 1024)}MB...",
                        BytesDownloaded = bytesAlreadyDownloaded
                    });

                    (contentStream, totalBytes) = await _fileSource.GetFileStreamAsync(fileName, bytesAlreadyDownloaded);
                }
                else
                {
                    (contentStream, totalBytes) = await _fileSource.GetFileStreamAsync(fileName);
                }

                await using (contentStream)
                {
                    // Append if resuming, create if fresh
                    var fileMode = bytesAlreadyDownloaded > 0 ? FileMode.Append : FileMode.Create;
                    await using var fileStream = new FileStream(destinationPath, fileMode, FileAccess.Write, FileShare.None);

                    var buffer = new byte[81920];
                    long totalRead = bytesAlreadyDownloaded;
                    int bytesRead;
                    int lastReportedPercent = -1;

                    while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                        totalRead += bytesRead;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            var percent = (int)((double)totalRead / totalBytes.Value * 100);
                            if (percent / 5 != lastReportedPercent / 5)
                            {
                                lastReportedPercent = percent;
                                _reporter.Push(new StatusUpdate
                                {
                                    Phase = "download",
                                    Message = $"Downloading: {percent}%",
                                    ProgressPercent = percent,
                                    BytesDownloaded = totalRead,
                                    TotalBytes = totalBytes.Value
                                });
                            }
                        }
                    }
                }

                // Download completed successfully
                _reporter.Push(new StatusUpdate
                {
                    Phase = "download",
                    Message = "Download complete.",
                    ProgressPercent = 100
                });

                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                attempt++;

                if (attempt >= MaxRetries)
                {
                    _reporter.Push(new StatusUpdate
                    {
                        Phase = "download",
                        Message = $"Download failed after {MaxRetries} attempts: {ex.Message}"
                    });
                    throw;
                }

                var delay = RetryDelaySeconds[Math.Min(attempt - 1, RetryDelaySeconds.Length - 1)];

                _reporter.Push(new StatusUpdate
                {
                    Phase = "download",
                    Message = $"Download interrupted: {ex.Message}. Retrying in {delay}s (attempt {attempt}/{MaxRetries})..."
                });

                await Task.Delay(TimeSpan.FromSeconds(delay));
            }
        }
    }
}
