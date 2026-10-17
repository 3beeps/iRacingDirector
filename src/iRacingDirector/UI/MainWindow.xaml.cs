using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using IRacingDirector.Core;
using IRacingDirector.Services;
using Microsoft.Win32;

namespace IRacingDirector.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _trayHintShown;

    /// <summary>Set when the app is really exiting (otherwise closing hides to tray).</summary>
    public bool AllowClose { get; set; }

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        SourceInitialized += (_, _) => WindowTheme.UseDarkTitleBar(this);
        _vm.Apps.CollectionChanged += (_, _) => UpdateEmptyState();
        _vm.RestartRequired += OnRestartRequired;
        UpdateEmptyState();
    }

    private void UpdateEmptyState() =>
        EmptyState.Visibility = _vm.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose && _vm.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _vm.Log.Info("Director is still running in the tray. Right-click the tray icon to exit.");
                ((App)Application.Current).ShowTrayHint();
            }
            return;
        }
        if (!AllowClose)
        {
            e.Cancel = true;
            ((App)Application.Current).ExitApp();
            return;
        }
        base.OnClosing(e);
    }

    // ---- Header ----

    private void LaunchIRacing_Click(object sender, RoutedEventArgs e) => _vm.LaunchIRacing();
    private async void StartApps_Click(object sender, RoutedEventArgs e) => await _vm.StartAppsAsync();
    private async void CloseApps_Click(object sender, RoutedEventArgs e) => await _vm.CloseAppsAsync();

    private async void UpdateBanner_Click(object sender, RoutedEventArgs e) => await _vm.InstallUpdateAsync();

    // ---- Apps ----

    private static AppEntry AppOf(object sender) => (AppEntry)((FrameworkElement)sender).DataContext;

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProgramPickerWindow { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is not { } program) return;

        // Known apps come with the right settings (e.g. Garage 61's agent process).
        var app = DefaultApps.Lookup(program.Path)?.ToEntry(program.Path)
                  ?? new AppEntry { Name = program.Name, Path = program.Path };
        var editor = new AppEditorWindow(app) { Owner = this, Title = "Add app" };
        if (editor.ShowDialog() == true) _vm.Apps.Add(app);
    }

    private void FindApps_Click(object sender, RoutedEventArgs e)
    {
        var before = _vm.Apps.Count;
        if (new SetupWindow(_vm.Settings, firstRun: false) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog() == true)
            _vm.Log.Info($"Added {_vm.Apps.Count - before} app(s).");
    }

    private void EditApp_Click(object sender, RoutedEventArgs e)
    {
        var app = AppOf(sender);
        var copy = app.Clone();
        var editor = new AppEditorWindow(copy) { Owner = this };
        if (editor.ShowDialog() == true) app.CopyFrom(copy);
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        var app = AppOf(sender);
        var answer = MessageBox.Show(this, $"Remove {app.Name} from iRacing Director?\n\nThis doesn't uninstall the app.",
            "Remove app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) _vm.Apps.Remove(app);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => _vm.Move(AppOf(sender), -1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => _vm.Move(AppOf(sender), +1);
    private async void LaunchApp_Click(object sender, RoutedEventArgs e) => await _vm.Director.LaunchAppAsync(AppOf(sender));
    private async void CloseApp_Click(object sender, RoutedEventArgs e) => await _vm.Director.CloseAppAsync(AppOf(sender));

    // ---- Activity / Settings ----

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });

    private void BrowseUi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Find iRacingUI.exe", Filter = "iRacing UI|iRacingUI.exe|Programs (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true) _vm.Settings.IRacingUiPath = dialog.FileName;
    }

    private void OnRestartRequired()
    {
        var wantAdmin = _vm.Settings.RunAsAdmin;
        var text = wantAdmin
            ? "Restart iRacing Director as administrator now?"
            : "Restart iRacing Director without administrator rights now?";
        if (MessageBox.Show(this, text, "Restart needed", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!((App)Application.Current).Restart(elevated: wantAdmin))
            _vm.Log.Warn($"Restart didn't happen ({Elevation.LastError}).");
    }
}
