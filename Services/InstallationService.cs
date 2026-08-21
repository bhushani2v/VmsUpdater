using System.Diagnostics;
using VmsUpdater.Models;

namespace VmsUpdater.Services;

public class InstallationService
{
    private readonly StatusReporter _reporter;

    public InstallationService(StatusReporter reporter)
    {
        _reporter = reporter;
    }

    public (int ExitCode, List<ComponentResult> Results) Execute(string workingDirectory, UpdateComponent[] components)
    {
        var results = components.Select(c => new ComponentResult { Name = c.Name, Status = "pending" }).ToList();

        // Phase 0: Validate all required files exist BEFORE touching anything
        var validationErrors = ValidateRequiredFiles(workingDirectory, components);
        if (validationErrors.Count > 0)
        {
            var missingDetails = string.Join("; ", validationErrors);

            _reporter.Push(new StatusUpdate
            {
                Phase = "install",
                Message = $"Validation failed — missing packages: {missingDetails}"
            });

            // Mark the components with missing files as failed, rest as skipped
            foreach (var r in results)
            {
                var error = validationErrors.FirstOrDefault(e => e.StartsWith(r.Name));
                r.Status = error != null ? "failed" : "skipped";
                r.Error = error;
            }

            return (1, results);
        }

        _reporter.Push(new StatusUpdate
        {
            Phase = "install",
            Message = "All required packages verified."
        });

        var commands = new List<string> { "set -e" };

        // Phase 1: Pre-commands
        foreach (var component in components)
        {
            if (component.PreCommands.Length > 0)
            {
                commands.Add($"echo '[PRE] {component.Name}: stopping dependent services...'");
                commands.AddRange(component.PreCommands);
            }
        }

        // Phase 2: Uninstall old versions
        foreach (var component in components)
        {
            if (component.UninstallCommands.Length > 0)
            {
                commands.Add($"echo '[UNINSTALL] {component.Name}...'");
                commands.AddRange(component.UninstallCommands);
            }
        }

        // Phase 3: Install new versions
        foreach (var component in components)
        {
            commands.Add($"echo '[INSTALL] {component.Name}...'");
            commands.AddRange(component.InstallCommands);
        }

        // Phase 4: Post-commands
        foreach (var component in components)
        {
            if (component.PostCommands.Length > 0)
            {
                commands.Add($"echo '[POST] {component.Name}: restarting dependent services...'");
                commands.AddRange(component.PostCommands);
            }
        }

        // Phase 5: Reload systemd
        commands.Add("echo '[SYSTEMD] Reloading daemon...'");
        commands.Add("sudo systemctl daemon-reload");

        _reporter.Push(new StatusUpdate
        {
            Phase = "install",
            Message = $"Starting update for {components.Length} component(s)..."
        });

        var script = string.Join("\n", commands);
        var exitCode = RunScript(workingDirectory, script);

        var status = exitCode == 0 ? "success" : "failed";
        foreach (var r in results)
            r.Status = status;

        _reporter.Push(new StatusUpdate
        {
            Phase = "install",
            Message = exitCode == 0
                ? "All components updated successfully."
                : $"Installation failed with exit code {exitCode}."
        });

        return (exitCode, results);
    }

    /// <summary>
    /// Checks that all required .deb files exist in the extract directory
    /// before any uninstall/install begins.
    /// Returns a list of error messages (empty = all OK).
    /// </summary>
    private static List<string> ValidateRequiredFiles(string workingDirectory, UpdateComponent[] components)
    {
        var errors = new List<string>();

        foreach (var component in components)
        {
            foreach (var file in component.RequiredFiles)
            {
                var fullPath = Path.Combine(workingDirectory, file);
                if (!File.Exists(fullPath))
                {
                    errors.Add($"{component.Name}: missing '{file}'");
                }
            }
        }

        return errors;
    }

    private static int RunScript(string workingDirectory, string script)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"" + script.Replace("\"", "\\\"") + "\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.Error.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.Error.WriteLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return process.ExitCode;
    }
}
