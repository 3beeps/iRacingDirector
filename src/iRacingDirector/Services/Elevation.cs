using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;

namespace IRacingDirector.Services;

public static class Elevation
{
    public const string RelaunchArg = "--relaunched-from";
    private const int ErrorCancelled = 1223;

    public static bool IsElevated { get; } = CheckElevated();

    private static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Starts a new copy of Director (optionally elevated). The new copy waits for this one to exit
    /// before taking over. Returns false if the user cancelled the UAC prompt.
    /// </summary>
    public static bool Relaunch(bool elevated, params string[] extraArgs)
    {
        var args = Environment.GetCommandLineArgs().Skip(1)
            .Where(a => !a.StartsWith(RelaunchArg, StringComparison.Ordinal))
            .Concat(extraArgs)
            .Append($"{RelaunchArg}={Environment.ProcessId}");

        var psi = new ProcessStartInfo(AppPaths.ExePath)
        {
            UseShellExecute = true,
            Arguments = string.Join(' ', args.Select(Quote)),
        };
        if (elevated) psi.Verb = "runas";

        try
        {
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Win32Exception ex)
        {
            // 1223 = the user clicked No; anything else (blocked by policy, exe missing) is a failure too.
            LastError = ex.NativeErrorCode == ErrorCancelled ? "the administrator prompt was cancelled" : ex.Message;
            return false;
        }
    }

    /// <summary>Why the last <see cref="Relaunch"/> failed, in words for the log.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>If we were started by <see cref="Relaunch"/>, wait for the previous instance to exit.</summary>
    public static void WaitForPreviousInstance(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(RelaunchArg + "=", StringComparison.Ordinal));
        if (arg is null || !int.TryParse(arg[(RelaunchArg.Length + 1)..], out var pid)) return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(15000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Can't wait on it (e.g. it runs as administrator and we don't); give it a moment instead.
            System.Threading.Thread.Sleep(2000);
        }
    }

    private static string Quote(string a) => a.Contains(' ') ? $"\"{a}\"" : a;
}
