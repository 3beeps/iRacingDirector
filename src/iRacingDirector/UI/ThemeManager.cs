using System;
using System.Linq;
using System.Windows;

namespace IRacingDirector.UI;

/// <summary>Swaps the color dictionary (Themes/Colors.&lt;name&gt;.xaml) at runtime.</summary>
public static class ThemeManager
{
    public static readonly string[] Themes = { "Dark", "Pride" };

    public static string Normalize(string? name) =>
        Themes.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    public static string Next(string current) =>
        Themes[(Array.IndexOf(Themes, Normalize(current)) + 1) % Themes.Length];

    public static void Apply(string name)
    {
        var colors = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Colors.{Normalize(name)}.xaml", UriKind.Absolute),
        };
        // App.xaml keeps the color dictionary first.
        Application.Current.Resources.MergedDictionaries[0] = colors;
    }
}
