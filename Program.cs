using MyWinKeys.Core;
using MyWinKeys.Infrastructure;
using System.Windows.Forms;

namespace MyWinKeys;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Resolve the physical directory of the executable (works for single-file too)
        var exePath = Environment.ProcessPath
                     ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                     ?? AppContext.BaseDirectory;
        var exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        var configPath = Path.Combine(exeDir, "appsettings.json");
        var config = AppConfig.Load(configPath);

        Logger.Initialize(exeDir, config.Debug);
        Logger.Info($"MyWinKeys starting. Debug={(config.Debug ? "on" : "off")}");

        try
        {
            using var ctx = new TrayContext(exeDir, config);
            Application.Run(ctx);
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error: " + ex);
        }
    }
}
