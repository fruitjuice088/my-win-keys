using System.Text.Json;

namespace MyWinKeys.Core;

public sealed class AppConfig
{
    public bool Debug { get; set; } = false;

    // timings (ms)
    public int HoldThresholdMs { get; set; } = 175;
    public int TapGraceMs { get; set; } = 130;
    public int ComboWindowMs { get; set; } = 50;

    // key codes (virtual-key codes)
    public int VK_Space { get; set; } = 0x20;
    public int VK_Shift { get; set; } = 0x10; // generic shift
    public int VK_LControl { get; set; } = 0xA2;
    public int VK_RControl { get; set; } = 0xA3;
    public int VK_CapsLock { get; set; } = 0x14;
    // Some keyboards report CapsLock as OEM VKs like 0xF0. Add alternates here.
    public int[] AltCapsVks { get; set; } = [0xF0];
    public int VK_Kanji { get; set; } = 0x19; // Hankaku/Zenkaku (IME toggle)
    public int VK_Muhenkan { get; set; } = 0x1D; // Non-Convert
    public int VK_Henkan { get; set; } = 0x1C; // Convert
    public int VK_Back { get; set; } = 0x08; // Backspace
    public int VK_Return { get; set; } = 0x0D; // Enter
    public int VK_Escape { get; set; } = 0x1B; // Esc
    public int VK_Tab { get; set; } = 0x09;
    public int VK_Left { get; set; } = 0x25;
    public int VK_Up { get; set; } = 0x26;
    public int VK_Right { get; set; } = 0x27;
    public int VK_Down { get; set; } = 0x28;
    public int VK_H { get; set; } = 0x48;
    public int VK_L { get; set; } = 0x4C;
    public int VK_D { get; set; } = 0x44;
    public int VK_K { get; set; } = 0x4B;
    public int VK_J { get; set; } = 0x4A;
    public int VK_W { get; set; } = 0x57;
    public int VK_E { get; set; } = 0x45;
    public int VK_Q { get; set; } = 0x51;
    public int VK_U { get; set; } = 0x55;
    public int VK_I { get; set; } = 0x49;
    public int VK_O { get; set; } = 0x4F;
    public int VK_P { get; set; } = 0x50;
    public int VK_1 { get; set; } = 0x31;
    public int VK_2 { get; set; } = 0x32;
    public int VK_3 { get; set; } = 0x33;
    public int VK_4 { get; set; } = 0x34;
    public int VK_A { get; set; } = 0x41;
    public int VK_Z { get; set; } = 0x5A;

    public int VK_C { get; set; } = 0x43;
    public int VK_M { get; set; } = 0x4D;
    public int VK_OEM_COMMA { get; set; } = 0xBC;
    public int VK_OEM_PERIOD { get; set; } = 0xBE;

    public static AppConfig Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            if (cfg != null) return cfg;
        }
        catch { }
        return new AppConfig();
    }
}
