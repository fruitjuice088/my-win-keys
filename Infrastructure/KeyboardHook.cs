using MyWinKeys.Core;
using static MyWinKeys.Infrastructure.Win32;
using System.Runtime.InteropServices;

namespace MyWinKeys.Infrastructure;

internal sealed class KeyboardHook : IDisposable
{
    private readonly RemapEngine _engine;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile bool _running;

    public KeyboardHook(RemapEngine engine)
    {
        _engine = engine;
    }

    public void Install()
    {
        if (_thread != null) return;
        _running = true;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "KBHook" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(3)))
        {
            throw new InvalidOperationException("Keyboard hook thread failed to initialize.");
        }
    }

    public void Dispose()
    {
        try
        {
            _running = false;
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            }
            _thread?.Join(1000);
        }
        catch { }
        finally
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _thread = null;
            _threadId = 0;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            // Ignore only our injected events using marker
            if (InputSender.IsOurInjected(data.dwExtraInfo))
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            if (isDown || isUp)
            {
                bool suppress = _engine.ProcessEvent((int)data.vkCode, isDown);
                if (suppress)
                {
                    return 1; // eat
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void HookThread()
    {
        try
        {
            _proc = HookCallback;
            IntPtr hMod = GetModuleHandle(null);
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
            if (_hookId == IntPtr.Zero)
            {
                _ready.Set();
                return;
            }
            _threadId = GetCurrentThreadId();
            _ready.Set();

            // Message loop
            while (_running)
            {
                if (!GetMessage(out var msg, IntPtr.Zero, 0, 0)) break;
                if (msg.message == WM_QUIT) break;
            }
        }
        catch { }
        finally
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
}
