using System.Collections.Concurrent;
using System.Text;

namespace MyWinKeys.Infrastructure;

public static class Logger
{
    private static readonly BlockingCollection<string> _queue = [];
    private static Thread? _thread;
    private static bool _debug;
    private static string _logFile = Path.Combine(AppContext.BaseDirectory, "MyWinKeys.log");

    public static void Initialize(string baseDir, bool debug)
    {
        _debug = debug;
        _logFile = Path.Combine(baseDir, "MyWinKeys.log");
        _thread = new Thread(Worker) { IsBackground = true, Name = "Logger" };
        _thread.Start();
    }

    public static void Flush()
    {
        _queue.CompleteAdding();
        try { _thread?.Join(500); } catch { }
    }

    private static void Enqueue(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        try { _queue.Add(line); } catch { }
    }

    public static void Info(string msg)
    {
        if (_debug) Enqueue("INFO", msg);
    }
    public static void Warn(string msg)
    {
        if (_debug) Enqueue("WARN", msg);
    }
    public static void Error(string msg)
    {
        Enqueue("ERROR", msg);
    }

    private static void Worker()
    {
        try
        {
            using var fs = new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                sw.WriteLine(line);
            }
        }
        catch { }
    }
}
