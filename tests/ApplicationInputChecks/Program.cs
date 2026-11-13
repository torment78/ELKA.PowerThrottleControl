using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ELKA.PowerThrottleControl.Models;
using ELKA.PowerThrottleControl.Services;

// The command-line check invokes this test executable as a harmless stand-in for powercfg.
if (args.FirstOrDefault() == "/powerthrottling")
{
    File.WriteAllText(Environment.GetEnvironmentVariable("ELKA_TEST_ARGUMENT_CAPTURE")!, JsonSerializer.Serialize(args));
    return;
}

var root = Directory.CreateTempSubdirectory("Elka-ApplicationInputChecks-").FullName;
try
{
    var gameDirectory = Path.Combine(root, "Games & Things (x64)^ ! 100%", "_retail_");
    Directory.CreateDirectory(gameDirectory);
    var gamePath = Path.Combine(gameDirectory, "cod.exe");
    File.WriteAllText(gamePath, "An EXE-named fixture; this file is never executed.");
    var app = ExecutableApplication.Create($"  \"{gamePath}\"  ");
    Check(app.ExecutablePath == gamePath && app.DisplayName == "cod", "Quoted full paths resolve to a usable game row");
    Check(app.IsThrottlingDisabled is null, "Adding an EXE does not invent a power-throttling state");
    Expect<ArgumentException>(() => ExecutableApplication.Create("cod.exe"), "Relative paths are rejected");
    Expect<ArgumentException>(() => ExecutableApplication.Create($"\"{gamePath}\" --arguments"), "Command lines are rejected instead of being executed");
    Expect<ArgumentException>(() => ExecutableApplication.Create(gamePath + "\r\necho unexpected"), "Multiline paths are rejected");
    Expect<ArgumentException>(() => ExecutableApplication.Create(Path.Combine(gameDirectory, "game.lnk")), "Shortcuts are rejected");
    Expect<FileNotFoundException>(() => ExecutableApplication.Create(Path.Combine(gameDirectory, "missing.exe")), "Missing executables are rejected");

    var dataRoot = Path.Combine(root, "profile");
    var store = new ManualApplicationStore(dataRoot);
    Check(store.LoadPaths().Count == 0, "First use starts with an empty saved list");
    store.Remember(gamePath);
    store.Remember(gamePath.ToUpperInvariant());
    var savedPath = Path.Combine(dataRoot, "ElkaSoft", "ELKA.PowerThrottleControl", "applications.json");
    Check(File.Exists(savedPath) && new ManualApplicationStore(dataRoot).LoadPaths().Count == 1,
        "Manual paths persist under ElkaSoft without case-only duplicates");
    var restored = ExecutableApplication.Create(new ManualApplicationStore(dataRoot).LoadPaths()[0]);
    Check(restored.ExecutablePath == gamePath && restored.IsThrottlingDisabled is null, "Relaunch restores the path while status remains unconfirmed");
    var discovered = new ApplicationEntry { DisplayName = "Call of Duty", ExecutablePath = gamePath.ToUpperInvariant() };
    Check(ExecutableApplication.Merge([discovered], [app]).Count == 1, "Search merges the manually added and discovered copies");
    Check(ExecutableApplication.Merge([], [app]).Single().ExecutablePath == gamePath, "Another search cannot remove a manually added game");

    File.Delete(gamePath);
    Check(ExecutableApplication.TryCreate(gamePath) is null, "Unavailable executables are not offered as usable rows");
    var secondPath = Path.Combine(gameDirectory, "another.exe");
    File.WriteAllText(secondPath, "Another non-executed fixture");
    store.Remember(secondPath);
    Check(store.LoadPaths().Contains(gamePath), "A disconnected drive's saved path survives later saves");
    File.WriteAllText(gamePath, "Reconnected fixture");
    Check(ExecutableApplication.TryCreate(gamePath) is not null, "Reconnected game paths become usable again");
    File.WriteAllText(savedPath, "invalid-json-backup");
    Expect<IOException>(() => store.Remember(gamePath), "An unreadable saved list is reported");
    Check(File.ReadAllText(savedPath) == "invalid-json-backup", "A damaged saved list is never overwritten");

    Check(RunningApplicationDiscovery.ReadExecutablePath(Environment.ProcessId)?.Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) == true,
        "Limited-access process lookup returns the real running executable");
    Check(RunningApplicationDiscovery.ReadExecutablePath(-1) is null, "An inaccessible or nonexistent process is skipped");
    Check(RunningApplicationDiscovery.Discover().Any(entry => entry.ExecutablePath.Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)),
        "Running application discovery includes this ordinary process");

    // Execute only a generated batch script and this test program, never powercfg or a game.
    foreach (var disable in new[] { true, false })
    {
        var capture = Path.Combine(root, "captured-arguments.json");
        var results = Path.Combine(gameDirectory, "results.txt");
        var script = PowerThrottlingService.BuildCommandScript(
            [new ApplicationEntry { DisplayName = "Call of Duty \"Test\" & () ^ ! 100%", ExecutablePath = gamePath }], disable, results);
        script = script.Replace("\npowercfg ", $"\n\"{Environment.ProcessPath}\" ").Replace("pause >nul", "rem No pause in checks");
        Check(!script.Split('\n').Any(line => line.StartsWith("powercfg ")), "Command harness cannot invoke the real powercfg");
        var scriptPath = Path.Combine(root, "check-command.cmd");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Arguments = $"/d /c \"\"{scriptPath}\"\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["ELKA_TEST_ARGUMENT_CAPTURE"] = capture;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await output;
        var errorText = await errors;
        Check(process.ExitCode == 0 && errorText.Length == 0, "The generated command handles special characters without shell errors");
        var captured = JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
        Check(captured.SequenceEqual(new[] { "/powerthrottling", disable ? "disable" : "enable", "/path", gamePath }),
            $"{(disable ? "Disable" : "Enable")} passes the exact EXE path as one argument");
        Check(File.ReadAllText(results).Trim() == "1", "Command results are written correctly for special-character directories");
    }
    Console.WriteLine("All application input checks passed.");
}
finally { Directory.Delete(root, recursive: true); }

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine("PASS: " + description);
}
static void Expect<T>(Action action, string description) where T : Exception
{
    try { action(); } catch (T) { Console.WriteLine("PASS: " + description); return; }
    throw new InvalidOperationException(description);
}
