using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ELKA.PowerThrottleControl.Models;

namespace ELKA.PowerThrottleControl.Services;

public sealed record PowerActionResult(
    IReadOnlyList<bool> Successes,
    bool WasCancelled = false,
    string? ErrorMessage = null);

public sealed record PowerListResult(
    IReadOnlySet<string> DisabledPaths,
    bool WasCancelled = false,
    string? ErrorMessage = null);

public sealed class PowerThrottlingService
{
    private const int UacCancelledError = 1223;

    public async Task<PowerActionResult> ApplyAsync(IReadOnlyList<ApplicationEntry> applications, bool disable)
    {
        var operationDirectory = CreateOperationDirectory();
        var scriptPath = Path.Combine(operationDirectory, "apply-power-throttling.cmd");
        var resultPath = Path.Combine(operationDirectory, "results.txt");

        try
        {
            await File.WriteAllTextAsync(scriptPath, BuildCommandScript(applications, disable, resultPath), new UTF8Encoding(false));
            var processResult = await RunElevatedAsync(scriptPath);
            if (processResult.WasCancelled) return new PowerActionResult([], WasCancelled: true);
            if (processResult.ErrorMessage is not null) return new PowerActionResult([], ErrorMessage: processResult.ErrorMessage);

            var successes = await ReadResultsAsync(resultPath, applications.Count);
            var error = successes.Count < applications.Count || successes.Any(success => !success)
                ? "One or more powercfg commands failed or the elevated window closed before completion."
                : null;
            return new PowerActionResult(successes, ErrorMessage: error);
        }
        finally { Cleanup(operationDirectory); }
    }

    public async Task<PowerListResult> GetAuthoritativeListAsync(bool keepWindowOpen)
    {
        var operationDirectory = CreateOperationDirectory();
        var scriptPath = Path.Combine(operationDirectory, "list-power-throttling.cmd");
        var outputPath = Path.Combine(operationDirectory, "power-throttling-list.txt");

        try
        {
            await File.WriteAllTextAsync(scriptPath, BuildListScript(outputPath, keepWindowOpen), new UTF8Encoding(false));
            var processResult = await RunElevatedAsync(scriptPath);
            if (processResult.WasCancelled) return new PowerListResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase), WasCancelled: true);
            if (processResult.ErrorMessage is not null) return new PowerListResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase), ErrorMessage: processResult.ErrorMessage);
            if (processResult.ExitCode != 0)
            {
                var details = File.Exists(outputPath) ? (await File.ReadAllTextAsync(outputPath)).Trim() : string.Empty;
                return new PowerListResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    ErrorMessage: string.IsNullOrWhiteSpace(details) ? "Windows could not list power throttling exceptions." : details);
            }

            var output = File.Exists(outputPath) ? await File.ReadAllTextAsync(outputPath) : string.Empty;
            return new PowerListResult(ParseDisabledPaths(output));
        }
        finally { Cleanup(operationDirectory); }
    }

    internal static IReadOnlySet<string> ParseDisabledPaths(string output)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"(?<path>(?:[A-Za-z]:\\|\\\\)[^\""\r\n]+?\.exe)(?:\""|$)", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            try { paths.Add(Path.GetFullPath(match.Groups["path"].Value.Trim())); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return paths;
    }

    private static string BuildListScript(string outputPath, bool keepWindowOpen)
    {
        var path = EscapeBatchValue(outputPath);
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("chcp 65001 >nul");
        builder.AppendLine("setlocal DisableDelayedExpansion");
        builder.AppendLine("title ELKA Power Throttle Control - Authoritative Windows List");
        builder.AppendLine($"powercfg /powerthrottling list >\"{path}\" 2>&1");
        builder.AppendLine("set \"commandExit=%errorlevel%\"");
        builder.AppendLine($"type \"{path}\"");
        builder.AppendLine("echo.");
        builder.AppendLine("echo This is the authoritative Windows power throttling exception list.");
        if (keepWindowOpen)
        {
            builder.AppendLine("echo Press any key to close this administrator window and update ELKA...");
            builder.AppendLine("pause >nul");
        }
        builder.AppendLine("exit /b %commandExit%");
        return builder.ToString();
    }

    private static string BuildCommandScript(IReadOnlyList<ApplicationEntry> applications, bool disable, string resultPath)
    {
        var operation = disable ? "disable" : "enable";
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("chcp 65001 >nul");
        builder.AppendLine("setlocal DisableDelayedExpansion");
        builder.AppendLine("title ELKA Power Throttle Control (Administrator)");
        builder.AppendLine($">\"{EscapeBatchValue(resultPath)}\" type nul");
        builder.AppendLine($"echo Applying: powercfg /powerthrottling {operation}");
        builder.AppendLine("echo.");

        for (var index = 0; index < applications.Count; index++)
        {
            var path = EscapeBatchValue(applications[index].ExecutablePath);
            builder.AppendLine($"echo [{index + 1}/{applications.Count}] {EscapeEchoValue(applications[index].DisplayName)}");
            builder.AppendLine($"powercfg /powerthrottling {operation} /path \"{path}\"");
            builder.AppendLine($"if errorlevel 1 (>>\"{EscapeBatchValue(resultPath)}\" echo 0) else (>>\"{EscapeBatchValue(resultPath)}\" echo 1)");
            builder.AppendLine("echo.");
        }

        builder.AppendLine("echo Finished. Review any errors above.");
        builder.AppendLine("echo Press any key to close this administrator window...");
        builder.AppendLine("pause >nul");
        builder.AppendLine("exit /b 0");
        return builder.ToString();
    }

    private static async Task<(int ExitCode, bool WasCancelled, string? ErrorMessage)> RunElevatedAsync(string scriptPath)
    {
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
            if (!process.Start()) return (-1, false, "Windows could not start the elevated command window.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
        {
            return (-1, true, null);
        }
        catch (Exception ex)
        {
            return (-1, false, ex.Message);
        }
        await process.WaitForExitAsync();
        return (process.ExitCode, false, null);
    }

    private static string CreateOperationDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ElkaSoft", "ELKA.PowerThrottleControl", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Cleanup(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task<IReadOnlyList<bool>> ReadResultsAsync(string resultPath, int expectedCount)
    {
        if (!File.Exists(resultPath)) return [];
        return (await File.ReadAllLinesAsync(resultPath)).Take(expectedCount).Select(line => line.Trim() == "1").ToList();
    }

    private static string EscapeBatchValue(string value) =>
        value.Replace("^", "^^", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);

    private static string EscapeEchoValue(string value) => EscapeBatchValue(value)
        .Replace("&", "^&", StringComparison.Ordinal).Replace("|", "^|", StringComparison.Ordinal)
        .Replace("<", "^<", StringComparison.Ordinal).Replace(">", "^>", StringComparison.Ordinal)
        .Replace("(", "^(", StringComparison.Ordinal).Replace(")", "^)", StringComparison.Ordinal);
}
