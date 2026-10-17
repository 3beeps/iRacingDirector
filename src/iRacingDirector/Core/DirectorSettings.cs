using System;
using System.Collections.ObjectModel;

namespace IRacingDirector.Core;

public sealed class DirectorSettings : ObservableObject
{
    /// <summary>Matches iRacingSim64DX11.exe today and any renamed sim exe (e.g. a new graphics API) later.</summary>
    public const string DefaultSimProcess = "iRacingSim64*";
    public const string LegacySimProcess = "iRacingSim64DX11";
    public const string DefaultIRacingUiPath = @"C:\Program Files (x86)\iRacing\ui\iRacingUI.exe";

    private bool _autoMode = true;
    private bool _startWithWindows;
    private bool _startMinimizedToTray = true;
    private bool _closeToTray = true;
    private bool _runAsAdmin;
    private bool _onlyCloseAppsDirectorStarted;
    private bool _checkForUpdates = true;
    private int _sessionEndDelaySeconds = 15;
    private int _closeGraceSeconds = 5;
    private string _simProcessName = DefaultSimProcess;
    private string _iRacingUiPath = DefaultIRacingUiPath;

    public int SettingsVersion { get; set; } = 1;

    public ObservableCollection<AppEntry> Apps { get; set; } = new();

    /// <summary>Automatically start/stop apps when the sim starts/exits.</summary>
    public bool AutoMode { get => _autoMode; set => SetField(ref _autoMode, value); }

    public bool StartWithWindows { get => _startWithWindows; set => SetField(ref _startWithWindows, value); }
    public bool StartMinimizedToTray { get => _startMinimizedToTray; set => SetField(ref _startMinimizedToTray, value); }
    public bool CloseToTray { get => _closeToTray; set => SetField(ref _closeToTray, value); }
    public bool RunAsAdmin { get => _runAsAdmin; set => SetField(ref _runAsAdmin, value); }
    public bool OnlyCloseAppsDirectorStarted { get => _onlyCloseAppsDirectorStarted; set => SetField(ref _onlyCloseAppsDirectorStarted, value); }

    /// <summary>Look for a newer release on GitHub at startup and once a day.</summary>
    public bool CheckForUpdates { get => _checkForUpdates; set => SetField(ref _checkForUpdates, value); }

    /// <summary>
    /// How long the sim must stay closed before apps are shut down. Prevents app churn
    /// when hopping from one session straight into the next.
    /// </summary>
    public int SessionEndDelaySeconds { get => _sessionEndDelaySeconds; set => SetField(ref _sessionEndDelaySeconds, Math.Clamp(value, 0, 3600)); }

    /// <summary>Seconds to wait for an app to close politely before it is force-closed.</summary>
    public int CloseGraceSeconds { get => _closeGraceSeconds; set => SetField(ref _closeGraceSeconds, Math.Clamp(value, 0, 120)); }

    public string SimProcessName { get => _simProcessName; set => SetField(ref _simProcessName, value); }
    public string IRacingUiPath { get => _iRacingUiPath; set => SetField(ref _iRacingUiPath, value); }
}
