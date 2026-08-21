using System.Text.Json;
using System.Text.RegularExpressions;

namespace VmsUpdater.Services;

/// <summary>
/// Fetches files from a publicly shared Google Drive folder.
/// The folder must be shared as "Anyone with the link can view".
///
/// How it works:
///   1. Extract folder ID from the Google Drive URL.
///   2. Fetch the folder HTML page — Google embeds file metadata in a JS variable
///      called window['_DRIVE_ivd'] as hex-escaped JSON.
///   3. Parse that JSON to get file IDs and names.
///   4. Download individual files via Google's export endpoint.
/// </summary>
public partial class GoogleDriveFileSource : IFileSource
{
    private readonly HttpClient _httpClient;
    private readonly string _folderId;

    /// <summary>Cache of fileName -> fileId discovered from the folder page.</summary>
    private Dictionary<string, string>? _fileMap;

    public GoogleDriveFileSource(HttpClient httpClient, string folderUrl)
    {
        _httpClient = httpClient;
        _folderId = ExtractFolderId(folderUrl)
            ?? throw new ArgumentException($"Cannot extract Google Drive folder ID from: {folderUrl}");
    }

    public static bool IsGoogleDriveUrl(string url)
    {
        return url.Contains("drive.google.com/drive/folders/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> GetFileContentAsync(string fileName)
    {
        var fileId = await GetFileIdAsync(fileName);
        var downloadUrl = BuildDownloadUrl(fileId);
        return await _httpClient.GetStringAsync(downloadUrl);
    }

    public Task<(Stream Stream, long? ContentLength)> GetFileStreamAsync(string fileName)
    {
        return GetFileStreamInternalAsync(fileName, rangeFrom: null);
    }

    public Task<(Stream Stream, long? TotalFileSize)> GetFileStreamAsync(string fileName, long fromByte)
    {
        return GetFileStreamInternalAsync(fileName, rangeFrom: fromByte);
    }

    private async Task<(Stream Stream, long? TotalSize)> GetFileStreamInternalAsync(string fileName, long? rangeFrom)
    {
        var fileId = await GetFileIdAsync(fileName);
        var downloadUrl = BuildDownloadUrl(fileId);

        var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (rangeFrom.HasValue && rangeFrom.Value > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeFrom.Value, null);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        // For large files, Google may return an HTML virus-scan warning page.
        if (response.Content.Headers.ContentType?.MediaType == "text/html")
        {
            var html = await response.Content.ReadAsStringAsync();
            var confirmMatch = Regex.Match(html, @"href=""(/uc\?export=download[^""]+)""");
            if (confirmMatch.Success)
            {
                var confirmUrl = "https://drive.google.com" + confirmMatch.Groups[1].Value.Replace("&amp;", "&");
                var retryRequest = new HttpRequestMessage(HttpMethod.Get, confirmUrl);
                if (rangeFrom.HasValue && rangeFrom.Value > 0)
                    retryRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeFrom.Value, null);

                response = await _httpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
            }
        }

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

    private async Task<string> GetFileIdAsync(string fileName)
    {
        _fileMap ??= await DiscoverFilesInFolderAsync();

        if (_fileMap.TryGetValue(fileName, out var fileId))
            return fileId;

        var available = _fileMap.Count > 0
            ? string.Join(", ", _fileMap.Keys)
            : "(none found)";

        throw new FileNotFoundException(
            $"File '{fileName}' not found in Google Drive folder. Available files: {available}");
    }

    /// <summary>
    /// Fetches the Google Drive folder page and parses the embedded _DRIVE_ivd variable
    /// to extract file names and IDs.
    ///
    /// The _DRIVE_ivd variable contains hex-escaped JSON. Each file entry is an array where:
    ///   [0] = file ID
    ///   [2] = file name
    ///   [3] = MIME type
    ///   [13] = file size in bytes
    /// </summary>
    private async Task<Dictionary<string, string>> DiscoverFilesInFolderAsync()
    {
        var folderPageUrl = $"https://drive.google.com/drive/folders/{_folderId}";

        var request = new HttpRequestMessage(HttpMethod.Get, folderPageUrl);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // Extract the _DRIVE_ivd variable: window['_DRIVE_ivd'] = '...escaped JSON...';
        var ivdMatch = DriveIvdRegex().Match(html);
        if (!ivdMatch.Success)
        {
            throw new InvalidOperationException(
                "Could not find file listing in Google Drive folder page. " +
                "Make sure the folder is shared as 'Anyone with the link can view'.");
        }

        var escapedJson = ivdMatch.Groups[1].Value;

        // Decode hex escape sequences (\x5b -> [, \x22 -> ", etc.)
        var decodedJson = DecodeHexEscapes(escapedJson);

        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(decodedJson);
            var root = doc.RootElement;

            // root[0] is the array of file entries
            if (root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in root[0].EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 3)
                        continue;

                    var id = entry[0].GetString();
                    var name = entry[2].GetString();

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    {
                        fileMap[name] = id;
                        Console.Error.WriteLine($"  Found: {name} (id: {id})");
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse Google Drive folder file listing: {ex.Message}");
        }

        if (fileMap.Count == 0)
        {
            throw new InvalidOperationException(
                "Google Drive folder appears empty or files could not be parsed.");
        }

        return fileMap;
    }

    private static string BuildDownloadUrl(string fileId)
    {
        return $"https://drive.usercontent.google.com/download?id={fileId}&export=download&confirm=t";
    }

    private static string? ExtractFolderId(string url)
    {
        var match = Regex.Match(url, @"drive\.google\.com/drive/folders/([a-zA-Z0-9_-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Decodes JavaScript escape sequences in the _DRIVE_ivd string:
    ///   \xNN  -> character
    ///   \uNNNN -> character
    ///   \'    -> '
    ///   \/    -> /
    ///   \=    -> =   (Google escapes = in URLs)
    ///   \X    -> X   (any other stray backslash-escaped char)
    /// </summary>
    private static string DecodeHexEscapes(string input)
    {
        // Replace \xNN sequences
        var result = HexEscapeRegex().Replace(input, match =>
        {
            var hexValue = Convert.ToByte(match.Groups[1].Value, 16);
            return ((char)hexValue).ToString();
        });

        // Replace \uNNNN sequences
        result = UnicodeEscapeRegex().Replace(result, match =>
        {
            var hexValue = Convert.ToInt32(match.Groups[1].Value, 16);
            return ((char)hexValue).ToString();
        });

        // Replace escaped single quotes
        result = result.Replace("\\'", "'");

        // Remove stray backslashes before chars that aren't valid JSON escape targets
        // Valid JSON escapes: \" \\ \/ \b \f \n \r \t \u
        result = StrayBackslashRegex().Replace(result, "$1");

        return result;
    }

    [GeneratedRegex(@"window\['_DRIVE_ivd'\]\s*=\s*'(.*?)';", RegexOptions.Singleline)]
    private static partial Regex DriveIvdRegex();

    [GeneratedRegex(@"\\x([0-9a-fA-F]{2})")]
    private static partial Regex HexEscapeRegex();

    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})")]
    private static partial Regex UnicodeEscapeRegex();

    /// <summary>
    /// Matches a backslash followed by a character that is NOT a valid JSON escape target.
    /// Valid JSON escapes: " \ / b f n r t u — anything else is a stray backslash.
    /// </summary>
    [GeneratedRegex(@"\\([^""\\\/bfnrtu])")]
    private static partial Regex StrayBackslashRegex();
}
