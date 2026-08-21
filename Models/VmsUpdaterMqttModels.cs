// ============================================================================
// VmsUpdaterMqttModels.cs
//
// Copy this file into the VMS_CLIENT / Management Server project.
// Used to deserialize MQTT messages from topic: vms/updater/status
//
// Every MQTT message is a JSON object with a "type" field:
//   "status" -> UpdateStatusEvent   (progress updates)
//   "result" -> UpdateResultEvent   (final outcome, last message)
//
// Usage:
//   var baseEvent = JsonSerializer.Deserialize<UpdateBaseEvent>(payload);
//   if (baseEvent.Type == "status")
//       var status = JsonSerializer.Deserialize<UpdateStatusEvent>(payload);
//   else if (baseEvent.Type == "result")
//       var result = JsonSerializer.Deserialize<UpdateResultEvent>(payload);
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace VmsUpdater.Models;

/// <summary>
/// Base class — deserialize first to read the "type" field,
/// then deserialize again into the correct derived type.
/// </summary>
public class UpdateBaseEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Convenience method to parse any MQTT payload into the correct event type.
    /// Returns either UpdateStatusEvent or UpdateResultEvent.
    /// </summary>
    public static UpdateBaseEvent Parse(string json)
    {
        var baseEvent = JsonSerializer.Deserialize<UpdateBaseEvent>(json);
        return baseEvent?.Type switch
        {
            "status" => JsonSerializer.Deserialize<UpdateStatusEvent>(json)!,
            "result" => JsonSerializer.Deserialize<UpdateResultEvent>(json)!,
            _ => baseEvent ?? new UpdateBaseEvent()
        };
    }
}

/// <summary>
/// Progress/status update — published during each phase of the update process.
///
/// Phases (in order):
///   "init"          — URL type detected (Google Drive or HTTP)
///   "version_check" — Fetching and comparing version.txt
///   "download"      — Downloading update.zip (has ProgressPercent, BytesDownloaded, TotalBytes)
///   "extract"       — Extracting zip contents
///   "install"       — Running uninstall + install scripts
/// </summary>
public class UpdateStatusEvent : UpdateBaseEvent
{
    /// <summary>
    /// Current phase: "init", "version_check", "download", "extract", "install"
    /// </summary>
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable progress message.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Download progress percentage (0-100). Only set during "download" phase.
    /// </summary>
    [JsonPropertyName("progressPercent")]
    public double? ProgressPercent { get; set; }

    /// <summary>
    /// Bytes downloaded so far. Only set during "download" phase.
    /// </summary>
    [JsonPropertyName("bytesDownloaded")]
    public long? BytesDownloaded { get; set; }

    /// <summary>
    /// Total file size in bytes. Only set during "download" phase.
    /// </summary>
    [JsonPropertyName("totalBytes")]
    public long? TotalBytes { get; set; }
}

/// <summary>
/// Final result — the last message published. Contains the overall outcome.
///
/// Status values:
///   "up_to_date" — No update needed
///   "success"    — Update installed successfully
///   "failed"     — Installation script failed
///   "error"      — An exception occurred (network, parse, etc.)
/// </summary>
public class UpdateResultEvent : UpdateBaseEvent
{
    /// <summary>
    /// Overall status: "up_to_date", "success", "failed", "error"
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    /// <summary>
    /// Human-readable summary of the result.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("localVersion")]
    public float LocalVersion { get; set; }

    [JsonPropertyName("remoteVersion")]
    public float RemoteVersion { get; set; }

    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; set; }

    /// <summary>
    /// Per-component results. Each entry has Name, Status ("success"/"failed"), and optional Error.
    /// </summary>
    [JsonPropertyName("components")]
    public List<UpdateComponentResult> Components { get; set; } = [];

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }
}

public class UpdateComponentResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
