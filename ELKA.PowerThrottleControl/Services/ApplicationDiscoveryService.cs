using System.IO;
using Microsoft.Win32;
using ELKA.PowerThrottleControl.Models;
using System.Runtime.InteropServices;

namespace ELKA.PowerThrottleControl.Services;

public sealed class ApplicationDiscoveryService
{
    private static readonly string[] UninstallLocations =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private const string AppPathsLocation = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    public IReadOnlyList<ApplicationEntry> Discover()
    {
        var applications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ReadUninstallEntries(RegistryHive.LocalMachine, RegistryView.Registry64, applications);
        ReadUninstallEntries(RegistryHive.LocalMachine, RegistryView.Registry32, applications);
        ReadUninstallEntries(RegistryHive.CurrentUser, RegistryView.Registry64, applications);
        ReadUninstallEntries(RegistryHive.CurrentUser, RegistryView.Registry32, applications);
        ReadAppPaths(RegistryHive.LocalMachine, RegistryView.Registry64, applications);
        ReadAppPaths(RegistryHive.LocalMachine, RegistryView.Registry32, applications);
        ReadAppPaths(RegistryHive.CurrentUser, RegistryView.Registry64, applications);
        ReadAppPaths(RegistryHive.CurrentUser, RegistryView.Registry32, applications);
        ReadStartMenuShortcuts(applications);

        return applications
            .Select(pair => new ApplicationEntry { ExecutablePath = pair.Key, DisplayName = pair.Value })
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ReadUninstallEntries(
        RegistryHive hive,
        RegistryView view,
        IDictionary<string, string> applications)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            foreach (var location in UninstallLocations.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var uninstallKey = baseKey.OpenSubKey(location);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using var entry = uninstallKey.OpenSubKey(subKeyName);
                        if (entry is null || Convert.ToInt32(entry.GetValue("SystemComponent", 0)) == 1)
                        {
                            continue;
                        }

                        var displayName = entry.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            continue;
                        }

                        var executable = ParseDisplayIcon(entry.GetValue("DisplayIcon") as string)
                                         ?? FindExecutableInInstallLocation(
                                             entry.GetValue("InstallLocation") as string,
                                             displayName);
                        AddIfUsable(applications, executable, displayName);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                    {
                        // Continue searching other installed-app entries.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A registry view may be unavailable or restricted; other views can still be searched.
        }
    }

    private static void ReadAppPaths(
        RegistryHive hive,
        RegistryView view,
        IDictionary<string, string> applications)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appPaths = baseKey.OpenSubKey(AppPathsLocation);
            if (appPaths is null)
            {
                return;
            }

            foreach (var subKeyName in appPaths.GetSubKeyNames())
            {
                using var entry = appPaths.OpenSubKey(subKeyName);
                var executable = entry?.GetValue(null) as string;
                var displayName = Path.GetFileNameWithoutExtension(subKeyName);
                AddIfUsable(applications, executable, displayName);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Continue with the remaining discovery sources.
        }
    }

    private static void ReadStartMenuShortcuts(IDictionary<string, string> applications)
    {
        var startMenuRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            foreach (var root in startMenuRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IEnumerable<string> shortcuts;
                try
                {
                    shortcuts = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToArray();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                foreach (var shortcutPath in shortcuts)
                {
                    object? shortcut = null;
                    try
                    {
                        shortcut = shellType.InvokeMember("CreateShortcut",
                            System.Reflection.BindingFlags.InvokeMethod, null, shell, [shortcutPath]);
                        var target = shortcut?.GetType().InvokeMember("TargetPath",
                            System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
                        AddIfUsable(applications, target, Path.GetFileNameWithoutExtension(shortcutPath));
                    }
                    catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException)
                    {
                        // Ignore broken or inaccessible shortcuts.
                    }
                    finally
                    {
                        if (shortcut is not null && Marshal.IsComObject(shortcut))
                        {
                            Marshal.FinalReleaseComObject(shortcut);
                        }
                    }
                }
            }
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string? ParseDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(displayIcon.Trim());
        if (expanded.StartsWith('"'))
        {
            var closingQuote = expanded.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                return expanded[1..closingQuote];
            }
        }

        var commaIndex = expanded.LastIndexOf(',');
        if (commaIndex > 0 && int.TryParse(expanded[(commaIndex + 1)..], out _))
        {
            expanded = expanded[..commaIndex];
        }

        return expanded.Trim().Trim('"');
    }

    private static string? FindExecutableInInstallLocation(string? installLocation, string displayName)
    {
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return null;
        }

        var directory = Environment.ExpandEnvironmentVariables(installLocation.Trim().Trim('"'));
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var candidates = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(path => !IsInstallerOrUninstaller(path))
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            var normalizedName = NormalizeName(displayName);
            return candidates.FirstOrDefault(path => NormalizeName(Path.GetFileNameWithoutExtension(path)) == normalizedName)
                   ?? (candidates.Count == 1 ? candidates[0] : null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static void AddIfUsable(IDictionary<string, string> applications, string? candidate, string displayName)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var expanded = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (!fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath)
            || IsInstallerOrUninstaller(fullPath))
        {
            return;
        }

        var cleanName = displayName.Trim();
        if (!applications.TryGetValue(fullPath, out var existingName)
            || cleanName.Length > existingName.Length)
        {
            applications[fullPath] = cleanName;
        }
    }

    private static bool IsInstallerOrUninstaller(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
               || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
               || name.Equals("setup", StringComparison.OrdinalIgnoreCase)
               || name.Equals("installer", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}


