using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ELKA.PowerThrottleControl.Models;

namespace ELKA.PowerThrottleControl.Services;

public sealed class NetworkApplicationDiscoveryService
{
    private static readonly IReadOnlyDictionary<string, string> KnownApplications =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["voicemeeter.exe"] = "VoiceMeeter Standard (32-bit)",
            ["voicemeeter_x64.exe"] = "VoiceMeeter Standard (64-bit)",
            ["voicemeeterpro.exe"] = "VoiceMeeter Banana (32-bit)",
            ["voicemeeterpro_x64.exe"] = "VoiceMeeter Banana (64-bit)",
            ["voicemeeter8.exe"] = "VoiceMeeter Potato (32-bit)",
            ["voicemeeter8x64.exe"] = "VoiceMeeter Potato (64-bit)",
            ["VoicemeeterMacroButtons.exe"] = "VoiceMeeter Macro Buttons",
            ["VBAN2MIDI.exe"] = "VBAN to MIDI",
            ["VBANScreen.exe"] = "VBAN Screen",
            ["VBAudioMatrix.exe"] = "VB-Audio Matrix (32-bit)",
            ["VBAudioMatrix_x64.exe"] = "VB-Audio Matrix (64-bit)",
            ["VBAudioMatrixCoconut.exe"] = "VB-Audio Matrix Coconut (32-bit)",
            ["VBAudioMatrixCoconut_x64.exe"] = "VB-Audio Matrix Coconut (64-bit)"
        };

    public IReadOnlyList<NetworkApplicationEntry> Discover()
    {
        var runningPaths = GetRunningPaths();
        var candidates = new HashSet<string>(runningPaths, StringComparer.OrdinalIgnoreCase);

        foreach (var directory in GetCandidateDirectories().Where(Directory.Exists))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    if (KnownApplications.ContainsKey(Path.GetFileName(path)))
                    {
                        candidates.Add(Path.GetFullPath(path));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // A protected install folder should not prevent the remaining apps from being listed.
            }
            catch (IOException)
            {
                // An application may be updating while discovery runs.
            }
        }

        return candidates
            .Where(File.Exists)
            .Select(path => new NetworkApplicationEntry
            {
                DisplayName = KnownApplications.GetValueOrDefault(Path.GetFileName(path), Path.GetFileNameWithoutExtension(path)),
                ExecutablePath = path,
                Architecture = ReadArchitecture(path),
                IsRunning = runningPaths.Contains(path),
                IsSelected = runningPaths.Contains(path)
            })
            .OrderByDescending(app => app.IsRunning)
            .ThenBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<string> GetRunningPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null && KnownApplications.ContainsKey(Path.GetFileName(path)))
                    {
                        paths.Add(Path.GetFullPath(path));
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Some elevated/system processes cannot be inspected from the normal UI process.
                }
            }
        }

        return paths;
    }

    private static IEnumerable<string> GetCandidateDirectories()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(root, "VB", "Voicemeeter");
            yield return Path.Combine(root, "VB", "VBAudioMatrix");
        }
    }

    private static string ReadArchitecture(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D) return "Unknown";
            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550) return "Unknown";
            return reader.ReadUInt16() switch
            {
                0x014c => "x86",
                0x8664 => "x64",
                0xAA64 => "ARM64",
                _ => "Unknown"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return "Unknown";
        }
    }
}
