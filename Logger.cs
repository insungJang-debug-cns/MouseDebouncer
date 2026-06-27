namespace MouseDebouncer;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "debounce_log.txt");

    private static readonly object _lock = new();

    public static bool IsEnabled { get; set; } = false;

    public static void Write(string message)
    {
        if (!IsEnabled) return;
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (_lock)
            File.AppendAllText(LogPath, line + Environment.NewLine);
    }

    public static void Clear()
    {
        lock (_lock)
            if (File.Exists(LogPath))
                File.Delete(LogPath);
    }

    public static string LogFilePath => LogPath;
}
