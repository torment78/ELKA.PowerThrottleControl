using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ELKA.PowerThrottleControl.Services;

internal static class UpdateLauncher
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A3D6B7E9-4A78-4C46-91D7-8EF52F76D2F1}_is1";

    public static bool IsInstalledCopy()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(UninstallKey);
            return MatchesInstalledPath(Environment.ProcessPath, key?.GetValue("InstallLocation") as string);
        }
        catch (System.Security.SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    internal static bool MatchesInstalledPath(string? executable, string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(installDirectory)) return false;
        try
        {
            return Path.GetFullPath(executable).Equals(Path.GetFullPath(Path.Combine(installDirectory, "ELKA.PowerThrottleControl.exe")), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (IOException) { return false; }
    }

    public static void ShowDownload(string path) => Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
        Arguments = $"/select,\"{path}\"",
        UseShellExecute = true
    });

    public static async Task PrepareInstallerAsync(DownloadedUpdate download, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(download.Path)!;
        var helperPath = Path.Combine(directory, "UpdateRunner.ps1");
        var readyPath = Path.Combine(directory, "ready.txt");
        await using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ELKA.PowerThrottleControl.UpdateRunner.ps1")
            ?? throw new InvalidOperationException("The update helper is missing."))
        await using (var output = File.Create(helperPath))
        {
            await resource.CopyToAsync(output, cancellationToken);
        }

        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helperPath,
            "-ParentProcessId", Environment.ProcessId.ToString(), "-InstallerPath", download.Path,
            "-ExpectedHash", download.Sha256, "-AppExecutable", Environment.ProcessPath! })
            start.ArgumentList.Add(argument);

        using var helper = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(readyPath) && (await File.ReadAllTextAsync(readyPath, cancellationToken)).Trim() == "ready")
                    return;
                if (helper.HasExited) throw new IOException("The update helper could not start. The application will stay open.");
                await Task.Delay(100, cancellationToken);
            }
            throw new TimeoutException("The update helper did not respond. The application will stay open.");
        }
        catch
        {
            if (!helper.HasExited) helper.Kill();
            throw;
        }
    }
}
