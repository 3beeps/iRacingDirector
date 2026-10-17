using System;
using System.Collections.Generic;
using System.IO;
using IRacingDirector.Core;

namespace IRacingDirector.Services;

/// <summary>Well-known sim racing apps, added on first run when they're installed.</summary>
public static class DefaultApps
{
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Pf86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static string Pf => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static IEnumerable<AppEntry> Known()
    {
        yield return New("SimHub", Path.Combine(Pf86, "SimHub", "SimHubWPF.exe"));
        // The launcher spawns a versioned agent (garage61-agent-<build>.exe) that keeps running.
        yield return New("Garage 61", Path.Combine(Roaming, "garage61-install", "garage61-launcher.exe"), extra: "garage61-agent*");
        yield return New("Trading Paints", Path.Combine(Pf86, "Rhinode LLC", "Trading Paints", "Trading Paints.exe"));
        yield return New("VRS One", Path.Combine(Local, "VrsOne", "current", "VrsOne.exe"));
        yield return New("iOverlay", Path.Combine(Pf, "iOverlay", "iOverlay.exe"));
        yield return New("Crew Chief", Path.Combine(Pf86, "Britton IT Ltd", "CrewChiefV4", "CrewChiefV4.exe"), enabled: false);
        yield return New("RaceLab", Path.Combine(Local, "Programs", "racelab-app", "RaceLab.exe"), enabled: false);
    }

    /// <summary>Known apps that exist on this PC.</summary>
    public static List<AppEntry> Detect()
    {
        var list = new List<AppEntry>();
        foreach (var app in Known())
            if (File.Exists(app.Path)) list.Add(app);
        return list;
    }

    private static AppEntry New(string name, string path, string extra = "", bool enabled = true) => new()
    {
        Name = name,
        Path = path,
        ExtraProcessNames = extra,
        StartWithSession = enabled,
        StopWithSession = enabled,
    };
}
