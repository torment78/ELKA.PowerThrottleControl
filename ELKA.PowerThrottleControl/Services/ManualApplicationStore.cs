using System.IO;
using System.Text.Json;

namespace ELKA.PowerThrottleControl.Services;

internal sealed class ManualApplicationStore(string localApplicationData)
{
    private readonly string _path = Path.Combine(localApplicationData, "ElkaSoft", "ELKA.PowerThrottleControl", "applications.json");

    public IReadOnlyList<string> LoadPaths()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_path))
                ?? throw new JsonException("The saved application list is empty or invalid.");
            // Keep unavailable drives and removed games in the file for reconnection later.
            return paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (JsonException ex)
        {
            throw new IOException("The saved application list could not be read. It has been left unchanged.", ex);
        }
    }

    public void Remember(string executablePath)
    {
        var path = ExecutableApplication.ResolvePath(executablePath);
        var paths = LoadPaths().Append(path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
