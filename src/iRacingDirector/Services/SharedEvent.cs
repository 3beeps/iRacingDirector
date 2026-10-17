using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace IRacingDirector.Services;

/// <summary>
/// Named events that any copy of Director can signal, whether or not it runs as administrator.
/// A normal named event created by an elevated process can't be opened by a non-elevated one.
/// </summary>
internal static class SharedEvent
{
    // Everyone: all access. Mandatory label: low integrity, no write-up restriction.
    private const string Sddl = "D:(A;;GA;;;WD)S:(ML;;NW;;;LW)";

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public bool InheritHandle;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr sd, IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateEventW(ref SecurityAttributes sa, bool manualReset, bool initialState, string name);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    public static EventWaitHandle Create(string name)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(Sddl, 1, out var sd, IntPtr.Zero))
            throw new Win32Exception();
        try
        {
            var sa = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), SecurityDescriptor = sd };
            var handle = CreateEventW(ref sa, false, false, name);
            if (handle.IsInvalid) throw new Win32Exception();
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset);
            ev.SafeWaitHandle.Dispose();
            ev.SafeWaitHandle = handle;
            return ev;
        }
        finally
        {
            LocalFree(sd);
        }
    }

    /// <summary>Signals an event created by another copy. Returns false if there's none.</summary>
    public static bool TrySignal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            return ev.Set();
        }
        catch
        {
            return false;
        }
    }
}
