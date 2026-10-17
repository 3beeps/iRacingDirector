using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IRacingDirector.Services;

/// <summary>A program the user can launch, found via a Start Menu or desktop shortcut.</summary>
public sealed record InstalledProgram(string Name, string Path);

/// <summary>Finds installed programs by reading Start Menu and desktop shortcuts.</summary>
public static class ProgramScanner
{
    private static readonly Regex Junk = new(
        @"uninstall|help|readme|read me|website|documentation|manual|release notes|check for updates|license|support center|reset preferences",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Task<IReadOnlyList<InstalledProgram>>? _cached;

    /// <summary>Scans once in the background; later calls reuse the result.</summary>
    public static Task<IReadOnlyList<InstalledProgram>> GetProgramsAsync(bool refresh = false)
    {
        if (refresh || _cached is null) _cached = Task.Run(SafeScan);
        return _cached;
    }

    /// <summary>Never throws: a broken shortcut or missing COM component just means fewer programs listed.</summary>
    private static IReadOnlyList<InstalledProgram> SafeScan()
    {
        try { return Scan(); }
        catch { return Array.Empty<InstalledProgram>(); }
    }

    private static IReadOnlyList<InstalledProgram> Scan()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return Array.Empty<InstalledProgram>();
        dynamic shell = Activator.CreateInstance(shellType)!;

        var byPath = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var folder in folders.Where(Directory.Exists))
            {
                IEnumerable<string> links;
                try { links = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).ToList(); }
                catch { continue; }

                foreach (var lnk in links)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(lnk);
                    if (Junk.IsMatch(name)) continue;

                    string target;
                    try
                    {
                        dynamic sc = shell.CreateShortcut(lnk);
                        target = (string)sc.TargetPath;
                        Marshal.FinalReleaseComObject(sc);
                    }
                    catch { continue; }

                    if (string.IsNullOrWhiteSpace(target) ||
                        !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(target))
                        continue;

                    // Keep the shortest shortcut name per program ("SimHub" over "SimHub (beta)").
                    if (!byPath.TryGetValue(target, out var existing) || name.Length < existing.Name.Length)
                        byPath[target] = new InstalledProgram(name, target);
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return byPath.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
