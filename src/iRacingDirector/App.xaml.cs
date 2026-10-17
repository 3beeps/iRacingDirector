using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using IRacingDirector.Core;
using IRacingDirector.Services;
using IRacingDirector.UI;

namespace IRacingDirector;

public partial class App : Application
{
    // Scoped to the data folder so a test copy (IRACING_DIRECTOR_DATA) can run beside the real one.
    private static readonly string InstanceKey = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppPaths.DataDir.ToLowerInvariant())))[..12];
    private static readonly string MutexName = $@"Local\iRacingDirector.SingleInstance.{InstanceKey}";
    private static readonly string ShowEventName = $@"Local\iRacingDirector.Show.{InstanceKey}";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private Director? _director;

    public MainViewModel? ViewModel { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var log = new Logger();
        DispatcherUnhandledException += (_, ex) =>
        {
            log.Error($"Unexpected error: {ex.Exception.Message}");
            ex.Handled = true;
        };

        Elevation.WaitForPreviousInstance(e.Args);
        if (!TryBecomeSingleInstance()) { Shutdown(); return; }

        var settings = SettingsStore.Load(log) ?? CreateFirstRunSettings(log);

        if (settings.RunAsAdmin && !Elevation.IsElevated && !e.Args.Contains("--no-elevate"))
        {
            ReleaseSingleInstance();
            if (Elevation.Relaunch(elevated: true)) { Shutdown(); return; }
            log.Warn("Administrator prompt was cancelled. Running without administrator rights; some apps may not close.");
            TryBecomeSingleInstance();
        }

        StartupManager.Apply(settings.StartWithWindows, settings.RunAsAdmin, log);

        _director = new Director(settings, log);
        ViewModel = new MainViewModel(settings, _director, log);
        _tray = new TrayIcon(ViewModel);

        var window = new MainWindow(ViewModel);
        MainWindow = window;
        var startHidden = e.Args.Contains(StartupManager.TrayArg) && settings.StartMinimizedToTray;
        if (!startHidden) window.Show();

        _director.Start();
    }

    private static DirectorSettings CreateFirstRunSettings(Logger log)
    {
        var settings = new DirectorSettings
        {
            // SimHub and friends run as administrator; without this Director can't close them.
            RunAsAdmin = true,
        };
        foreach (var app in DefaultApps.Detect()) settings.Apps.Add(app);

        if (IRacingManagerImporter.IsAvailable)
        {
            try
            {
                var added = IRacingManagerImporter.MergeInto(settings.Apps, IRacingManagerImporter.Read(AppPaths.IRacingManagerSettings));
                if (added > 0) log.Info($"Imported {added} app(s) from iRacing Manager.");
            }
            catch (Exception ex)
            {
                log.Warn($"Could not import iRacing Manager settings: {ex.Message}");
            }
        }

        log.Info($"First run: found {settings.Apps.Count} app(s).");
        SettingsStore.Save(settings, log);
        return settings;
    }

    private bool TryBecomeSingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
            catch { /* The other instance may be elevated; nothing more we can do. */ }
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var handle = _showEvent;
        var thread = new Thread(() =>
        {
            try
            {
                while (handle.WaitOne())
                    Dispatcher.BeginInvoke(() => ShowMainWindow());
            }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true, Name = "ShowSignal" };
        thread.Start();
        return true;
    }

    private void ReleaseSingleInstance()
    {
        _showEvent?.Dispose();
        _showEvent = null;
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
    }

    public static void ShowMain() => ((App)Current).ShowMainWindow();

    public void ShowMainWindow()
    {
        if (MainWindow is not { } w) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true;
        w.Topmost = false;
    }

    /// <summary>Restarts Director, optionally elevated. Returns false if the UAC prompt was cancelled.</summary>
    public bool Restart(bool elevated)
    {
        ReleaseSingleInstance();
        if (!elevated && Elevation.IsElevated)
        {
            // An elevated process can't start a normal one directly; Explorer launches us as the signed-in user.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AppPaths.ExePath}\"") { UseShellExecute = true });
            ExitApp();
            return true;
        }
        if (Elevation.Relaunch(elevated))
        {
            ExitApp();
            return true;
        }
        TryBecomeSingleInstance();
        return false;
    }

    public void ExitApp()
    {
        if (MainWindow is MainWindow mw) mw.AllowClose = true;
        _tray?.Dispose();
        _director?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        ReleaseSingleInstance();
        base.OnExit(e);
    }
}
