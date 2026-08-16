using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ELKA.PowerThrottleControl.Models;

namespace ELKA.PowerThrottleControl.Services;

public sealed class GeneralFirewallService
{
    private const int UacCancelledError = 1223;

    public Task<FirewallActionResult> AllowPortsAsync(
        IReadOnlyList<ApplicationEntry> applications,
        string ports,
        IReadOnlyList<string> protocols,
        bool inbound,
        bool outbound,
        string profiles) => RunElevatedAsync(applications, app =>
            BuildPortCommands(app, ports, protocols, inbound, outbound, profiles),
            $"Allow {string.Join(" and ", protocols)} ports {ports}", profiles);

    public Task<FirewallActionResult> AllowAllTrafficAsync(
        IReadOnlyList<ApplicationEntry> applications,
        bool inbound,
        bool outbound,
        string profiles) => RunElevatedAsync(applications,
            app => BuildFullAccessCommands(app, inbound, outbound, profiles),
            "Allow all selected application traffic", profiles);

    public Task<FirewallActionResult> RemoveRulesAsync(IReadOnlyList<ApplicationEntry> applications) =>
        RunElevatedAsync(applications, BuildRemoveCommands, "Remove general ELKA Network rules", "all");

    public FirewallActionResult OpenRulesList()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /k \"title ELKA General Network Rules & echo General ELKA Network firewall rules: & echo. & netsh advfirewall firewall show rule name=all verbose | findstr /i /c:\"ELKA Network\" & echo. & echo Close this window when finished.\"",
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

    public static bool TryNormalizePorts(string input, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var separated = input.Trim()
            .Replace(';', ',')
            .Replace(' ', ',');
        var tokens = separated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = "Enter at least one port.";
            return false;
        }

        var valid = new List<string>();
        foreach (var token in tokens)
        {
            var range = token.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 1 && TryPort(range[0], out var port))
            {
                valid.Add(port.ToString());
                continue;
            }

            if (range.Length == 2 && TryPort(range[0], out var first) && TryPort(range[1], out var last) && first <= last)
            {
                valid.Add($"{first}-{last}");
                continue;
            }

            error = $"'{token}' is not a valid port or ascending port range. Use values from 1 through 65535.";
            return false;
        }

        normalized = string.Join(',', valid.Distinct(StringComparer.Ordinal));
        return true;
    }

    private static bool TryPort(string text, out int port) =>
        int.TryParse(text, out port) && port is >= 1 and <= 65535;

    private static IEnumerable<string> BuildPortCommands(
        ApplicationEntry app, string ports, IReadOnlyList<string> protocols,
        bool inbound, bool outbound, string profiles)
    {
        var path = EscapeBatchValue(app.ExecutablePath);
        var identity = RuleIdentity(app);
        foreach (var protocol in protocols)
        {
            if (inbound)
            {
                var name = $"ELKA Network - {identity} - {protocol} - In";
                yield return DeleteRule(name);
                yield return $"netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow program=\"{path}\" protocol={protocol} localport={ports} profile={profiles} enable=yes";
            }
            if (outbound)
            {
                var name = $"ELKA Network - {identity} - {protocol} - Out";
                yield return DeleteRule(name);
                yield return $"netsh advfirewall firewall add rule name=\"{name}\" dir=out action=allow program=\"{path}\" protocol={protocol} remoteport={ports} profile={profiles} enable=yes";
            }
        }
    }

    private static IEnumerable<string> BuildFullAccessCommands(
        ApplicationEntry app, bool inbound, bool outbound, string profiles)
    {
        var path = EscapeBatchValue(app.ExecutablePath);
        var identity = RuleIdentity(app);
        if (inbound)
        {
            var name = $"ELKA Network Full - {identity} - In";
            yield return DeleteRule(name);
            yield return $"netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow program=\"{path}\" protocol=any profile={profiles} enable=yes";
        }
        if (outbound)
        {
            var name = $"ELKA Network Full - {identity} - Out";
            yield return DeleteRule(name);
            yield return $"netsh advfirewall firewall add rule name=\"{name}\" dir=out action=allow program=\"{path}\" protocol=any profile={profiles} enable=yes";
        }
    }

    private static IEnumerable<string> BuildRemoveCommands(ApplicationEntry app)
    {
        var identity = RuleIdentity(app);
        foreach (var protocol in new[] { "TCP", "UDP" })
        foreach (var direction in new[] { "In", "Out" })
            yield return DeleteRule($"ELKA Network - {identity} - {protocol} - {direction}");
        foreach (var direction in new[] { "In", "Out" })
            yield return DeleteRule($"ELKA Network Full - {identity} - {direction}");
    }

    private static string RuleIdentity(ApplicationEntry app)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.ExecutablePath.ToUpperInvariant())))[..8];
        return $"{Path.GetFileName(app.ExecutablePath)} - {hash}";
    }

    private static string DeleteRule(string name) =>
        $"netsh advfirewall firewall delete rule name=\"{name}\" >nul 2>&1 & ver >nul";

    private static async Task<FirewallActionResult> RunElevatedAsync(
        IReadOnlyList<ApplicationEntry> applications,
        Func<ApplicationEntry, IEnumerable<string>> commands,
        string heading,
        string profiles)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ELKA.PowerThrottleControl", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "general-firewall-action.cmd");
        var results = Path.Combine(directory, "results.txt");
        try
        {
            await File.WriteAllTextAsync(script, BuildScript(applications, commands, heading, profiles, results),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/d /c \"\"{script}\"\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal
                }
            };
            try
            {
                if (!process.Start()) return new FirewallActionResult([], ErrorMessage: "Windows could not start the elevated command window.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
            {
                return new FirewallActionResult([], WasCancelled: true);
            }
            await process.WaitForExitAsync();
            var values = File.Exists(results)
                ? (await File.ReadAllLinesAsync(results)).Take(applications.Count).Select(line => line.Trim() == "1").ToList()
                : [];
            var error = values.Count < applications.Count || values.Any(value => !value)
                ? "One or more firewall commands failed or the elevated window closed before completion."
                : null;
            return new FirewallActionResult(values, ErrorMessage: error);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string BuildScript(IReadOnlyList<ApplicationEntry> apps,
        Func<ApplicationEntry, IEnumerable<string>> commands, string heading, string profiles, string resultPath)
    {
        var builder = new StringBuilder("@echo off\r\nsetlocal DisableDelayedExpansion\r\n");
        builder.AppendLine("title ELKA Power Throttle Control - General Network (Administrator)");
        builder.AppendLine($">\"{EscapeBatchValue(resultPath)}\" type nul");
        builder.AppendLine($"echo {EscapeEchoValue(heading)}");
        builder.AppendLine($"echo Firewall profiles: {profiles}");
        builder.AppendLine("echo.");
        for (var index = 0; index < apps.Count; index++)
        {
            builder.AppendLine("set \"appok=1\"");
            builder.AppendLine($"echo [{index + 1}/{apps.Count}] {EscapeEchoValue(apps[index].DisplayName)}");
            foreach (var command in commands(apps[index]))
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
        return builder.ToString();
    }

    private static string EscapeBatchValue(string value) =>
        value.Replace("^", "^^", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);

    private static string EscapeEchoValue(string value) => EscapeBatchValue(value)
        .Replace("&", "^&", StringComparison.Ordinal).Replace("|", "^|", StringComparison.Ordinal)
        .Replace("<", "^<", StringComparison.Ordinal).Replace(">", "^>", StringComparison.Ordinal)
        .Replace("(", "^(", StringComparison.Ordinal).Replace(")", "^)", StringComparison.Ordinal);
}
