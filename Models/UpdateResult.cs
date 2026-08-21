using System.Text.Json.Serialization;

namespace VmsUpdater.Models;

/// <summary>
/// Final result output (last JSON line on stdout).
/// </summary>
public class UpdateResult
{
    [JsonPropertyName("type")]
    public string Type => "result";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("localVersion")]
    public float LocalVersion { get; set; }

    [JsonPropertyName("remoteVersion")]
    public float RemoteVersion { get; set; }

    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; set; }

    [JsonPropertyName("components")]
    public List<ComponentResult> Components { get; set; } = [];

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }
}

public class ComponentResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Intermediate progress update (streamed as NDJSON lines on stdout).
/// </summary>
public class StatusUpdate
{
    [JsonPropertyName("type")]
    public string Type => "status";

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("progressPercent")]
    public double? ProgressPercent { get; set; }

    [JsonPropertyName("bytesDownloaded")]
    public long? BytesDownloaded { get; set; }

    [JsonPropertyName("totalBytes")]
    public long? TotalBytes { get; set; }
}
