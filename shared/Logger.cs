using System.Text;

namespace BloxCode.Core;

public static class Logger
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            FilePath = Path.Combine(Paths.Logs, $"BloxCode_{DateTime.Now:yyyyMMdd'T'HHmmss}_{Environment.ProcessId}.log");
            _writer = new StreamWriter(FilePath, append: false, Encoding.UTF8) { AutoFlush = true };
            Prune();
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public static void Info(string ident, string message) => Write("INFO", ident, message);
    public static void Warn(string ident, string message) => Write("WARN", ident, message);
    public static void Error(string ident, Exception ex) => Write("ERROR", ident, ex.ToString());

    /// <summary>
    /// A roblox-player: link carries a one-time login ticket (gameinfo:…); whoever reads a log someone
    /// shared for support could sign in with it, so it never reaches the file.
    /// </summary>
    public static string Mask(string message) =>
        message.Contains("gameinfo:", StringComparison.OrdinalIgnoreCase)
            ? System.Text.RegularExpressions.Regex.Replace(message, @"(?i)(gameinfo:)[^+\s""']+", "$1***")
            : message;

    private static void Write(string level, string ident, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{ident}] {Mask(message)}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (Sync)
        {
            try { _writer?.WriteLine(line); } catch { }
        }
    }

    private static void Prune()
    {
        var threshold = DateTime.Now.AddDays(-7);
        foreach (var file in Directory.EnumerateFiles(Paths.Logs, "*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < threshold)
                    File.Delete(file);
            }
            catch { }
        }
    }
}
