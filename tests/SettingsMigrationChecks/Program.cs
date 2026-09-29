using ELKA.PowerThrottleControl.Services;

// Exercise migration with isolated fixtures, never the current user's settings.
var testRoot = Directory.CreateTempSubdirectory("ElkaPowerThrottle-SettingsChecks-").FullName;
try
{
    var fresh = Path.Combine(testRoot, "fresh");
    var freshStore = new ThemePreferenceStore(fresh);
    Check(freshStore.LoadPreference() == ThemePreference.Dark, "A fresh user gets the dark default");
    Check(!Directory.Exists(fresh), "Loading a fresh profile does not create files");

    foreach (var preference in Enum.GetValues<ThemePreference>())
    {
        freshStore.SavePreference(preference);
        Check(File.ReadAllText(NewPath(fresh)) == preference.ToString()
            && new ThemePreferenceStore(fresh).LoadPreference() == preference,
            $"{preference} saves and reloads under ElkaSoft");
    }
    Check(!File.Exists(OldPath(fresh)), "New saves never create legacy files");

    var migrated = Path.Combine(testRoot, "migrated");
    WritePreference(OldPath(migrated), " System \r\n");
    var migrationStore = new ThemePreferenceStore(migrated);
    Check(migrationStore.LoadPreference() == ThemePreference.System, "The old preference loads during migration");
    Check(File.ReadAllText(NewPath(migrated)) == " System \r\n"
        && File.ReadAllText(OldPath(migrated)) == " System \r\n",
        "Migration copies the preference and retains the original byte-for-byte");
    migrationStore.SavePreference(ThemePreference.Light);
    Check(new ThemePreferenceStore(migrated).LoadPreference() == ThemePreference.Light,
        "A subsequent launch prefers the updated new setting over the backup");
    Check(File.ReadAllText(OldPath(migrated)) == " System \r\n", "Later saves leave the legacy backup untouched");

    var existing = Path.Combine(testRoot, "existing");
    WritePreference(OldPath(existing), "Light");
    WritePreference(NewPath(existing), "Dark");
    Check(new ThemePreferenceStore(existing).LoadPreference() == ThemePreference.Dark
        && File.ReadAllText(NewPath(existing)) == "Dark", "An existing new preference always wins");

    var blocked = Path.Combine(testRoot, "blocked");
    WritePreference(OldPath(blocked), "Light");
    File.WriteAllText(Path.Combine(blocked, "ElkaSoft"), "Fixture blocking directory creation");
    var blockedStore = new ThemePreferenceStore(blocked);
    Check(blockedStore.LoadPreference() == ThemePreference.Light, "A blocked migration still loads the legacy preference");
    blockedStore.SavePreference(ThemePreference.System);
    Check(File.ReadAllText(OldPath(blocked)) == "Light", "An unavailable save location does not damage the backup");

    var invalid = Path.Combine(testRoot, "invalid");
    foreach (var value in new[] { "", "bad-theme", "999" })
    {
        WritePreference(NewPath(invalid), value);
        Check(new ThemePreferenceStore(invalid).LoadPreference() == ThemePreference.Dark,
            $"Invalid preference '{value}' falls back safely");
    }
    WritePreference(OldPath(invalid), "Light");
    Check(new ThemePreferenceStore(invalid).LoadPreference() == ThemePreference.Light
        && File.ReadAllText(NewPath(invalid)) == "999", "Fallback does not overwrite an existing destination file");

    Console.WriteLine("All settings migration checks passed.");
}
finally
{
    // testRoot was freshly allocated by CreateTempSubdirectory, not supplied by a user.
    Directory.Delete(testRoot, recursive: true);
}

static string NewPath(string root) => Path.Combine(root, "ElkaSoft", "ELKA.PowerThrottleControl", "theme.txt");
static string OldPath(string root) => Path.Combine(root, "ELKA.PowerThrottleControl", "theme.txt");
static void WritePreference(string path, string value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, value);
}
static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine($"PASS: {description}");
}
