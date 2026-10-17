using System;
using System.Linq;
using System.Threading;
using System.Windows;
using IRacingDirector.Core;
using IRacingDirector.Services;
using IRacingDirector.UI;

namespace IRacingDirector;

public partial class App : Application
{
    public const string ExitArg = "--exit";
    public const string UninstallArg = "--uninstall";
    public const string NoElevateArg = "--no-elevate";

    // Scoped to the data folder so a test copy (IRACING_DIRECTOR_DATA) can run beside the real one.
    private static readonly string InstanceKey = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppPaths.DataDir.ToLowerInvariant())))[..12];
    private static readonly string MutexName = $@"Local\iRacingDirector.SingleInstance.{InstanceKey}";
    private static readonly string ShowEventName = $@"Local\iRacingDirector.Show.{InstanceKey}";
    private static readonly string ExitEventName = $@"Local\iRacingDirector.Exit.{InstanceKey}";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private TrayIcon? _tray;
    private Director? _director;
    private UpdateChecker? _updates;

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

        // Installer/uninstaller hooks: no UI.
        if (e.Args.Contains(ExitArg)) { ExitRunningCopy(); Shutdown(); return; }
        if (e.Args.Contains(UninstallArg))
        {
            ExitRunningCopy();
            StartupManager.RemoveAll(log);
            Shutdown();
            return;
        }

        Elevation.WaitForPreviousInstance(e.Args);
        if (!TryBecomeSingleInstance()) { Shutdown(); return; }

        var settings = SettingsStore.Load(log);
        if (settings is null)
        {
            settings = new DirectorSettings();
            new SetupWindow(settings, firstRun: true).ShowDialog();
            log.Info($"Setup finished with {settings.Apps.Count} app(s).");
            SettingsStore.Save(settings, log);
        }

        if (settings.RunAsAdmin && !Elevation.IsElevated && !e.Args.Contains(NoElevateArg))
        {
            ReleaseSingleInstance();
            if (Elevation.Relaunch(elevated: true)) { Shutdown(); return; }
            log.Warn("Administrator prompt was cancelled. Running without administrator rights; some apps may not close.");
            TryBecomeSingleInstance();
        }

        StartupManager.Apply(settings.StartWithWindows, settings.RunAsAdmin, log);

        _director = new Director(settings, log);
        _updates = new UpdateChecker(settings, log);
        ViewModel = new MainViewModel(settings, _director, _updates, log);
        _tray = new TrayIcon(ViewModel);

        var window = new MainWindow(ViewModel);
        MainWindow = window;
        var startHidden = e.Args.Contains(StartupManager.TrayArg) && settings.StartMinimizedToTray;
        if (!startHidden) window.Show();

        _director.Start();
        _updates.Start();
    }

    private bool TryBecomeSingleInstance()
    {
        bool createdNew;
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // Another copy runs as administrator; we can't open its mutex, but it exists.
            createdNew = false;
            _mutex = null;
        }

        if (!createdNew)
        {
            SharedEvent.TrySignal(ShowEventName);
            _mutex?.Dispose();
            _mutex = null;
            return false;
        }

        _showEvent = SharedEvent.Create(ShowEventName);
        _exitEvent = SharedEvent.Create(ExitEventName);
        var handles = new WaitHandle[] { _showEvent, _exitEvent };
        var thread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    var which = WaitHandle.WaitAny(handles);
                    if (which == 0) Dispatcher.BeginInvoke(() => ShowMainWindow());
                    else { Dispatcher.BeginInvoke(() => ExitApp()); return; }
                }
            }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true, Name = "InstanceSignals" };
        thread.Start();
        return true;
    }

    private void ReleaseSingleInstance()
    {
        _showEvent?.Dispose();
        _showEvent = null;
        _exitEvent?.Dispose();
        _exitEvent = null;
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
    }

    /// <summary>Asks a running copy to exit and waits (up to 15s) for it to go.</summary>
    private static void ExitRunningCopy()
    {
        if (!SharedEvent.TrySignal(ExitEventName)) return;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && InstanceExists()) Thread.Sleep(250);
    }

    private static bool InstanceExists()
    {
        try
        {
            if (!Mutex.TryOpenExisting(MutexName, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
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
        _updates?.Dispose();
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
