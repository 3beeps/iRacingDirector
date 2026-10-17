using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using IRacingDirector.Core;
using IRacingDirector.Services;
using Microsoft.Win32;

namespace IRacingDirector.UI;

public sealed class SetupItem : ObservableObject
{
    private bool _selected;

    public SetupItem(AppEntry entry, string note, bool selected, bool needsAdmin)
    {
        Entry = entry;
        Note = note;
        _selected = selected;
        NeedsAdmin = needsAdmin;
    }

    public AppEntry Entry { get; }
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public string Note { get; }
    public bool NeedsAdmin { get; }
    public bool Selected { get => _selected; set => SetField(ref _selected, value); }
}

/// <summary>
/// First-run setup (find apps, pick which to manage, startup options) and, later,
/// "Find apps" from the Apps page, which only offers apps that aren't added yet.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly DirectorSettings _settings;
    private readonly bool _firstRun;
    private readonly ObservableCollection<SetupItem> _items = new();
    private string? _iRacingUi;
    private bool _adminTouched;

    public SetupWindow(DirectorSettings settings, bool firstRun)
    {
        InitializeComponent();
        _settings = settings;
        _firstRun = firstRun;
        ItemsList.ItemsSource = _items;
        SourceInitialized += (_, _) => WindowTheme.UseDarkTitleBar(this);
        AdminBox.Click += (_, _) => _adminTouched = true;

        if (!firstRun)
        {
            Title = "Find apps";
            Heading.Text = "Find apps";
            Intro.Text = "These sim racing apps are on your PC but not in iRacing Director yet. Switch on the ones to add.";
            OptionsCard.Visibility = Visibility.Collapsed;
            IRacingCard.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Cancel";
            FinishButton.Content = "Add selected";
        }

        Loaded += async (_, _) =>
        {
            var programs = await ProgramScanner.GetProgramsAsync(refresh: true);
            _iRacingUi = firstRun ? DefaultApps.FindIRacingUi(programs) : _settings.IRacingUiPath;
            ShowIRacing();

            foreach (var d in DefaultApps.Detect(programs))
            {
                if (AlreadyAdded(d.Path)) continue;
                AddItem(new SetupItem(d.Known.ToEntry(d.Path), "", d.Known.DefaultOn, d.Known.NeedsAdmin));
            }

            if (IRacingManagerImporter.IsAvailable)
            {
                try
                {
                    foreach (var app in IRacingManagerImporter.Read(AppPaths.IRacingManagerSettings))
                    {
                        if (AlreadyAdded(app.Path) || _items.Any(i => SameFile(i.Path, app.Path)) || !app.PathExists) continue;
                        AddItem(new SetupItem(app, "from iRacing Manager", app.StartWithSession, DefaultApps.Lookup(app.Path)?.NeedsAdmin == true));
                    }
                }
                catch { /* Not important enough to bother the user about. */ }
            }

            UpdateListMessage();
        };
    }

    private bool AlreadyAdded(string path) => _settings.Apps.Any(a => SameFile(a.Path, path));

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);

    private void AddItem(SetupItem item)
    {
        item.PropertyChanged += (_, _) => UpdateAdminDefault();
        _items.Add(item);
        UpdateAdminDefault();
        UpdateListMessage();
    }

    /// <summary>Suggest admin mode while an admin-only app is selected, unless the user chose themselves.</summary>
    private void UpdateAdminDefault()
    {
        var needs = _items.Where(i => i.Selected && i.NeedsAdmin).Select(i => i.Name).ToList();
        if (!_adminTouched) AdminBox.IsChecked = needs.Count > 0;
        AdminHint.Text = needs.Count > 0
            ? $"Recommended: {string.Join(", ", needs)} runs as administrator, so Director needs this to close it."
            : "Turn this on if any of your apps run as administrator, or Director can't close them.";
    }

    private void UpdateListMessage()
    {
        ListMessage.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListMessage.Text = _firstRun
            ? "Didn't spot any of the usual sim racing apps. Use \"Add another app\" to pick yours."
            : "Nothing new found. Use \"Add another app\" to pick any program.";
    }

    private void ShowIRacing()
    {
        var found = _iRacingUi is not null && File.Exists(_iRacingUi);
        IRacingIcon.Text = found ? "" : "";
        IRacingIcon.Foreground = (System.Windows.Media.Brush)FindResource(found ? "GoodBrush" : "WarnBrush");
        IRacingTitle.Text = found ? "Found iRacing" : "Couldn't find iRacing";
        IRacingPath.Text = found ? _iRacingUi : "Click Change and pick iRacingUI.exe (in your iRacing folder, under \"ui\").";
    }

    private void BrowseIRacing_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Find iRacingUI.exe", Filter = "iRacing UI|iRacingUI.exe|Programs (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != true) return;
        _iRacingUi = dialog.FileName;
        ShowIRacing();
    }

    private void AddOther_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProgramPickerWindow { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is not { } p) return;
        if (AlreadyAdded(p.Path) || _items.Any(i => SameFile(i.Path, p.Path)))
        {
            var existing = _items.FirstOrDefault(i => SameFile(i.Path, p.Path));
            if (existing is not null) existing.Selected = true;
            return;
        }
        var known = DefaultApps.Lookup(p.Path);
        var entry = known?.ToEntry(p.Path) ?? new AppEntry { Name = p.Name, Path = p.Path };
        AddItem(new SetupItem(entry, "added", true, known?.NeedsAdmin == true));
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items.Where(i => i.Selected))
        {
            item.Entry.StartWithSession = true;
            item.Entry.StopWithSession = true;
            _settings.Apps.Add(item.Entry);
        }

        if (_firstRun)
        {
            if (_iRacingUi is not null) _settings.IRacingUiPath = _iRacingUi;
            _settings.StartWithWindows = StartWithWindowsBox.IsChecked == true;
            _settings.RunAsAdmin = AdminBox.IsChecked == true;
        }
        DialogResult = true;
    }
}
