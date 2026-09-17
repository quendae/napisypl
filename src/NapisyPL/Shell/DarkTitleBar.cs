using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace NapisyPL.Shell;

/// <summary>
/// Windows draws the title bar itself and keeps it light unless the window asks for
/// the dark variant, which left a white strip above the dark UI.
/// </summary>
public static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return;

        void Set()
        {
            if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero)
                return;
            var enabled = 1;
            var attribute = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985)
                ? UseImmersiveDarkMode
                : UseImmersiveDarkModeBefore20H1;
            _ = DwmSetWindowAttribute(handle, attribute, ref enabled, sizeof(int));
        }

        if (window.TryGetPlatformHandle() is not null)
            Set();
        window.Opened += (_, _) => Set();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
