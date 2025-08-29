using static MyWinKeys.Infrastructure.Win32;
using System.Runtime.InteropServices;

namespace MyWinKeys.Infrastructure;

internal static class InputSender
{
    // Mark injected events with a unique pointer so our hook can ignore only ours
    private static readonly IntPtr Marker = new(0xBEEF1234);
    public static void KeyDown(int vk)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = Marker }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public static void KeyUp(int vk)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = Marker }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public static void Tap(int vk)
    {
        KeyDown(vk);
        KeyUp(vk);
    }

    public static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            short vk = VkKeyScan(ch);
            int vkCode = vk & 0xFF;
            int shift = (vk >> 8) & 1;
            if (shift == 1) KeyDown(0x10);
            Tap(vkCode);
            if (shift == 1) KeyUp(0x10);
        }
    }

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    public static bool IsOurInjected(IntPtr extra) => extra == Marker;

    public static void KeyDown_AllowRepeat(int vk)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void KeyUp_AllowRepeat(int vk)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
