using System;
using System.IO;
using System.Text.Json;
using IRacingDirector.Core;

namespace IRacingDirector.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Loads settings, or returns null on first run.</summary>
    public static DirectorSettings? Load(Logger log)
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<DirectorSettings>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            var backup = path + ".broken";
            File.Copy(path, backup, overwrite: true);
            log.Error($"Settings file was unreadable ({ex.Message}). A copy was saved to {backup}; starting fresh.");
            return null;
        }
    }

    public static void Save(DirectorSettings settings, Logger log)
    {
        try
        {
            var path = AppPaths.SettingsFile;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            log.Error($"Could not save settings: {ex.Message}");
        }
    }
}
