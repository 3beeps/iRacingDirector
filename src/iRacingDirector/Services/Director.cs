using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IRacingDirector.Core;

namespace IRacingDirector.Services;

/// <summary>
/// Watches for the iRacing sim and starts/stops the configured support apps around each session.
/// All public members are used from the UI thread.
/// </summary>
public sealed class Director : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly DirectorSettings _settings;
    private readonly Logger _log;
    private readonly DispatcherTimer _timer;
    private readonly SessionTracker _tracker;
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private CancellationTokenSource _launchCts = new();

    private SessionState _state;
    private string _statusText = "Waiting for iRacing";
    private DateTime? _sessionStartedAt;
    private bool _isBusy;
    private bool _uiRunning;

    public Director(DirectorSettings settings, Logger log)
    {
        _settings = settings;
        _log = log;
        _tracker = new SessionTracker(TimeSpan.FromSeconds(settings.SessionEndDelaySeconds));
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Poll();
    }

    public SessionState State { get => _state; private set => SetField(ref _state, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public DateTime? SessionStartedAt { get => _sessionStartedAt; private set => SetField(ref _sessionStartedAt, value); }
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }
    public bool IRacingUiRunning { get => _uiRunning; private set => SetField(ref _uiRunning, value); }

    /// <summary>Raised for session transitions (for tray notifications).</summary>
    public event Action<SessionEvent>? SessionChanged;

    public void Start()
    {
        _log.Info(Elevation.IsElevated ? "Director started (administrator)." : "Director started.");
        Poll();
        _timer.Start();
    }

    private void Poll()
    {
        List<(string Name, int Id)> snapshot;
        try { snapshot = ProcessController.Snapshot(); }
        catch (Exception ex) { _log.Error($"Could not read running processes: {ex.Message}"); return; }

        foreach (var app in _settings.Apps)
        {
            app.IsRunning = ProcessController.IsRunning(app, snapshot);
            // Give freshly launched apps time to appear before forgetting we started them.
            if (!app.IsRunning && DateTime.UtcNow - app.LaunchedAt > TimeSpan.FromSeconds(30))
                app.LaunchedByDirector = false;
        }

        var uiName = System.IO.Path.GetFileNameWithoutExtension(_settings.IRacingUiPath);
        IRacingUiRunning = snapshot.Any(p => string.Equals(p.Name, uiName, StringComparison.OrdinalIgnoreCase));

        var simRunning = snapshot.Any(p => ProcessMatcher.Matches(p.Name, _settings.SimProcessName));
        _tracker.EndDelay = TimeSpan.FromSeconds(_settings.SessionEndDelaySeconds);
        var ev = _tracker.Update(simRunning, DateTime.Now);
        State = _tracker.State;

        switch (ev)
        {
            case SessionEvent.Started:
                SessionStartedAt = DateTime.Now;
                _log.Info("iRacing session started.");
                SessionChanged?.Invoke(ev);
                if (_settings.AutoMode) _ = StartAppsAsync(manual: false);
                break;
            case SessionEvent.Resumed:
                _log.Info("iRacing is back. Keeping apps open.");
                break;
            case SessionEvent.Ended:
                SessionStartedAt = null;
                _log.Info("iRacing session ended.");
                SessionChanged?.Invoke(ev);
                if (_settings.AutoMode) _ = CloseAppsAsync(manual: false);
                break;
        }

        StatusText = State switch
        {
            SessionState.InSession => "In session",
            SessionState.Ending => $"Sim closed. Closing apps in {_tracker.SecondsUntilEnd(DateTime.Now)}s",
            _ => IRacingUiRunning ? "iRacing UI open. Waiting for a session" : "Waiting for iRacing",
        };
    }

    /// <summary>Launches every app marked "start with session" (or all apps when manual) in list order.</summary>
    public async Task StartAppsAsync(bool manual)
    {
        _launchCts.Cancel();
        _launchCts = new CancellationTokenSource();
        var ct = _launchCts.Token;

        await _opLock.WaitAsync();
        IsBusy = true;
        try
        {
            var apps = _settings.Apps.Where(a => a.StartWithSession).ToList();
            if (apps.Count == 0) { if (manual) _log.Warn("No apps are set to start with a session."); return; }

            var snapshot = ProcessController.Snapshot();
            foreach (var app in apps)
            {
                ct.ThrowIfCancellationRequested();
                if (ProcessController.IsRunning(app, snapshot))
                {
                    _log.Info($"{app.Name} is already running.");
                    continue;
                }
                if (app.StartDelaySeconds > 0)
                {
                    app.StatusText = $"Starting in {app.StartDelaySeconds}s";
                    await Task.Delay(TimeSpan.FromSeconds(app.StartDelaySeconds), ct);
                    app.StatusText = "";
                }
                if (ProcessController.Launch(app, _log)) app.LaunchedByDirector = true;
            }
        }
        catch (OperationCanceledException)
        {
            _log.Info("App launch cancelled.");
        }
        finally
        {
            foreach (var a in _settings.Apps) a.StatusText = "";
            IsBusy = false;
            _opLock.Release();
        }
    }

    /// <summary>Closes every app marked "close with session" in reverse list order.</summary>
    public async Task CloseAppsAsync(bool manual)
    {
        _launchCts.Cancel();
        await _opLock.WaitAsync();
        IsBusy = true;
        try
        {
            var grace = TimeSpan.FromSeconds(_settings.CloseGraceSeconds);
            var apps = _settings.Apps.Where(a => a.StopWithSession && a.IsRunning).Reverse().ToList();
            if (!manual && _settings.OnlyCloseAppsDirectorStarted)
                apps = apps.Where(a => a.LaunchedByDirector).ToList();

            foreach (var app in apps)
            {
                app.StatusText = "Closing…";
                try { await ProcessController.CloseAsync(app, grace, _log); }
                finally { app.StatusText = ""; }
            }
        }
        finally
        {
            IsBusy = false;
            _opLock.Release();
            Poll();
        }
    }

    public async Task LaunchAppAsync(AppEntry app)
    {
        if (app.IsRunning) { _log.Info($"{app.Name} is already running."); return; }
        if (ProcessController.Launch(app, _log)) app.LaunchedByDirector = true;
        await Task.Delay(1000);
        Poll();
    }

    public async Task CloseAppAsync(AppEntry app)
    {
        app.StatusText = "Closing…";
        try { await ProcessController.CloseAsync(app, TimeSpan.FromSeconds(_settings.CloseGraceSeconds), _log); }
        finally { app.StatusText = ""; Poll(); }
    }

    public void LaunchIRacing()
    {
        if (IRacingUiRunning) { _log.Info("The iRacing UI is already open."); return; }
        var ui = new AppEntry { Name = "iRacing", Path = _settings.IRacingUiPath };
        // iRacing shouldn't inherit Director's admin rights: an elevated sim can lock out
        // overlays and tools the user starts normally.
        if (Elevation.IsElevated) ProcessController.LaunchAsUser(ui, _log);
        else ProcessController.Launch(ui, _log);
    }

    /// <summary>Closes the sim and the iRacing UI, then the support apps.</summary>
    public async Task CloseEverythingAsync()
    {
        var iracing = new AppEntry
        {
            Name = "iRacing",
            Path = _settings.IRacingUiPath,
            ExtraProcessNames = _settings.SimProcessName,
        };
        await ProcessController.CloseAsync(iracing, TimeSpan.FromSeconds(_settings.CloseGraceSeconds), _log);
        await CloseAppsAsync(manual: true);
    }

    public void Dispose()
    {
        _timer.Stop();
        _launchCts.Cancel();
    }
}
