using Windows.Storage;

namespace JonoWidgets;

/// <summary>A small log file in the app's own data folder (LocalState\log.txt), for debugging.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 256 * 1024;

    public static string? FilePath
    {
        get
        {
            try { return Path.Combine(ApplicationData.Current.LocalFolder.Path, "log.txt"); }
            catch { return null; }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? e) => Write("ERROR", e == null ? message : $"{message}: {e}");

    private static void Write(string level, string message)
    {
        try
        {
            var path = FilePath;
            if (path == null) return;
            lock (Gate)
            {
                var f = new FileInfo(path);
                if (f.Exists && f.Length > MaxBytes)
                {
                    var keep = File.ReadAllLines(path);
                    File.WriteAllLines(path, keep.Skip(keep.Length / 2));
                }
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break a widget.
        }
    }
}
