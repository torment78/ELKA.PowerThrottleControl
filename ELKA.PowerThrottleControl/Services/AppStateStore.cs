using System.IO;
using System.Text.Json;

namespace ELKA.PowerThrottleControl.Services;

public sealed class AppStateStore
{
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ELKA.PowerThrottleControl");

    private static readonly string StatePath = Path.Combine(StateDirectory, "application-states.json");

    public async Task<Dictionary<string, bool>> LoadAsync()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }

            await using var stream = File.OpenRead(StatePath);
            var states = await JsonSerializer.DeserializeAsync<Dictionary<string, bool>>(stream);
            return states is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(states, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, bool> states)
    {
        Directory.CreateDirectory(StateDirectory);
        var temporaryPath = StatePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, states,
                new JsonSerializerOptions { WriteIndented = true });
        }

        File.Move(temporaryPath, StatePath, overwrite: true);
    }
}


