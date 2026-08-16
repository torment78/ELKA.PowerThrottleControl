using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using ELKA.PowerThrottleControl.Models;

namespace ELKA.PowerThrottleControl.Services;

public sealed record FirewallActionResult(
    IReadOnlyList<bool> Successes,
    bool WasCancelled = false,
    string? ErrorMessage = null);

public sealed class FirewallService
{
    private const int UacCancelledError = 1223;

    public Task<FirewallActionResult> AllowVbanAsync(
        IReadOnlyList<NetworkApplicationEntry> applications, int port, string profiles) =>
        RunElevatedAsync(applications, BuildVbanCommands, port, profiles, "Allow VBAN UDP");

    public Task<FirewallActionResult> AllowFullAccessAsync(
        IReadOnlyList<NetworkApplicationEntry> applications, string profiles) =>
        RunElevatedAsync(applications, BuildFullAccessCommands, null, profiles, "Allow full application traffic");

    public Task<FirewallActionResult> RemoveElkaRulesAsync(
        IReadOnlyList<NetworkApplicationEntry> applications) =>
        RunElevatedAsync(applications, BuildRemoveCommands, null, "all", "Remove ELKA firewall rules");

    public FirewallActionResult OpenElkaRulesList()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /k \"title ELKA Network Rules & echo ELKA-managed firewall rules: & echo. & netsh advfirewall firewall show rule name=all verbose | findstr /i /c:\"ELKA VBAN\" /c:\"ELKA Full Access\" & echo. & echo Close this window when finished.\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            });
            return new FirewallActionResult([]);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
        {
            return new FirewallActionResult([], WasCancelled: true);
        }
        catch (Exception ex)
        {
            return new FirewallActionResult([], ErrorMessage: ex.Message);
        }
    }

    private static async Task<FirewallActionResult> RunElevatedAsync(
        IReadOnlyList<NetworkApplicationEntry> applications,
        Func<NetworkApplicationEntry, int?, string, IEnumerable<string>> commandFactory,
        int? port,
        string profiles,
        string heading)
    {
        var operationDirectory = Path.Combine(Path.GetTempPath(), "ELKA.PowerThrottleControl", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operationDirectory);
        var scriptPath = Path.Combine(operationDirectory, "firewall-action.cmd");
        var resultPath = Path.Combine(operationDirectory, "results.txt");

        try
        {
            await File.WriteAllTextAsync(scriptPath,
                BuildScript(applications, commandFactory, port, profiles, heading, resultPath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/d /c \"\"{scriptPath}\"\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal
                }
            };

            try
            {
                if (!process.Start())
                    return new FirewallActionResult([], ErrorMessage: "Windows could not start the elevated command window.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
            {
                return new FirewallActionResult([], WasCancelled: true);
            }

            await process.WaitForExitAsync();
            var successes = await ReadResultsAsync(resultPath, applications.Count);
            var error = successes.Count < applications.Count || successes.Any(success => !success)
                ? "One or more firewall commands failed or the elevated window closed before completion."
                : null;
            return new FirewallActionResult(successes, ErrorMessage: error);
        }
        finally
        {
            try { Directory.Delete(operationDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string BuildScript(
        IReadOnlyList<NetworkApplicationEntry> applications,
        Func<NetworkApplicationEntry, int?, string, IEnumerable<string>> commandFactory,
        int? port, string profiles, string heading, string resultPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("setlocal DisableDelayedExpansion");
        builder.AppendLine("title ELKA Power Throttle Control - Network (Administrator)");
        builder.AppendLine($">\"{EscapeBatchValue(resultPath)}\" type nul");
        builder.AppendLine($"echo {EscapeEchoValue(heading)}");
        builder.AppendLine($"echo Firewall profiles: {EscapeEchoValue(profiles)}");
        builder.AppendLine("echo.");

        for (var index = 0; index < applications.Count; index++)
        {
            var app = applications[index];
            builder.AppendLine("set \"appok=1\"");
            builder.AppendLine($"echo [{index + 1}/{applications.Count}] {EscapeEchoValue(app.DisplayName)}");
            foreach (var command in commandFactory(app, port, profiles))
            {
                builder.AppendLine(command);
                builder.AppendLine("if errorlevel 1 set \"appok=0\"");
            }
            builder.AppendLine($"if \"%appok%\"==\"1\" (>>\"{EscapeBatchValue(resultPath)}\" echo 1) else (>>\"{EscapeBatchValue(resultPath)}\" echo 0)");
            builder.AppendLine("echo.");
        }

        builder.AppendLine("echo Finished. Review any errors above.");
        builder.AppendLine("echo Press any key to close this administrator window...");
        builder.AppendLine("pause >nul");
        builder.AppendLine("exit /b 0");
        return builder.ToString();
    }

    private static IEnumerable<string> BuildVbanCommands(NetworkApplicationEntry app, int? port, string profiles)
    {
        var baseName = $"ELKA VBAN - {Path.GetFileName(app.ExecutablePath)}";
        var path = EscapeBatchValue(app.ExecutablePath);
        var profileValue = EscapeBatchValue(profiles);
        var portValue = port ?? 6980;
        yield return DeleteRuleCommand(baseName + " - In");
        yield return $"netsh advfirewall firewall add rule name=\"{baseName} - In\" dir=in action=allow program=\"{path}\" protocol=UDP localport={portValue} profile={profileValue} enable=yes";
        yield return DeleteRuleCommand(baseName + " - Out");
        yield return $"netsh advfirewall firewall add rule name=\"{baseName} - Out\" dir=out action=allow program=\"{path}\" protocol=UDP remoteport={portValue} profile={profileValue} enable=yes";
    }

    private static IEnumerable<string> BuildFullAccessCommands(NetworkApplicationEntry app, int? _, string profiles)
    {
        var baseName = $"ELKA Full Access - {Path.GetFileName(app.ExecutablePath)}";
        var path = EscapeBatchValue(app.ExecutablePath);
        var profileValue = EscapeBatchValue(profiles);
        yield return DeleteRuleCommand(baseName + " - In");
        yield return $"netsh advfirewall firewall add rule name=\"{baseName} - In\" dir=in action=allow program=\"{path}\" protocol=any profile={profileValue} enable=yes";
        yield return DeleteRuleCommand(baseName + " - Out");
        yield return $"netsh advfirewall firewall add rule name=\"{baseName} - Out\" dir=out action=allow program=\"{path}\" protocol=any profile={profileValue} enable=yes";
    }

    private static IEnumerable<string> BuildRemoveCommands(NetworkApplicationEntry app, int? _, string __)
    {
        var file = Path.GetFileName(app.ExecutablePath);
        foreach (var prefix in new[] { "ELKA VBAN", "ELKA Full Access" })
        foreach (var direction in new[] { "In", "Out" })
            yield return DeleteRuleCommand($"{prefix} - {file} - {direction}", ignoreMissing: true);
    }

    private static string DeleteRuleCommand(string name, bool ignoreMissing = false) =>
        ignoreMissing
            ? $"netsh advfirewall firewall delete rule name=\"{name}\" >nul 2>&1 & ver >nul"
            : $"netsh advfirewall firewall delete rule name=\"{name}\" >nul 2>&1 & ver >nul";

    private static async Task<IReadOnlyList<bool>> ReadResultsAsync(string path, int count)
    {
        if (!File.Exists(path)) return [];
        return (await File.ReadAllLinesAsync(path)).Take(count).Select(line => line.Trim() == "1").ToList();
    }

    private static string EscapeBatchValue(string value) =>
        value.Replace("^", "^^", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);

    private static string EscapeEchoValue(string value) => EscapeBatchValue(value)
        .Replace("&", "^&", StringComparison.Ordinal).Replace("|", "^|", StringComparison.Ordinal)
        .Replace("<", "^<", StringComparison.Ordinal).Replace(">", "^>", StringComparison.Ordinal)
        .Replace("(", "^(", StringComparison.Ordinal).Replace(")", "^)", StringComparison.Ordinal);
}
