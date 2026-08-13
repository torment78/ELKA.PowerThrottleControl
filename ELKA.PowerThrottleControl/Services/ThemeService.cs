using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ELKA.PowerThrottleControl.Services;

public enum ThemePreference
{
    Light,
    Dark,
    System
}

public static class ThemeService
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ELKA.PowerThrottleControl",
        "theme.txt");

    private static readonly IReadOnlyDictionary<string, string> DarkColors =
        new Dictionary<string, string>
        {
            ["WindowBackgroundBrush"] = "#11151B",
            ["SurfaceBrush"] = "#181E27",
            ["SurfaceAltBrush"] = "#202834",
            ["HeaderBrush"] = "#212A36",
            ["InputBrush"] = "#202834",
            ["PrimaryTextBrush"] = "#F1F5F9",
            ["SecondaryTextBrush"] = "#AAB4C2",
            ["BorderBrush"] = "#303B49",
            ["GridLineBrush"] = "#2B3542",
            ["RedRowBrush"] = "#3B2025",
            ["GreenRowBrush"] = "#17382C",
            ["SelectionBrush"] = "#4C82D4",
            ["ScrollTrackBrush"] = "#151B23",
            ["ScrollThumbBrush"] = "#465467",
            ["ScrollThumbHoverBrush"] = "#64748A"
        };

    private static readonly IReadOnlyDictionary<string, string> LightColors =
        new Dictionary<string, string>
        {
            ["WindowBackgroundBrush"] = "#F4F6F9",
            ["SurfaceBrush"] = "#FFFFFF",
            ["SurfaceAltBrush"] = "#EEF1F5",
            ["HeaderBrush"] = "#E6EAF0",
            ["InputBrush"] = "#FFFFFF",
            ["PrimaryTextBrush"] = "#20242A",
            ["SecondaryTextBrush"] = "#5D6673",
            ["BorderBrush"] = "#C8D0DA",
            ["GridLineBrush"] = "#DDE2E8",
            ["RedRowBrush"] = "#F3D5D9",
            ["GreenRowBrush"] = "#D9EFDF",
            ["SelectionBrush"] = "#2D68C4",
            ["ScrollTrackBrush"] = "#E5E9EF",
            ["ScrollThumbBrush"] = "#9AA7B7",
            ["ScrollThumbHoverBrush"] = "#738398"
        };

    public static ThemePreference LoadPreference()
    {
        try
        {
            if (File.Exists(PreferencePath)
                && Enum.TryParse(File.ReadAllText(PreferencePath).Trim(), ignoreCase: true, out ThemePreference preference))
            {
                return preference;
            }
        }
        catch (IOException)
        {
            // Fall back to the preferred default if the setting cannot be read.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall back to the preferred default if the setting cannot be read.
        }

        return ThemePreference.Dark;
    }

    public static void SavePreference(ThemePreference preference)
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferencePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(PreferencePath, preference.ToString());
        }
        catch (IOException)
        {
            // Theme selection still applies for this run.
        }
        catch (UnauthorizedAccessException)
        {
            // Theme selection still applies for this run.
        }
    }

    public static bool Apply(ResourceDictionary resources, ThemePreference preference)
    {
        var useDarkTheme = preference == ThemePreference.Dark
                           || preference == ThemePreference.System && IsWindowsDarkTheme();
        var palette = useDarkTheme ? DarkColors : LightColors;

        foreach (var (key, colorValue) in palette)
        {
            resources[key] = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(colorValue));
        }

        return useDarkTheme;
    }

    private static bool IsWindowsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme
                   && appsUseLightTheme == 0;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }
}


