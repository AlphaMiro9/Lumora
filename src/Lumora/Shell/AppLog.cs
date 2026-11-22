using System.Text;

namespace Lumora.Shell;

/// <summary>
/// Lumoras eigenes Protokoll (Textdatei im lokalen Anwendungsordner). Es wird bei ca. 512 KB automatisch
/// rotiert (lumora.log, lumora.log.1, lumora.log.2). Es werden keine Suchbegriffe oder Adressen mit persönlichen Daten
/// protokolliert, nur Ereignisse, Fehler und Video-IDs. Logging darf die App nie zum Absturz bringen.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _dir;
    private static long _maxBytes = 512 * 1024;
    private const int Keep = 2;

    public static string? Folder { get { lock (Gate) return _dir; } }

    public static void Init(string folder, long maxBytes = 512 * 1024)
    {
        lock (Gate)
        {
            _maxBytes = Math.Max(1024, maxBytes);
            try { Directory.CreateDirectory(folder); _dir = folder; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _dir = null; }
        }
    }

    /// <summary>Schaltet das Protokoll ab (für Tests).</summary>
    public static void Shutdown() { lock (Gate) _dir = null; }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public static string Format(DateTime utcNow, string level, string message, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.Append(utcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'")).Append(' ').Append(level.PadRight(5)).Append(' ')
          .Append(message.ReplaceLineEndings(" "));
        if (ex != null) sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message.ReplaceLineEndings(" "));
        return sb.Append(Environment.NewLine).ToString();
    }

    private static void Write(string level, string message, Exception? ex)
    {
        lock (Gate)
        {
            if (_dir is null) return;
            try
            {
                var file = Path.Combine(_dir, "lumora.log");
                Rotate(file);
                File.AppendAllText(file, Format(DateTime.UtcNow, level, message, ex), Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Protokoll ist optional */ }
        }
    }

    private static void Rotate(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length < _maxBytes) return;
        for (var i = Keep; i >= 1; i--)
        {
            var from = i == 1 ? file : file + "." + (i - 1);
            var to = file + "." + i;
            if (!File.Exists(from)) continue;
            File.Move(from, to, overwrite: true);
        }
    }
}
