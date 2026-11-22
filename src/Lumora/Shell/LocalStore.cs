using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumora.Shell;

public sealed class VideoEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AppSettings
{
    public bool HideShorts { get; set; } = true;
    public bool HideComments { get; set; }
    public bool FocusMode { get; set; }
    /// <summary>Videos im eigenen Lumora-Player statt auf der YouTube-Seite abspielen.</summary>
    public bool OwnPlayer { get; set; } = true;
    /// <summary>Sponsor-Segmente (SponsorBlock) im eigenen Player überspringen.</summary>
    public bool SponsorBlock { get; set; } = true;
    /// <summary>"auto", "light" oder "dark"</summary>
    public string ColorScheme { get; set; } = "auto";
    public string Accent { get; set; } = LocalStore.DefaultAccent;
}

public sealed class StoreData
{
    public List<VideoEntry> Recent { get; set; } = new();
    public List<VideoEntry> Favorites { get; set; } = new();
    public List<string> Searches { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

/// <summary>Eigene Daten (zuletzt gesehen, Favoriten, Suchverlauf, Einstellungen) – nur lokal in einer JSON-Datei.</summary>
public sealed class LocalStore
{
    public const string DefaultAccent = "#0B7A87";
    private const int MaxRecent = 40, MaxSearches = 12, MaxTitle = 200;
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private StoreData _data = new();

    public LocalStore(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
                _data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(path)) ?? new StoreData();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _data = new StoreData(); // defekte Datei: sauber neu beginnen
        }
        Sanitize();
    }

    public IReadOnlyList<VideoEntry> Recent => _data.Recent.ToList();
    public IReadOnlyList<VideoEntry> Favorites => _data.Favorites.ToList();
    public IReadOnlyList<string> Searches => _data.Searches.ToList();
    public AppSettings Settings => _data.Settings;

    public bool IsFavorite(string id) => _data.Favorites.Any(v => v.Id == id);

    public void AddRecent(string id, string? title)
    {
        if (!YouTubeUrls.IsValidVideoId(id)) return;
        var clean = Trim(YouTubeUrls.CleanTitle(title));
        var existing = _data.Recent.FirstOrDefault(v => v.Id == id);
        if (existing != null) { _data.Recent.Remove(existing); if (clean.Length == 0) clean = existing.Title; }
        _data.Recent.Insert(0, new VideoEntry { Id = id, Title = clean.Length > 0 ? clean : id, AtUtc = DateTime.UtcNow });
        if (_data.Recent.Count > MaxRecent) _data.Recent.RemoveRange(MaxRecent, _data.Recent.Count - MaxRecent);
        Save();
    }

    /// <summary>Aktualisiert den Titel, wenn die Seite ihn erst nach dem Laden liefert.</summary>
    public void UpdateTitle(string id, string? title)
    {
        var clean = Trim(YouTubeUrls.CleanTitle(title));
        if (clean.Length == 0) return;
        var changed = false;
        foreach (var v in _data.Recent.Concat(_data.Favorites).Where(v => v.Id == id && v.Title != clean)) { v.Title = clean; changed = true; }
        if (changed) Save();
    }

    public bool ToggleFavorite(string id, string? title)
    {
        if (!YouTubeUrls.IsValidVideoId(id)) return false;
        var existing = _data.Favorites.FirstOrDefault(v => v.Id == id);
        if (existing != null) { _data.Favorites.Remove(existing); Save(); return false; }
        var clean = Trim(YouTubeUrls.CleanTitle(title));
        _data.Favorites.Insert(0, new VideoEntry { Id = id, Title = clean.Length > 0 ? clean : id });
        Save();
        return true;
    }

    public void RemoveFavorite(string id) { _data.Favorites.RemoveAll(v => v.Id == id); Save(); }
    public void ClearRecent() { _data.Recent.Clear(); Save(); }

    public void AddSearch(string? term)
    {
        term = (term ?? "").Trim();
        if (term.Length == 0 || term.Length > 200) return;
        _data.Searches.RemoveAll(s => string.Equals(s, term, StringComparison.OrdinalIgnoreCase));
        _data.Searches.Insert(0, term);
        if (_data.Searches.Count > MaxSearches) _data.Searches.RemoveRange(MaxSearches, _data.Searches.Count - MaxSearches);
        Save();
    }

    public void ClearSearches() { _data.Searches.Clear(); Save(); }

    public void SaveSettings() { Sanitize(); Save(); }

    private static string Trim(string s) => s.Length > MaxTitle ? s[..MaxTitle] : s;

    private void Sanitize()
    {
        _data.Settings ??= new AppSettings();
        if (!HexColor.IsMatch(_data.Settings.Accent ?? "")) _data.Settings.Accent = DefaultAccent;
        if (_data.Settings.ColorScheme is not ("auto" or "light" or "dark")) _data.Settings.ColorScheme = "auto";
        _data.Recent = (_data.Recent ?? new()).Where(v => YouTubeUrls.IsValidVideoId(v.Id)).ToList();
        _data.Favorites = (_data.Favorites ?? new()).Where(v => YouTubeUrls.IsValidVideoId(v.Id)).ToList();
        _data.Searches ??= new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Daten bleiben im Speicher */ }
    }
}
