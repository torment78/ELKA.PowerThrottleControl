using System.IO;
using ELKA.PowerThrottleControl.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ELKA.PowerThrottleControl.Services;

public sealed record PowerActionResult(
    IReadOnlyList<bool> Successes,
    bool WasCancelled = false,
    string? ErrorMessage = null);

public sealed class PowerThrottlingService
{
    private const int UacCancelledError = 1223;

    public async Task<PowerActionResult> ApplyAsync(
        IReadOnlyList<ApplicationEntry> applications,
        bool disable)
    {
        var operationDirectory = Path.Combine(
            Path.GetTempPath(),
            "ELKA.PowerThrottleControl",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operationDirectory);

        var scriptPath = Path.Combine(operationDirectory, "apply-power-throttling.cmd");
        var resultPath = Path.Combine(operationDirectory, "results.txt");

        try
        {
            await File.WriteAllTextAsync(scriptPath,
                BuildCommandScript(applications, disable, resultPath),
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
                {
                    return new PowerActionResult([], ErrorMessage: "Windows could not start the elevated command window.");
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
            {
                return new PowerActionResult([], WasCancelled: true);
            }

            await process.WaitForExitAsync();
            var successes = await ReadResultsAsync(resultPath, applications.Count);
            var error = successes.Count < applications.Count || successes.Any(success => !success)
                ? "One or more powercfg commands failed or the elevated window closed before completion."
                : null;
            return new PowerActionResult(successes, ErrorMessage: error);
        }
        finally
        {
            try
            {
                Directory.Delete(operationDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Cleanup is best effort if the command window still owns a temporary file.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best effort if elevation has briefly retained ownership.
            }
        }
    }

    public PowerActionResult OpenAuthoritativeList()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /k \"title Power Throttling List & powercfg /powerthrottling list & echo. & echo Close this window when finished.\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            });
            return new PowerActionResult([]);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
        {
            return new PowerActionResult([], WasCancelled: true);
        }
        catch (Exception ex)
        {
            return new PowerActionResult([], ErrorMessage: ex.Message);
        }
    }

    private static string BuildCommandScript(
        IReadOnlyList<ApplicationEntry> applications,
        bool disable,
        string resultPath)
    {
        var operation = disable ? "disable" : "enable";
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
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

    private static async Task<IReadOnlyList<bool>> ReadResultsAsync(string resultPath, int expectedCount)
    {
        if (!File.Exists(resultPath))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(resultPath);
        return lines.Take(expectedCount).Select(line => line.Trim() == "1").ToList();
    }

    private static string EscapeBatchValue(string value) =>
        value.Replace("^", "^^", StringComparison.Ordinal)
             .Replace("%", "%%", StringComparison.Ordinal);

    private static string EscapeEchoValue(string value) =>
        EscapeBatchValue(value)
            .Replace("&", "^&", StringComparison.Ordinal)
            .Replace("|", "^|", StringComparison.Ordinal)
            .Replace("<", "^<", StringComparison.Ordinal)
            .Replace(">", "^>", StringComparison.Ordinal)
            .Replace("(", "^(", StringComparison.Ordinal)
            .Replace(")", "^)", StringComparison.Ordinal);
}


