using System.Globalization;
using VmsUpdater.Models;
using VmsUpdater.Services;

await using var reporter = new StatusReporter();

string? url = null;
float localVersion = 0;
string? componentsArg = null;
string mqttHost = "localhost";
int mqttPort = 1883;

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--url":
            url = args[++i];
            break;
        case "--version":
            if (!float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out localVersion))
            {
                reporter.PushResult(new UpdateResult { Status = "error", Message = "Invalid version number.", ExitCode = 1 });
                return 1;
            }
            break;
        case "--components":
            componentsArg = args[++i];
            break;
        case "--mqtt-host":
            mqttHost = args[++i];
            break;
        case "--mqtt-port":
            if (!int.TryParse(args[++i], out mqttPort))
            {
                reporter.PushResult(new UpdateResult { Status = "error", Message = "Invalid MQTT port number.", ExitCode = 1 });
                return 1;
            }
            break;
    }
}

// Connect to MQTT broker
await reporter.ConnectAsync(mqttHost, mqttPort);

if (string.IsNullOrWhiteSpace(url))
{
    reporter.PushResult(new UpdateResult
    {
        Status = "error",
        Message = "Usage: VmsUpdater --url <url> --version <ver> --components <list|all> [--mqtt-host <host>] [--mqtt-port <port>]",
        ExitCode = 1
    });
    return 1;
}

if (string.IsNullOrWhiteSpace(componentsArg))
{
    reporter.PushResult(new UpdateResult
    {
        Status = "error",
        Message = "--components is required. Valid values: ms,rs,mysql,apache,client,configmgr,mosquitto,failover,netstatus,samba,all",
        ExitCode = 1
    });
    return 1;
}

var selectedComponents = ParseComponents(componentsArg);
if (selectedComponents.Length == 0)
{
    reporter.PushResult(new UpdateResult
    {
        Status = "error",
        Message = $"No valid components found in: '{componentsArg}'.",
        ExitCode = 1
    });
    return 1;
}

// Auto-detect URL type and create the appropriate file source
using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

IFileSource fileSource;
if (GoogleDriveFileSource.IsGoogleDriveUrl(url))
{
    reporter.Push(new StatusUpdate { Phase = "init", Message = "Detected Google Drive folder URL." });
    fileSource = new GoogleDriveFileSource(httpClient, url);
}
else
{
    reporter.Push(new StatusUpdate { Phase = "init", Message = "Using direct HTTP file source." });
    fileSource = new HttpFileSource(httpClient, url);
}

var versionChecker = new VersionChecker(fileSource);
var downloader = new PackageDownloader(fileSource, reporter);
var installer = new InstallationService(reporter);

try
{
    reporter.Push(new StatusUpdate { Phase = "version_check", Message = "Checking for updates..." });

    var (updateAvailable, remoteVersion) = await versionChecker.CheckAsync(localVersion);

    if (!updateAvailable)
    {
        reporter.PushResult(new UpdateResult
        {
            Status = "up_to_date",
            Message = "System is up to date.",
            LocalVersion = localVersion,
            RemoteVersion = remoteVersion,
            UpdateAvailable = false,
            ExitCode = 0
        });
        return 0;
    }

    reporter.Push(new StatusUpdate
    {
        Phase = "version_check",
        Message = $"Update available: {localVersion} -> {remoteVersion}"
    });

    var extractPath = await downloader.DownloadAndExtractAsync(remoteVersion);

    var (exitCode, componentResults) = installer.Execute(extractPath, selectedComponents);

    reporter.PushResult(new UpdateResult
    {
        Status = exitCode == 0 ? "success" : "failed",
        Message = exitCode == 0 ? "Update completed successfully." : "Update failed. Check logs for details.",
        LocalVersion = localVersion,
        RemoteVersion = remoteVersion,
        UpdateAvailable = true,
        Components = componentResults,
        ExitCode = exitCode
    });

    return exitCode;
}
catch (HttpRequestException ex)
{
    reporter.PushResult(new UpdateResult { Status = "error", Message = $"Network error: {ex.Message}", LocalVersion = localVersion, ExitCode = 1 });
    return 1;
}
catch (Exception ex)
{
    reporter.PushResult(new UpdateResult { Status = "error", Message = ex.Message, LocalVersion = localVersion, ExitCode = 1 });
    return 1;
}

static UpdateComponent[] ParseComponents(string input)
{
    if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
        return UpdateComponent.All;

    var keys = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var selected = new List<UpdateComponent>();

    foreach (var key in keys)
    {
        if (UpdateComponent.ComponentMap.TryGetValue(key, out var component))
            selected.Add(component);
        else
            Console.Error.WriteLine($"Warning: Unknown component '{key}', skipping.");
    }

    return selected.ToArray();
}
