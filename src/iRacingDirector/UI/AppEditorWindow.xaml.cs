using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRacingDirector.Core;
using Microsoft.Win32;

namespace IRacingDirector.UI;

public partial class AppEditorWindow : Window
{
    private readonly AppEntry _app;

    public AppEditorWindow(AppEntry app)
    {
        InitializeComponent();
        _app = app;
        DataContext = app;
        SourceInitialized += (_, _) => WindowTheme.UseDarkTitleBar(this);
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*", FileName = _app.Path };
        if (dialog.ShowDialog(this) == true) _app.Path = dialog.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Commit a number box that still has focus.
        if (Keyboard.FocusedElement is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        if (string.IsNullOrWhiteSpace(_app.Name) || string.IsNullOrWhiteSpace(_app.Path))
        {
            MessageBox.Show(this, "Give the app a name and choose its program.", "Almost there", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
