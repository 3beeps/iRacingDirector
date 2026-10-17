using System;
using System.Collections.Generic;
using System.IO;
using IRacingDirector.Core;
using IRacingDirector.Services;
using Xunit;

namespace IRacingDirector.Tests;

public class SessionTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void StartsWhenSimAppears()
    {
        var t = new SessionTracker(TimeSpan.FromSeconds(10));
        Assert.Equal(SessionEvent.None, t.Update(false, T0));
        Assert.Equal(SessionEvent.Started, t.Update(true, T0.AddSeconds(2)));
        Assert.Equal(SessionState.InSession, t.State);
        Assert.Equal(SessionEvent.None, t.Update(true, T0.AddSeconds(4)));
    }

    [Fact]
    public void EndsOnlyAfterDelay()
    {
        var t = new SessionTracker(TimeSpan.FromSeconds(10));
        t.Update(true, T0);
        Assert.Equal(SessionEvent.None, t.Update(false, T0.AddSeconds(1)));
        Assert.Equal(SessionState.Ending, t.State);
        Assert.Equal(5, t.SecondsUntilEnd(T0.AddSeconds(6)));
        Assert.Equal(SessionEvent.None, t.Update(false, T0.AddSeconds(9)));
        Assert.Equal(SessionEvent.Ended, t.Update(false, T0.AddSeconds(11)));
        Assert.Equal(SessionState.Idle, t.State);
    }

    [Fact]
    public void RejoiningDuringDelayResumes()
    {
        var t = new SessionTracker(TimeSpan.FromSeconds(10));
        t.Update(true, T0);
        t.Update(false, T0.AddSeconds(1));
        Assert.Equal(SessionEvent.Resumed, t.Update(true, T0.AddSeconds(5)));
        Assert.Equal(SessionState.InSession, t.State);
        // A later exit restarts the countdown from scratch.
        t.Update(false, T0.AddSeconds(20));
        Assert.Equal(SessionEvent.None, t.Update(false, T0.AddSeconds(25)));
        Assert.Equal(SessionEvent.Ended, t.Update(false, T0.AddSeconds(30)));
    }

    [Fact]
    public void ZeroDelayEndsImmediately()
    {
        var t = new SessionTracker(TimeSpan.Zero);
        t.Update(true, T0);
        Assert.Equal(SessionEvent.Ended, t.Update(false, T0.AddSeconds(1)));
    }
}

public class ProcessMatcherTests
{
    [Theory]
    [InlineData("SimHubWPF", "simhubwpf", true)]
    [InlineData("garage61-agent-20261008125121-34ed9ae", "garage61-agent*", true)]
    [InlineData("garage61-launcher", "garage61-agent*", false)]
    [InlineData("VrsOne", "Vrs?ne", true)]
    [InlineData("Trading Paints", "Trading Paints", true)]
    [InlineData("anything", "", false)]
    public void Matches(string process, string pattern, bool expected) =>
        Assert.Equal(expected, ProcessMatcher.Matches(process, pattern));

    [Fact]
    public void AppPatternsIncludeExeAndExtras()
    {
        var app = new AppEntry
        {
            Path = @"C:\x\garage61-launcher.exe",
            ExtraProcessNames = "garage61-agent*, other.exe",
        };
        Assert.Equal(new[] { "garage61-launcher", "garage61-agent*", "other" }, app.GetProcessPatterns());
    }
}

public class ProcessSafetyTests
{
    private static readonly AppEntry Discord = new() { Name = "Discord PTB", Path = @"C:\Users\x\AppData\Local\DiscordPTB\Update.exe" };

    [Theory]
    [InlineData("*")]
    [InlineData("a*")]
    [InlineData("??*")]
    [InlineData(" * ")]
    public void BroadPatternsMatchNothing(string pattern)
    {
        Assert.False(ProcessMatcher.Matches("explorer", pattern));
        Assert.False(ProcessMatcher.Matches("SimHubWPF", pattern));
        var app = new AppEntry { Path = @"C:\x\tool.exe", ExtraProcessNames = pattern };
        Assert.Empty(app.GetExtraPatterns());
    }

    [Fact]
    public void SameExeNameFromAnotherFolderIsNotTheApp()
    {
        // Another program's Update.exe must not be closed along with Discord PTB.
        Assert.False(ProcessController.Belongs(Discord, "Update", 100, _ => @"C:\Users\x\AppData\Local\SomethingElse\Update.exe"));
        Assert.True(ProcessController.Belongs(Discord, "Update", 101, _ => @"C:\Users\x\AppData\Local\DiscordPTB\Update.exe"));
        // Subfolders of the app count (e.g. apps that run from app-1.2.3\).
        Assert.True(ProcessController.Belongs(Discord, "Update", 102, _ => @"C:\Users\x\AppData\Local\DiscordPTB\app-1.0\Update.exe"));
        // Path unknown (Windows wouldn't say): trust the name.
        Assert.True(ProcessController.Belongs(Discord, "Update", 103, _ => null));
    }

    [Fact]
    public void ExtraNamesStillMatchFromAnywhere()
    {
        var g61 = new AppEntry { Path = @"C:\g61-install\garage61-launcher.exe", ExtraProcessNames = "garage61-agent*" };
        Assert.True(ProcessController.Belongs(g61, "garage61-agent-2026", 200, _ => @"C:\elsewhere\garage61-agent-2026.exe"));
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("svchost")]
    [InlineData("csrss")]
    [InlineData("iRacingService64")]
    public void WindowsProcessesAreNeverPartOfAnApp(string name)
    {
        var app = new AppEntry { Path = $@"C:\Windows\{name}.exe", ExtraProcessNames = name };
        Assert.False(ProcessController.Belongs(app, name, 300, _ => $@"C:\Windows\{name}.exe"));
    }

    [Theory]
    [InlineData("iRacingSim64DX11", true)]
    [InlineData("iRacingSim64DX12", true)]
    [InlineData("iRacingUI", false)]
    public void DefaultSimPatternFindsTheSim(string process, bool expected) =>
        Assert.Equal(expected, ProcessMatcher.Matches(process, DirectorSettings.DefaultSimProcess));
}

public class CatalogTests
{
    [Fact]
    public void DetectsAppsByShortcutTargetOrName()
    {
        var programs = new List<InstalledProgram>
        {
            new("SimHub", @"D:\Games\SimHub\SimHubWPF.exe"),          // matched by exe name, unusual folder
            new("Garage 61 Telemetry Agent", @"E:\g61\garage61-launcher.exe"),
            new("Kapps", @"E:\Kapps\Kapps.exe"),                      // matched by shortcut name
            new("Notepad++", @"C:\npp\notepad++.exe"),                // not a racing app
        };

        var found = DefaultApps.Detect(programs);

        Assert.Contains(found, d => d.Known.Name == "SimHub" && d.Path == @"D:\Games\SimHub\SimHubWPF.exe" && d.Known.NeedsAdmin);
        Assert.Contains(found, d => d.Known.Name == "Garage 61" && d.Known.ExtraProcessNames == "garage61-agent*");
        Assert.Contains(found, d => d.Known.Name == "Kapps");
        Assert.DoesNotContain(found, d => d.Path.Contains("notepad"));
    }

    [Fact]
    public void LookupGivesKnownSettingsForBrowsedFiles()
    {
        var entry = DefaultApps.Lookup(@"X:\anywhere\garage61-launcher.exe")!.ToEntry(@"X:\anywhere\garage61-launcher.exe");
        Assert.Equal("Garage 61", entry.Name);
        Assert.Equal("garage61-agent*", entry.ExtraProcessNames);
        Assert.Null(DefaultApps.Lookup(@"C:\x\random.exe"));
    }
}

public class ImporterTests
{
    [Fact]
    public void ReadsIRacingManagerSettings()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Settings><Programs>
              <Program>
                <StartWithIRacing>true</StartWithIRacing><StopWithIRacing>true</StopWithIRacing>
                <Name>Trading Paints</Name><DisplayName>Trading Paints</DisplayName>
                <InstallLocation>C:\TP\</InstallLocation><FileName>Trading Paints.exe</FileName>
                <StartHidden>true</StartHidden><DelayStart>true</DelayStart><DelayStartSeconds>7</DelayStartSeconds>
              </Program>
              <Program>
                <StartWithIRacing>false</StartWithIRacing><StopWithIRacing>false</StopWithIRacing>
                <DisplayName>iRacingUI</DisplayName><InstallLocation>C:\iRacing\ui</InstallLocation><FileName>iRacingUI.exe</FileName>
              </Program>
            </Programs></Settings>
            """;
        var file = Path.GetTempFileName();
        File.WriteAllText(file, xml);
        try
        {
            var apps = IRacingManagerImporter.Read(file);
            var tp = Assert.Single(apps);
            Assert.Equal("Trading Paints", tp.Name);
            Assert.Equal(@"C:\TP\Trading Paints.exe", tp.Path);
            Assert.True(tp.StartWithSession);
            Assert.True(tp.StartMinimized);
            Assert.Equal(7, tp.StartDelaySeconds);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void MergeSkipsAppsAlreadyPresent()
    {
        var existing = new List<AppEntry> { new() { Name = "SimHub", Path = @"C:\Program Files (x86)\SimHub\SimHubWPF.exe" } };
        var added = IRacingManagerImporter.MergeInto(existing, new[]
        {
            new AppEntry { Name = "SimHub", Path = @"D:\Other\SimHubWPF.exe" },
            new AppEntry { Name = "Crew Chief", Path = @"C:\CC\CrewChiefV4.exe" },
        });
        Assert.Equal(1, added);
        Assert.Equal(2, existing.Count);
    }
}
