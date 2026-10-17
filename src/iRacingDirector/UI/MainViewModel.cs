using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using IRacingDirector.Core;
using IRacingDirector.Services;

namespace IRacingDirector.UI;

public sealed class MainViewModel : ObservableObject
{
    private static readonly HashSet<string> PersistedAppProps = new()
    {
        nameof(AppEntry.Name), nameof(AppEntry.Path), nameof(AppEntry.Arguments), nameof(AppEntry.ExtraProcessNames),
        nameof(AppEntry.StartWithSession), nameof(AppEntry.StopWithSession), nameof(AppEntry.StartMinimized),
        nameof(AppEntry.StartDelaySeconds),
    };

    private readonly DispatcherTimer _clock;
    private string _sessionDuration = "";

    public MainViewModel(DirectorSettings settings, Director director, Logger log)
    {
        Settings = settings;
        Director = director;
        Log = log;

        Settings.PropertyChanged += OnSettingChanged;
        Settings.Apps.CollectionChanged += OnAppsChanged;
        foreach (var app in Settings.Apps) app.PropertyChanged += OnAppChanged;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
    }

    public DirectorSettings Settings { get; }
    public Director Director { get; }
    public Logger Log { get; }
    public ObservableCollection<AppEntry> Apps => Settings.Apps;
    public ObservableCollection<LogEntry> LogEntries => Log.Entries;

    public bool IsElevated => Elevation.IsElevated;
    public string ElevationText => IsElevated ? "Running as administrator." : "Running as a standard user.";
    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    public string SessionDuration { get => _sessionDuration; private set => SetField(ref _sessionDuration, value); }

    /// <summary>Raised when "Run as administrator" changes and a restart would apply it.</summary>
    public event Action? RestartRequired;

    public void LaunchIRacing() => Director.LaunchIRacing();
    public Task StartAppsAsync() => Director.StartAppsAsync(manual: true);
    public Task CloseAppsAsync() => Director.CloseAppsAsync(manual: true);
    public Task CloseEverythingAsync() => Director.CloseEverythingAsync();

    public void Save() => SettingsStore.Save(Settings, Log);

    public void Move(AppEntry app, int delta)
    {
        var i = Apps.IndexOf(app);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Apps.Count) return;
        Apps.Move(i, j);
    }

    private void UpdateClock()
    {
        SessionDuration = Director.SessionStartedAt is { } start
            ? "Session time " + (DateTime.Now - start).ToString(@"h\:mm\:ss")
            : "";
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
        switch (e.PropertyName)
        {
            case nameof(DirectorSettings.StartWithWindows):
                StartupManager.Apply(Settings.StartWithWindows, Settings.RunAsAdmin, Log);
                break;
            case nameof(DirectorSettings.RunAsAdmin):
                StartupManager.Apply(Settings.StartWithWindows, Settings.RunAsAdmin, Log);
                if (Settings.RunAsAdmin != IsElevated) RestartRequired?.Invoke();
                break;
            case nameof(DirectorSettings.AutoMode):
                Log.Info(Settings.AutoMode ? "Automatic mode on." : "Automatic mode off. Apps won't start or close on their own.");
                break;
        }
    }

    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (AppEntry a in e.NewItems) a.PropertyChanged += OnAppChanged;
        if (e.OldItems is not null) foreach (AppEntry a in e.OldItems) a.PropertyChanged -= OnAppChanged;
        Save();
    }

    private void OnAppChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && PersistedAppProps.Contains(e.PropertyName)) Save();
    }
}
