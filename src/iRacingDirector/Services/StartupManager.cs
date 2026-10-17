using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace IRacingDirector.Services;

/// <summary>
/// "Start with Windows". Non-admin mode uses the per-user Run key. Admin mode uses a scheduled
/// task with highest privileges, which is the only way to auto-start elevated without a UAC prompt.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "iRacing Director";
    public const string TrayArg = "--tray";

    public static void Apply(bool startWithWindows, bool asAdmin, Logger log)
    {
        // The startup entry is shared by every copy; a test copy must not rewrite or delete the real one.
        if (AppPaths.IsCustomDataDir) return;
        try
        {
            var useTask = startWithWindows && asAdmin;
            var useRunKey = startWithWindows && !asAdmin;

            SetRunKey(useRunKey);

            if (useTask)
            {
                if (Elevation.IsElevated) CreateTask(log);
                else log.Warn("Start-with-Windows (as administrator) will be registered the next time Director runs elevated.");
            }
            else if (TaskExists())
            {
                if (Elevation.IsElevated) DeleteTask();
                else log.Warn("An elevated startup task exists; run Director as administrator once to remove it.");
            }
        }
        catch (Exception ex)
        {
            log.Error($"Could not update Windows startup: {ex.Message}");
        }
    }

    /// <summary>Removes every startup entry (used by the uninstaller). Asks for admin only if an elevated task exists.</summary>
    public static void RemoveAll(Logger log)
    {
        try
        {
            SetRunKey(false);
            if (!TaskExists()) return;
            if (Elevation.IsElevated)
            {
                DeleteTask();
                return;
            }
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{EntryName}\" /F")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(30000);
        }
        catch (Exception ex)
        {
            log.Warn($"Could not remove the startup entry: {ex.Message}");
        }
    }

    private static void SetRunKey(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled) key.SetValue(EntryName, $"\"{AppPaths.ExePath}\" {TrayArg}");
        else if (key.GetValue(EntryName) is not null) key.DeleteValue(EntryName);
    }

    /// <summary>
    /// Starts Director through its elevated sign-in task, which runs it as administrator without a UAC
    /// prompt. Only if the task exists and points at this exe. Returns false if it couldn't.
    /// </summary>
    public static bool TryStartElevatedTask()
    {
        if (AppPaths.IsCustomDataDir) return false;
        try
        {
            var (code, xml) = RunSchtasksWithOutput($"/Query /TN \"{EntryName}\" /XML");
            if (code != 0 || !xml.Contains(SecurityElement.Escape(AppPaths.ExePath), StringComparison.OrdinalIgnoreCase))
                return false;
            return RunSchtasks($"/Run /TN \"{EntryName}\"") == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TaskExists() => RunSchtasks($"/Query /TN \"{EntryName}\"") == 0;

    private static void DeleteTask() => RunSchtasks($"/Delete /TN \"{EntryName}\" /F");

    private static void CreateTask(Logger log)
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape(AppPaths.ExePath);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts iRacing Director at sign-in.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command><Arguments>{TrayArg}</Arguments></Exec>
              </Actions>
            </Task>
            """;

        var file = Path.Combine(Path.GetTempPath(), "iRacingDirector-task.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            var code = RunSchtasks($"/Create /TN \"{EntryName}\" /XML \"{file}\" /F");
            if (code != 0) log.Error($"Could not register the startup task (schtasks exit code {code}).");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static int RunSchtasks(string args) => RunSchtasksWithOutput(args).Code;

    private static (int Code, string Output) RunSchtasksWithOutput(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var errors = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        errors.Wait(10000);
        if (!p.WaitForExit(10000)) return (-1, output);
        return (p.ExitCode, output);
    }
}
