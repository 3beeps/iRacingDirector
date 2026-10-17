using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IRacingDirector.UI;

internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void UseDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var on = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
    }
}
