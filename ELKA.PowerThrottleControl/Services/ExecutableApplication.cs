using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ELKA.PowerThrottleControl.Models;

namespace ELKA.PowerThrottleControl.Services;

internal static class ExecutableApplication
{
    public static string ResolvePath(string? input)
    {
        var path = input?.Trim() ?? "";
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        path = Environment.ExpandEnvironmentVariables(path);
        if (string.IsNullOrWhiteSpace(path) || path.Contains('"') || path.Any(char.IsControl)
            || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Enter the full path to an application EXE, without command-line arguments, or choose Browse.");

        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose the application's .exe file, not a shortcut or folder.");
        if (!File.Exists(path))
            throw new FileNotFoundException("The EXE could not be found. Check the path and make sure its drive is connected.", path);
        return path;
    }

    public static ApplicationEntry Create(string input)
    {
        var path = ResolvePath(input);
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                name = new string(description.Where(character => !char.IsControl(character)).ToArray()).Trim();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return new ApplicationEntry { ExecutablePath = path, DisplayName = name };
    }

    public static ApplicationEntry? TryCreate(string path)
    {
        try { return Create(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public static IReadOnlyList<ApplicationEntry> Merge(IEnumerable<ApplicationEntry> discovered, IEnumerable<ApplicationEntry> remembered) =>
        discovered.Concat(remembered).DistinctBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase).ToList();
}
