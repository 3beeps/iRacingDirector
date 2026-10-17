using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using IRacingDirector.Services;
using Forms = System.Windows.Forms;

namespace IRacingDirector.UI;

/// <summary>Notification-area icon with quick actions.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _autoItem;
    private bool _disposed;

    public TrayIcon(MainViewModel vm)
    {
        _vm = vm;

        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("Open iRacing Director", null, (_, _) => App.ShowMain());
        open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Launch iRacing", null, (_, _) => _vm.LaunchIRacing());
        menu.Items.Add("Start apps now", null, async (_, _) => await _vm.StartAppsAsync());
        menu.Items.Add("Close apps now", null, async (_, _) => await _vm.CloseAppsAsync());
        menu.Items.Add("Close iRacing and apps", null, async (_, _) => await _vm.CloseEverythingAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _autoItem = new Forms.ToolStripMenuItem("Automatic mode", null, (_, _) => _vm.Settings.AutoMode = !_vm.Settings.AutoMode)
        {
            Checked = _vm.Settings.AutoMode,
        };
        menu.Items.Add(_autoItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ((App)Application.Current).ExitApp());

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) App.ShowMain(); };

        _vm.Settings.PropertyChanged += OnChanged;
        _vm.Director.PropertyChanged += OnChanged;
        UpdateText();
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.None);

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        _autoItem.Checked = _vm.Settings.AutoMode;
        UpdateText();
    }

    private void UpdateText()
    {
        var text = $"iRacing Director: {_vm.Director.StatusText}";
        if (!_vm.Settings.AutoMode) text = "iRacing Director (auto mode off)";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    private static Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/director.ico"));
        using var stream = info.Stream;
        return new Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vm.Settings.PropertyChanged -= OnChanged;
        _vm.Director.PropertyChanged -= OnChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
