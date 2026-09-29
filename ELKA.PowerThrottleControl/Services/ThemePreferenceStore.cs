using System.IO;

namespace ELKA.PowerThrottleControl.Services;

public enum ThemePreference
{
    Light,
    Dark,
    System
}

// Keep user data separate from the elevated, machine-wide installation.
internal sealed class ThemePreferenceStore(string localApplicationData)
{
    private readonly string _preferencePath = Path.Combine(
        localApplicationData, "ElkaSoft", "ELKA.PowerThrottleControl", "theme.txt");

    private readonly string _legacyPreferencePath = Path.Combine(
        localApplicationData, "ELKA.PowerThrottleControl", "theme.txt");

    public ThemePreference LoadPreference()
    {
        if (TryReadPreference(_preferencePath, out var preference))
        {
            return preference;
        }

        if (TryReadPreference(_legacyPreferencePath, out preference))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_preferencePath)!);
                // Copy once without replacing newer settings or removing the backup.
                File.Copy(_legacyPreferencePath, _preferencePath, overwrite: false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            // A read-only destination must not prevent using the existing preference.
            return preference;
        }

        return ThemePreference.Dark;
    }

    public void SavePreference(ThemePreference preference)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferencePath)!);
            File.WriteAllText(_preferencePath, preference.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        // Theme selection still applies for this run if storage is unavailable.
    }

    private static bool TryReadPreference(string path, out ThemePreference preference)
    {
        preference = ThemePreference.Dark;
        try
        {
            return File.Exists(path)
                && Enum.TryParse(File.ReadAllText(path).Trim(), ignoreCase: true, out preference)
                && Enum.IsDefined(preference);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
