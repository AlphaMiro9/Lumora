using System.Text.RegularExpressions;

namespace Lumora.Shell;

/// <summary>Adressen und Regeln, welche Seiten im Lumora-Fenster geöffnet werden dürfen.</summary>
public static class YouTubeUrls
{
    public const string Home = "https://www.youtube.com/";
    public const string Subscriptions = "https://www.youtube.com/feed/subscriptions";
    public const string Library = "https://www.youtube.com/feed/library";

    public const string PlayerHost = "lumora.local";

    private static readonly string[] AllowedDomains = { "youtube.com", "youtube-nocookie.com", "google.com" };

    /// <summary>Adresse der normalen YouTube-Suche. Leere Eingaben führen zur Startseite.</summary>
    public static string Search(string? query)
        => string.IsNullOrWhiteSpace(query)
            ? Home
            : "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query.Trim());

    /// <summary>Nur https-Seiten von YouTube und der Google-Anmeldung bleiben im Fenster; alles andere öffnet der Standardbrowser.</summary>
    public static bool IsInApp(Uri? uri)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.Host.ToLowerInvariant();
        return AllowedDomains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>Nur sichere https-Adressen dürfen extern geöffnet werden (keine file:-, ms-:- oder anderen Schemata).</summary>
    public static bool IsSafeExternal(Uri? uri) => uri is { Scheme: "https" or "http" } && !string.IsNullOrEmpty(uri.Host);

    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    public static bool IsValidVideoId(string? id) => id is not null && IdPattern.IsMatch(id);

    /// <summary>Liest die Video-ID aus einer Adresse wie https://www.youtube.com/watch?v=ID (sonst null).</summary>
    public static string? TryGetVideoId(Uri? uri)
    {
        if (uri is null || !IsInApp(uri) || !uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase)) return null;
        if (!uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "v" && IsValidVideoId(kv[1])) return kv[1];
        }
        return null;
    }

    /// <summary>Adresse der eigenen Player-Seite (liegt lokal, wird über einen virtuellen Host ausgeliefert).</summary>
    public static string PlayerUrl(string id, bool sponsorBlock)
        => "https://" + PlayerHost + "/player.html?v=" + id + "&sb=" + (sponsorBlock ? "1" : "0");

    public static bool IsPlayerPage(Uri? uri)
        => uri is not null && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals(PlayerHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>Video-ID aus einer Adresse der eigenen Player-Seite (sonst null).</summary>
    public static string? TryGetPlayerVideoId(Uri? uri)
    {
        if (!IsPlayerPage(uri)) return null;
        foreach (var part in uri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "v" && IsValidVideoId(kv[1])) return kv[1];
        }
        return null;
    }

    public static string WatchUrl(string id) => "https://www.youtube.com/watch?v=" + id;
    public static string ThumbnailUrl(string id) => "https://i.ytimg.com/vi/" + id + "/mqdefault.jpg";

    /// <summary>Entfernt " - YouTube" und die Benachrichtigungszahl "(3) " aus dem Seitentitel.</summary>
    public static string CleanTitle(string? title)
    {
        var t = (title ?? "").Trim();
        t = Regex.Replace(t, @"^\(\d+\)\s*", "");
        if (t.EndsWith(" - YouTube", StringComparison.Ordinal)) t = t[..^10];
        return t.Trim();
    }
}
