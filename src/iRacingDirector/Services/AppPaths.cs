using System;
using System.IO;

namespace IRacingDirector.Services;

public static class AppPaths
{
    /// <summary>Settings/log folder. IRACING_DIRECTOR_DATA overrides it (handy for testing).</summary>
    public static string DataDir { get; } = CreateDir(
        Environment.GetEnvironmentVariable("IRACING_DIRECTOR_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iRacing Director"));

    /// <summary>True for a test copy using IRACING_DIRECTOR_DATA; such copies leave machine-wide settings alone.</summary>
    public static bool IsCustomDataDir { get; } =
        Environment.GetEnvironmentVariable("IRACING_DIRECTOR_DATA") is { Length: > 0 };

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "director.log");

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "iRacingDirector.exe");

    public static string IRacingManagerSettings => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacingManager", "settings.xml");

    private static string CreateDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
