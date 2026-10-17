using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRacingDirector.Services;
using Microsoft.Win32;

namespace IRacingDirector.UI;

/// <summary>Lets the user pick a program from the Start Menu (or browse for any file).</summary>
public partial class ProgramPickerWindow : Window
{
    private IReadOnlyList<InstalledProgram> _all = Array.Empty<InstalledProgram>();

    public InstalledProgram? Result { get; private set; }

    public ProgramPickerWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheme.UseDarkTitleBar(this);
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            _all = await ProgramScanner.GetProgramsAsync();
            LoadingText.Visibility = Visibility.Collapsed;
            ApplyFilter();
        };
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text.Trim();
        List.ItemsSource = q.Length == 0
            ? _all
            : _all.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                              p.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AddButton.IsEnabled = List.SelectedItem is not null;

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is InstalledProgram) Add_Click(sender, e);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not InstalledProgram p) return;
        Result = p;
        DialogResult = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the app to add", Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(dialog.FileName);
        var name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription! : System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        Result = new InstalledProgram(name, dialog.FileName);
        DialogResult = true;
    }
}
