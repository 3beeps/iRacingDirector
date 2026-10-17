using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using IRacingDirector.Core;

namespace IRacingDirector.Services;

/// <summary>Reads the program list from the old "iRacing Manager" settings.xml.</summary>
public static class IRacingManagerImporter
{
    public static bool IsAvailable => File.Exists(AppPaths.IRacingManagerSettings);

    public static List<AppEntry> Read(string path)
    {
        var doc = XDocument.Load(path);
        var result = new List<AppEntry>();

        foreach (var p in doc.Descendants("Program"))
        {
            string Get(string name) => p.Element(name)?.Value?.Trim() ?? "";
            bool GetBool(string name) => bool.TryParse(Get(name), out var b) && b;
            int GetInt(string name) => int.TryParse(Get(name), out var i) ? i : 0;

            var file = Get("FileName");
            if (file.Length == 0) continue;
            var exe = Path.Combine(Get("InstallLocation"), file);

            // Director launches the iRacing UI itself, so it isn't a "support app".
            if (file.Equals("iRacingUI.exe", StringComparison.OrdinalIgnoreCase)) continue;

            var name = Get("DisplayName");
            result.Add(new AppEntry
            {
                Name = name.Length > 0 ? name : Path.GetFileNameWithoutExtension(file),
                Path = exe,
                Arguments = Get("Arguments"),
                StartWithSession = GetBool("StartWithIRacing"),
                StopWithSession = GetBool("StopWithIRacing"),
                StartMinimized = GetBool("StartHidden"),
                StartDelaySeconds = GetBool("DelayStart") ? GetInt("DelayStartSeconds") : 0,
            });
        }
        return result;
    }

    /// <summary>Adds imported apps whose executable isn't already in the list. Returns how many were added.</summary>
    public static int MergeInto(ICollection<AppEntry> apps, IEnumerable<AppEntry> imported)
    {
        var added = 0;
        foreach (var app in imported)
        {
            var exists = apps.Any(a => string.Equals(
                Path.GetFileName(a.Path), Path.GetFileName(app.Path), StringComparison.OrdinalIgnoreCase));
            if (exists) continue;
            apps.Add(app);
            added++;
        }
        return added;
    }
}
