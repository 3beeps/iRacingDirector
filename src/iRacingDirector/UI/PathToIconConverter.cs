using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IRacingDirector.UI;

/// <summary>Turns an executable path into its icon (cached).</summary>
public sealed class PathToIconConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string path && path.Length > 0 ? Cache.GetOrAdd(path, Load) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static ImageSource? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
