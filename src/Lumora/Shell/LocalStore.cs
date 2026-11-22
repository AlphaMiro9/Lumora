using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

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
    /// <summary>Beim Start nach einer neuen Lumora-Version suchen.</summary>
    public bool AutoUpdate { get; set; } = true;
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

/// <summary>
/// Lumoras lokale Datenbank (SQLite): zuletzt gesehen, Favoriten, Suchverlauf, Einstellungen. Nur auf diesem PC.
/// Eine ältere lumora-data.json wird beim ersten Start übernommen. Eine defekte Datenbank wird beiseitegelegt.
/// </summary>
public sealed class LocalStore
{
    public const string DefaultAccent = "#0B7A87";
    private const int MaxRecent = 40, MaxSearches = 12, MaxTitle = 200;
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private readonly string _path;
    private StoreData _data = new();

    public LocalStore(string path, string? legacyJsonPath = null)
    {
        _path = path;
        var fresh = !File.Exists(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var c = Open();
            EnsureSchema(c);
            if (!fresh) _data = Load(c);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Datenbank nicht lesbar, es wird eine neue angelegt", ex);
            BackupBroken();
            _data = new StoreData();
            fresh = true;
        }
        if (fresh) ImportLegacy(legacyJsonPath);
        Sanitize();
        Save();
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

    // ---------- Datenbank ----------

    private SqliteConnection Open()
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        var c = new SqliteConnection(cs);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureSchema(SqliteConnection c)
    {
        Exec(c, null, "CREATE TABLE IF NOT EXISTS videos(kind TEXT NOT NULL, pos INTEGER NOT NULL, id TEXT NOT NULL, title TEXT NOT NULL, at TEXT NOT NULL, PRIMARY KEY(kind, pos))");
        Exec(c, null, "CREATE TABLE IF NOT EXISTS searches(pos INTEGER PRIMARY KEY, term TEXT NOT NULL)");
        Exec(c, null, "CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
    }

    private static StoreData Load(SqliteConnection c)
    {
        var data = new StoreData();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT kind, id, title, at FROM videos ORDER BY kind, pos";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var at = DateTime.TryParse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.UtcNow;
                var entry = new VideoEntry { Id = r.GetString(1), Title = r.GetString(2), AtUtc = at };
                (r.GetString(0) == "fav" ? data.Favorites : data.Recent).Add(entry);
            }
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT term FROM searches ORDER BY pos";
            using var r = cmd.ExecuteReader();
            while (r.Read()) data.Searches.Add(r.GetString(0));
        }
        var kv = new Dictionary<string, string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM settings";
            using var r = cmd.ExecuteReader();
            while (r.Read()) kv[r.GetString(0)] = r.GetString(1);
        }
        var d0 = new AppSettings();
        bool B(string key, bool def) => kv.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : def;
        data.Settings = new AppSettings
        {
            HideShorts = B("HideShorts", d0.HideShorts), HideComments = B("HideComments", d0.HideComments), FocusMode = B("FocusMode", d0.FocusMode),
            OwnPlayer = B("OwnPlayer", d0.OwnPlayer), SponsorBlock = B("SponsorBlock", d0.SponsorBlock), AutoUpdate = B("AutoUpdate", d0.AutoUpdate),
            ColorScheme = kv.GetValueOrDefault("ColorScheme", d0.ColorScheme), Accent = kv.GetValueOrDefault("Accent", d0.Accent),
        };
        return data;
    }

    private void Save()
    {
        try
        {
            using var c = Open();
            EnsureSchema(c);
            using var tx = c.BeginTransaction();
            Exec(c, tx, "DELETE FROM videos");
            Exec(c, tx, "DELETE FROM searches");
            Exec(c, tx, "DELETE FROM settings");
            void Videos(string kind, List<VideoEntry> list)
            {
                for (var i = 0; i < list.Count; i++)
                    Exec(c, tx, "INSERT INTO videos(kind,pos,id,title,at) VALUES($k,$p,$i,$t,$a)",
                        ("$k", kind), ("$p", i), ("$i", list[i].Id), ("$t", list[i].Title), ("$a", list[i].AtUtc.ToString("o", CultureInfo.InvariantCulture)));
            }
            Videos("recent", _data.Recent);
            Videos("fav", _data.Favorites);
            for (var i = 0; i < _data.Searches.Count; i++) Exec(c, tx, "INSERT INTO searches(pos,term) VALUES($p,$t)", ("$p", i), ("$t", _data.Searches[i]));
            var s = _data.Settings;
            void Set(string k, object v) => Exec(c, tx, "INSERT INTO settings(key,value) VALUES($k,$v)", ("$k", k), ("$v", Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""));
            Set("HideShorts", s.HideShorts); Set("HideComments", s.HideComments); Set("FocusMode", s.FocusMode);
            Set("OwnPlayer", s.OwnPlayer); Set("SponsorBlock", s.SponsorBlock); Set("AutoUpdate", s.AutoUpdate);
            Set("ColorScheme", s.ColorScheme); Set("Accent", s.Accent);
            tx.Commit();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Daten konnten nicht gespeichert werden", ex); // Daten bleiben im Speicher
        }
    }

    private void BackupBroken()
    {
        try { if (File.Exists(_path)) File.Move(_path, _path + ".defekt", overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* wird beim nächsten Speichern überschrieben */ }
    }

    private void ImportLegacy(string? jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath)) return;
        try
        {
            _data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(jsonPath)) ?? new StoreData();
            AppLog.Info("Alte Daten aus lumora-data.json wurden übernommen");
            File.Move(jsonPath, jsonPath + ".uebernommen", overwrite: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Alte Datei lumora-data.json konnte nicht übernommen werden", ex);
            _data = new StoreData();
        }
    }
}
