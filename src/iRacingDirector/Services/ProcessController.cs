using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRacingDirector.Core;

namespace IRacingDirector.Services;

public static class ProcessController
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorCancelled = 1223;

    /// <summary>Lightweight snapshot of running processes (name + id).</summary>
    public static List<(string Name, int Id)> Snapshot()
    {
        var procs = Process.GetProcesses();
        var list = new List<(string, int)>(procs.Length);
        foreach (var p in procs)
        {
            list.Add((p.ProcessName, p.Id));
            p.Dispose();
        }
        return list;
    }

    public static bool IsRunning(AppEntry app, IReadOnlyList<(string Name, int Id)> snapshot) =>
        snapshot.Any(p => Belongs(app, p.Name, p.Id, TryGetPath));

    /// <summary>
    /// Whether a running process is part of the app. The app's own exe only counts when it runs from the
    /// app's folder (so a shared name like "Update.exe" doesn't catch other programs); extra names match by
    /// name. Protected Windows processes never match.
    /// </summary>
    internal static bool Belongs(AppEntry app, string name, int id, Func<int, string?> pathOf)
    {
        if (id == Environment.ProcessId || ProcessMatcher.IsProtected(name)) return false;

        if (app.MainProcessName is { } main && string.Equals(name, main, StringComparison.OrdinalIgnoreCase))
        {
            var path = pathOf(id);
            if (path is null) return true; // Can't tell where it runs from; trust the name.
            var folder = Path.GetDirectoryName(app.Path);
            if (string.IsNullOrEmpty(folder)) return true;
            if (path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            // Same name from another folder: only ours if an extra pattern claims it.
        }

        return ProcessMatcher.MatchesAny(name, app.GetExtraPatterns());
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Full exe path of a process, or null if Windows won't say (works for admin processes too).</summary>
    internal static string? TryGetPath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static bool Launch(AppEntry app, Logger log)
    {
        if (!app.PathExists)
        {
            log.Error($"{app.Name}: file not found ({app.Path}).");
            return false;
        }

        var psi = new ProcessStartInfo(app.Path)
        {
            UseShellExecute = true,
            Arguments = app.Arguments ?? "",
            WorkingDirectory = Path.GetDirectoryName(app.Path) ?? "",
            WindowStyle = app.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
        };

        try
        {
            Process.Start(psi)?.Dispose();
            log.Success($"Started {app.Name}.");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            log.Warn($"{app.Name}: the administrator prompt was cancelled. Turn on Settings > Run as administrator to avoid it.");
        }
        catch (Exception ex)
        {
            log.Error($"{app.Name}: could not start ({ex.Message}).");
        }
        return false;
    }

    /// <summary>
    /// Starts a program as the signed-in user without admin rights, even from an elevated Director:
    /// Explorer (which never runs elevated) does the launch. Explorer can't pass arguments, so this is
    /// only for programs that don't need any.
    /// </summary>
    public static bool LaunchAsUser(AppEntry app, Logger log)
    {
        if (!app.PathExists)
        {
            log.Error($"{app.Name}: file not found ({app.Path}).");
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{app.Path}\"") { UseShellExecute = true })?.Dispose();
            log.Success($"Started {app.Name}.");
            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"{app.Name}: couldn't start it as a normal user ({ex.Message}); starting it directly.");
            return Launch(app, log);
        }
    }

    /// <summary>
    /// Asks every process of the app to close, then force-closes whatever is left after the grace period.
    /// Returns true if nothing of the app is left running.
    /// </summary>
    public static async Task<bool> CloseAsync(AppEntry app, TimeSpan grace, Logger log, CancellationToken ct = default)
    {
        var targets = Snapshot()
            .Where(p => Belongs(app, p.Name, p.Id, TryGetPath))
            .Select(p => p.Id)
            .ToHashSet();
        if (targets.Count == 0) return true;

        // 1) Polite close: lets apps save settings/telemetry.
        foreach (var id in targets) TryCloseMainWindow(id);

        // 2) Wait for them to exit on their own.
        var deadline = DateTime.UtcNow + grace;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, ct);
            var alive = Snapshot().Select(p => p.Id).ToHashSet();
            targets.IntersectWith(alive);
            if (targets.Count == 0)
            {
                log.Success($"Closed {app.Name}.");
                return true;
            }
        }

        // 3) Force close what's left (tray apps without windows usually end up here).
        var denied = false;
        foreach (var id in targets)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                p.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { /* already exited */ }
            catch (InvalidOperationException) { /* already exited */ }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorAccessDenied) { denied = true; }
            catch (Exception ex) { log.Error($"{app.Name}: could not close process {id} ({ex.Message})."); }
        }

        await Task.Delay(300, ct);
        var stillRunning = IsRunning(app, Snapshot());
        if (!stillRunning) log.Success($"Closed {app.Name}.");
        else if (denied && !Elevation.IsElevated)
            log.Error($"{app.Name} runs as administrator, so Windows won't let Director close it. Turn on Settings > Run as administrator.");
        else log.Error($"{app.Name} is still running after trying to close it.");
        return !stillRunning;
    }

    private static void TryCloseMainWindow(int id)
    {
        try
        {
            using var p = Process.GetProcessById(id);
            if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
        }
        catch
        {
            // Inaccessible or already gone; the force-close step handles it.
        }
    }
}
