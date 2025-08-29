using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MyWinKeys.Core;

namespace MyWinKeys.Infrastructure;

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly RemapEngine _engine;
    private readonly KeyboardHook _hook;
    private readonly System.Threading.Timer _timer;
    private readonly string _baseDir;

    public TrayContext(string baseDir, AppConfig config)
    {
        _baseDir = baseDir;

        // Initialize engine + hook
        _engine = new RemapEngine(config);
        _hook = new KeyboardHook(_engine);

        try
        {
            _hook.Install();
            Logger.Info("Keyboard hook installed.");
        }
        catch (Exception ex)
        {
            Logger.Error("Hook install failed: " + ex);
            throw;
        }

    _timer = new System.Threading.Timer(_ => _engine.Tick(), null, 5, 5);

        var menu = new ContextMenuStrip();

        var openConfigItem = new ToolStripMenuItem("Open config folder");
        openConfigItem.Click += (_, __) => OpenConfigFolder();
        menu.Items.Add(openConfigItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, __) => ExitApp();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "MyWinKeys",
            ContextMenuStrip = menu
        };
    }

    private void OpenConfigFolder()
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = _baseDir,
                UseShellExecute = true
            };
            p.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open config folder: " + ex);
            ShowBalloon("Failed to open config folder.");
        }
    }

    private void ExitApp()
    {
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        try
        {
            _tray.Visible = false;
            _tray.Dispose();
            _hook.Dispose();
            _timer.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error("Shutdown error: " + ex);
        }
        finally
        {
            Logger.Info("MyWinKeys exiting.");
            Logger.Flush();
        }
        base.ExitThreadCore();
    }

    private void ShowBalloon(string message)
    {
        try
        {
            _tray.BalloonTipTitle = "MyWinKeys";
            _tray.BalloonTipText = message;
            _tray.ShowBalloonTip(3000);
        }
        catch { }
    }
}
