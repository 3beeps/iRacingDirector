using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IRacingDirector.Core;
using Microsoft.Win32;

namespace IRacingDirector.Services;

/// <summary>A sim racing app Director knows how to find and manage.</summary>
public sealed record KnownApp(
    string Name,
    string[] ExeNames,
    string? ShortcutPattern = null,
    string[]? KnownPaths = null,
    string ExtraProcessNames = "",
    bool DefaultOn = true,
    bool NeedsAdmin = false)
{
    public bool MatchesExe(string path) =>
        ExeNames.Any(e => string.Equals(Path.GetFileName(path), e, StringComparison.OrdinalIgnoreCase));

    public bool MatchesShortcut(string name) =>
        ShortcutPattern is not null && Regex.IsMatch(name, ShortcutPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public AppEntry ToEntry(string path) => new()
    {
        Name = Name,
        Path = path,
        ExtraProcessNames = ExtraProcessNames,
        StartWithSession = true,
        StopWithSession = true,
    };
}

public sealed record DetectedApp(KnownApp Known, string Path);

/// <summary>Catalog of popular iRacing companion apps plus detection on this PC.</summary>
public static class DefaultApps
{
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Pf86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static string Pf => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static IReadOnlyList<KnownApp> Catalog { get; } = new KnownApp[]
    {
        // SimHub runs as administrator, so Director needs admin rights to close it.
        new("SimHub", new[] { "SimHubWPF.exe" }, @"^SimHub$", new[] { Path.Combine(Pf86, "SimHub", "SimHubWPF.exe") }, NeedsAdmin: true),
        new("Crew Chief", new[] { "CrewChiefV4.exe" }, @"^Crew ?Chief ?V4$", new[] { Path.Combine(Pf86, "Britton IT Ltd", "CrewChiefV4", "CrewChiefV4.exe") }),
        // The launcher spawns a versioned agent (garage61-agent-<build>.exe) that keeps running.
        new("Garage 61", new[] { "garage61-launcher.exe" }, @"^Garage 61", new[] { Path.Combine(Roaming, "garage61-install", "garage61-launcher.exe") }, "garage61-agent*"),
        new("Trading Paints", new[] { "Trading Paints.exe" }, @"^Trading Paints$", new[] { Path.Combine(Pf86, "Rhinode LLC", "Trading Paints", "Trading Paints.exe") }),
        new("VRS One", new[] { "VrsOne.exe" }, @"^VRS One$", new[] { Path.Combine(Local, "VrsOne", "current", "VrsOne.exe") }),
        new("iOverlay", new[] { "iOverlay.exe" }, @"^iOverlay$", new[] { Path.Combine(Pf, "iOverlay", "iOverlay.exe") }),
        new("RaceLab", new[] { "RaceLab.exe", "RaceLab Apps.exe" }, @"^RaceLab", new[] { Path.Combine(Local, "Programs", "racelab-app", "RaceLab.exe") }),
        new("Kapps", new[] { "Kapps.exe" }, @"^Kapps$"),
        new("Joel Real Timing", new[] { "JRT.exe" }, @"^(JRT|Joel Real Timing)"),
        new("trophi.ai", new[] { "trophi.ai.launcher.exe" }, @"^trophi\.ai$", ExtraProcessNames: "trophi.ai*"),
        new("1simracing", new[] { "1simracing.exe" }, @"^1simracing$"),
        new("Coach Dave Delta", new[] { "Delta.exe", "Coach Dave Delta.exe" }, @"^Coach Dave"),
        new("irSidekick Livery", new[] { "irSidekickLivery.exe" }, @"^irSidekick Livery$", DefaultOn: false),
        new("Sim Racing Studio", new[] { "SimRacingStudio.exe" }, @"^Sim ?Racing ?Studio"),
        new("Simucube TrueDrive", new[] { "TrueDrive.exe" }, @"TrueDrive"),
        new("MOZA Pit House", new[] { "MOZA Pit House.exe" }, @"Pit ?House"),
        new("Simagic SimPro Manager", new[] { "SimPro Manager.exe" }, @"SimPro Manager"),
        new("Fanatec App", new[] { "Fanatec App.exe" }, @"^Fanatec App$"),
        new("Asetek RaceHub", new[] { "RaceHub.exe" }, @"RaceHub"),
        new("GSI simOS", new[] { "GSISimOS.exe" }, @"^GSI simOS$", DefaultOn: false),
        new("OBS Studio", new[] { "obs64.exe" }, @"^OBS Studio", DefaultOn: false),
        new("HWiNFO64", new[] { "HWiNFO64.exe" }, @"^HWiNFO", DefaultOn: false),
    };

    /// <summary>Known apps installed on this PC, found via shortcuts first and known install paths second.</summary>
    public static List<DetectedApp> Detect(IReadOnlyList<InstalledProgram> programs)
    {
        var found = new List<DetectedApp>();
        foreach (var known in Catalog)
        {
            var path = programs.FirstOrDefault(p => known.MatchesExe(p.Path))?.Path
                       ?? programs.FirstOrDefault(p => known.MatchesShortcut(p.Name))?.Path
                       ?? known.KnownPaths?.FirstOrDefault(File.Exists);
            if (path is not null) found.Add(new DetectedApp(known, path));
        }
        return found;
    }

    public static KnownApp? Lookup(string path) => Catalog.FirstOrDefault(k => k.MatchesExe(path));

    /// <summary>Finds iRacingUI.exe wherever iRacing was installed.</summary>
    public static string? FindIRacingUi(IReadOnlyList<InstalledProgram> programs)
    {
        if (File.Exists(DirectorSettings.DefaultIRacingUiPath)) return DirectorSettings.DefaultIRacingUiPath;

        var fromShortcut = programs.FirstOrDefault(p =>
            string.Equals(Path.GetFileName(p.Path), "iRacingUI.exe", StringComparison.OrdinalIgnoreCase))?.Path;
        if (fromShortcut is not null) return fromShortcut;

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var keyPath in new[]
                 {
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                 })
        {
            using var key = root.OpenSubKey(keyPath);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(sub);
                if (app?.GetValue("DisplayName") is not string name || !name.StartsWith("iRacing.com", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (app.GetValue("InstallLocation") is string dir && dir.Length > 0)
                {
                    var ui = Path.Combine(dir, "ui", "iRacingUI.exe");
                    if (File.Exists(ui)) return ui;
                }
            }
        }
        return null;
    }
}
