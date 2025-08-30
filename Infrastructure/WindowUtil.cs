namespace MyWinKeys.Infrastructure;

using System;

internal static class WindowUtil
{
    private static void MoveCursor(Func<Win32.RECT, (int x, int y)> getCoordinates)
    {
        var h = Win32.GetForegroundWindow();
        if (h == IntPtr.Zero) return;
        if (Win32.GetWindowRect(h, out var r))
        {
            var (x, y) = getCoordinates(r);
            Win32.SetCursorPos(x, y);
        }
    }

    // Corners
    public static void MoveCursorLeftTop() => MoveCursor(r => (r.Left + 2, r.Top + 2));
    public static void MoveCursorRightTop() => MoveCursor(r => (r.Right - 2, r.Top + 2));
    public static void MoveCursorLeftBottom() => MoveCursor(r => (r.Left + 2, r.Bottom - 2));
    public static void MoveCursorRightBottom() => MoveCursor(r => (r.Right - 2, r.Bottom - 2));

    // Special positions
    public static void MoveCursorTitleCenter() => MoveCursor(r => ((r.Left + r.Right) / 2, r.Top + 15));
    public static void MoveCursorWindowCenter() => MoveCursor(r => ((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2));
}
