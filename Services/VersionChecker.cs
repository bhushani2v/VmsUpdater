using System.Globalization;
using System.Text.Json;

namespace VmsUpdater.Services;

public class VersionChecker
{
    private readonly IFileSource _fileSource;

    public VersionChecker(IFileSource fileSource)
    {
        _fileSource = fileSource;
    }

    public async Task<(bool UpdateAvailable, float RemoteVersion)> CheckAsync(float localVersion)
    {
        var response = await _fileSource.GetFileContentAsync("version.txt");
        var content = response.Trim();

        float remoteVersion;

        try
        {
            using var doc = JsonDocument.Parse(content);
            var versionValue = doc.RootElement.GetProperty("version");

            remoteVersion = versionValue.ValueKind == JsonValueKind.Number
                ? versionValue.GetSingle()
                : float.Parse(versionValue.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        catch (JsonException)
        {
            throw new FormatException(
                $"Invalid version.txt format. Expected JSON: {{\"version\": 1.0}}. Got: '{content}'");
        }

        Console.Error.WriteLine($"Local version:  {localVersion}");
        Console.Error.WriteLine($"Remote version: {remoteVersion}");

        return (remoteVersion > localVersion, remoteVersion);
    }
}
