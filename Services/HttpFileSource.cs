using System.Net.Http.Headers;

namespace VmsUpdater.Services;

/// <summary>
/// Fetches files from a regular HTTP/HTTPS base URL.
/// Files are expected at {baseUrl}/version.txt, {baseUrl}/update.zip, etc.
/// </summary>
public class HttpFileSource : IFileSource
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HttpFileSource(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<string> GetFileContentAsync(string fileName)
    {
        var url = $"{_baseUrl}/{fileName}";
        return await _httpClient.GetStringAsync(url);
    }

    public Task<(Stream Stream, long? ContentLength)> GetFileStreamAsync(string fileName)
    {
        return GetFileStreamInternalAsync($"{_baseUrl}/{fileName}", rangeFrom: null);
    }

    public Task<(Stream Stream, long? TotalFileSize)> GetFileStreamAsync(string fileName, long fromByte)
    {
        return GetFileStreamInternalAsync($"{_baseUrl}/{fileName}", rangeFrom: fromByte);
    }

    private async Task<(Stream Stream, long? TotalSize)> GetFileStreamInternalAsync(string url, long? rangeFrom)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (rangeFrom.HasValue && rangeFrom.Value > 0)
        {
            request.Headers.Range = new RangeHeaderValue(rangeFrom.Value, null);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        // For range requests, total size comes from Content-Range header
        long? totalSize;
        if (response.Content.Headers.ContentRange?.Length.HasValue == true)
        {
            totalSize = response.Content.Headers.ContentRange.Length;
        }
        else
        {
            totalSize = response.Content.Headers.ContentLength.HasValue
                ? response.Content.Headers.ContentLength.Value + (rangeFrom ?? 0)
                : null;
        }

        var stream = await response.Content.ReadAsStreamAsync();
        return (stream, totalSize);
    }
}
