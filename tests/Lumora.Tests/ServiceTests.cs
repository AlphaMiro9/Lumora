using Lumora.Shell;
using Xunit;

namespace Lumora.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lumora-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class YouTubeUrlsTests
{
    [Fact]
    public void Search_builds_normal_youtube_search_url()
        => Assert.Equal("https://www.youtube.com/results?search_query=katzen%20video%20%26%20mehr",
            YouTubeUrls.Search("  katzen video & mehr "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_search_goes_to_home(string? q) => Assert.Equal(YouTubeUrls.Home, YouTubeUrls.Search(q));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("https://m.youtube.com/", true)]
    [InlineData("https://accounts.google.com/signin", true)]
    [InlineData("https://www.youtube-nocookie.com/embed/x", true)]
    [InlineData("http://www.youtube.com/", false)]
    [InlineData("https://evilyoutube.com/", false)]
    [InlineData("https://youtube.com.evil.example/", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    public void Decides_which_pages_stay_in_the_window(string url, bool expected)
        => Assert.Equal(expected, YouTubeUrls.IsInApp(new Uri(url)));

    [Fact]
    public void Null_is_not_in_app() => Assert.False(YouTubeUrls.IsInApp(null));

    [Theory]
    [InlineData("https://example.com/", true)]
    [InlineData("http://example.com/", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("ms-settings:privacy", false)]
    public void Only_web_links_open_externally(string url, bool expected)
        => Assert.Equal(expected, YouTubeUrls.IsSafeExternal(new Uri(url)));
}

public class PlayerUrlTests
{
    [Fact]
    public void Player_url_roundtrips_video_id()
    {
        var url = YouTubeUrls.PlayerUrl("dQw4w9WgXcQ", true);
        Assert.Equal("https://lumora.local/player.html?v=dQw4w9WgXcQ&sb=1", url);
        Assert.Equal("dQw4w9WgXcQ", YouTubeUrls.TryGetPlayerVideoId(new Uri(url)));
        Assert.True(YouTubeUrls.IsPlayerPage(new Uri(url)));
    }

    [Theory]
    [InlineData("https://lumora.local.evil.example/player.html?v=dQw4w9WgXcQ")]
    [InlineData("http://lumora.local/player.html?v=dQw4w9WgXcQ")]
    [InlineData("https://lumora.local/player.html?v=kurz")]
    public void Foreign_or_invalid_player_urls_are_rejected(string url)
        => Assert.Null(YouTubeUrls.TryGetPlayerVideoId(new Uri(url)));

    [Fact]
    public void New_settings_default_to_on() => Assert.True(new AppSettings().OwnPlayer && new AppSettings().SponsorBlock);
}

public class LocalStoreTests
{
    private const string A = "dQw4w9WgXcQ", B = "abcdefghijk";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=5s", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?list=x&v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/results?search_query=a", null)]
    [InlineData("https://www.youtube.com/watch?v=zu-kurz", null)]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ", null)]
    public void Reads_video_id(string url, string? expected) => Assert.Equal(expected, YouTubeUrls.TryGetVideoId(new Uri(url)));

    [Theory]
    [InlineData("(3) Mein Video - YouTube", "Mein Video")]
    [InlineData("Nur Titel", "Nur Titel")]
    [InlineData(null, "")]
    public void Cleans_title(string? raw, string expected) => Assert.Equal(expected, YouTubeUrls.CleanTitle(raw));

    [Fact]
    public void Recent_is_deduplicated_newest_first_and_capped()
    {
        using var t = new TempDir();
        var s = new LocalStore(Path.Combine(t.Path, "d.json"));
        s.AddRecent(A, "Eins - YouTube");
        s.AddRecent(B, "Zwei");
        s.AddRecent(A, "");
        Assert.Equal(new[] { A, B }, s.Recent.Select(v => v.Id));
        Assert.Equal("Eins", s.Recent[0].Title);
        for (var i = 0; i < 60; i++) s.AddRecent($"{i:00000000000}", "x");
        Assert.True(s.Recent.Count <= 40);
        s.AddRecent("ungueltig", "x");
        Assert.DoesNotContain(s.Recent, v => v.Id == "ungueltig");
    }

    [Fact]
    public void Favorites_toggle_and_persist()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "d.json");
        var s = new LocalStore(path);
        Assert.True(s.ToggleFavorite(A, "Titel"));
        s.AddSearch("katzen"); s.AddSearch("Katzen"); s.AddSearch("hunde");
        s.Settings.Accent = "#6D4AFF"; s.SaveSettings();

        var again = new LocalStore(path);
        Assert.True(again.IsFavorite(A));
        Assert.Equal(new[] { "hunde", "Katzen" }, again.Searches);
        Assert.Equal("#6D4AFF", again.Settings.Accent);
        Assert.False(again.ToggleFavorite(A, null));
    }

    [Fact]
    public void Corrupt_database_is_set_aside_and_replaced()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "d.db");
        File.WriteAllText(path, "{ kaputt, das ist keine Datenbank");
        var s = new LocalStore(path);
        Assert.Empty(s.Recent);
        Assert.True(File.Exists(path + ".defekt"));
        s.AddRecent(A, "Neu");
        Assert.Single(new LocalStore(path).Recent);
    }

    [Fact]
    public void Bad_settings_in_database_fall_back()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "d.db");
        _ = new LocalStore(path);
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE settings SET value='red;}</style>' WHERE key='Accent'; UPDATE settings SET value='x' WHERE key='ColorScheme'";
            cmd.ExecuteNonQuery();
        }
        var s = new LocalStore(path);
        Assert.Equal(LocalStore.DefaultAccent, s.Settings.Accent);
        Assert.Equal("auto", s.Settings.ColorScheme);
    }

    [Fact]
    public void Old_json_data_is_imported_once()
    {
        using var t = new TempDir();
        var json = Path.Combine(t.Path, "lumora-data.json");
        File.WriteAllText(json, """{ "Favorites": [ { "Id": "dQw4w9WgXcQ", "Title": "Alt" } ], "Searches": [ "katzen" ] }""");
        var s = new LocalStore(Path.Combine(t.Path, "lumora.db"), json);
        Assert.True(s.IsFavorite(A));
        Assert.Equal(new[] { "katzen" }, s.Searches);
        Assert.False(File.Exists(json));
        Assert.True(new LocalStore(Path.Combine(t.Path, "lumora.db"), json).IsFavorite(A));
    }

    [Fact]
    public void Start_page_escapes_titles()
    {
        using var t = new TempDir();
        var s = new LocalStore(Path.Combine(t.Path, "d.json"));
        s.AddRecent(A, "<script>alert(1)</script> \"x\"");
        var html = StartPage.Build(s);
        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.Contains("&lt;script&gt;alert(1)", html);
    }

    [Fact]
    public void Css_hides_shorts_but_never_ads()
    {
        var css = StartPage.BuildCss(new AppSettings { HideShorts = true, HideComments = true, FocusMode = true });
        Assert.Contains("shorts", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#comments", css);
        Assert.DoesNotContain("ytp-ad", css);
        Assert.DoesNotContain("ad-slot", css);
        Assert.DoesNotContain("ytd-ad", css);
        Assert.Equal("", StartPage.BuildCss(new AppSettings { HideShorts = false }));
    }
}

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v0.5.0", 0, 5, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v2.0-beta", 2, 0, 0)]
    public void Parses_tags(string tag, int major, int minor, int build)
        => Assert.Equal(new Version(major, minor, build), UpdateService.ParseTag(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("neueste")]
    [InlineData("7")]
    public void Rejects_bad_tags(string? tag) => Assert.Null(UpdateService.ParseTag(tag));

    [Fact]
    public void Compares_versions()
    {
        Assert.True(UpdateService.IsNewer(new Version(0, 5, 0), new Version(0, 4, 0)));
        Assert.True(UpdateService.IsNewer(new Version(1, 0, 0), new Version(0, 9, 9)));
        Assert.False(UpdateService.IsNewer(new Version(0, 4, 0), new Version(0, 4, 0, 0)));
        Assert.False(UpdateService.IsNewer(new Version(0, 3, 0), new Version(0, 4, 0)));
    }

    [Theory]
    [InlineData("https://github.com/AlphaMiro9/Lumora/releases/download/v0.5.0/Lumora-win-x64.exe", true)]
    [InlineData("http://github.com/AlphaMiro9/Lumora/releases/download/v0.5.0/Lumora-win-x64.exe", false)]
    [InlineData("https://evil.example/AlphaMiro9/Lumora/releases/download/v0.5.0/Lumora-win-x64.exe", false)]
    [InlineData("https://github.com/Anderer/Lumora/releases/download/v0.5.0/Lumora-win-x64.exe", false)]
    [InlineData("https://github.com.evil.example/AlphaMiro9/Lumora/releases/download/x", false)]
    [InlineData(null, false)]
    public void Only_trusts_own_release_downloads(string? url, bool expected) => Assert.Equal(expected, UpdateService.IsTrustedDownloadUrl(url));

    [Fact]
    public void Reads_checksum_file()
    {
        var h1 = new string('a', 64);
        var h2 = new string('B', 64);
        var text = h1 + "  Lumora-win-x64.exe\n" + h2 + " *Lumora-win-arm64.exe\r\nmuell\n";
        Assert.Equal(h1, UpdateService.ParseChecksum(text, "Lumora-win-x64.exe"));
        Assert.Equal(new string('b', 64), UpdateService.ParseChecksum(text, "Lumora-win-arm64.exe"));
        Assert.Null(UpdateService.ParseChecksum(text, "andere.exe"));
        Assert.Null(UpdateService.ParseChecksum("kurz  Lumora-win-x64.exe", "Lumora-win-x64.exe"));
    }

    private const string Base = "https://github.com/AlphaMiro9/Lumora/releases/download/v0.5.0/";

    [Fact]
    public void Parses_release_for_architecture()
    {
        var json = "{\"tag_name\":\"v0.5.0\",\"html_url\":\"https://github.com/AlphaMiro9/Lumora/releases/tag/v0.5.0\",\"draft\":false,\"prerelease\":false,\"assets\":[" +
                   "{\"name\":\"Lumora-win-x64.exe\",\"browser_download_url\":\"" + Base + "Lumora-win-x64.exe\"}," +
                   "{\"name\":\"Lumora-win-arm64.exe\",\"browser_download_url\":\"" + Base + "Lumora-win-arm64.exe\"}," +
                   "{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"" + Base + "SHA256SUMS.txt\"}]}";
        var arm = UpdateService.ParseRelease(json, System.Runtime.InteropServices.Architecture.Arm64)!;
        Assert.Equal(new Version(0, 5, 0), arm.Version);
        Assert.Equal("Lumora-win-arm64.exe", arm.AssetName);
        Assert.Equal(Base + "SHA256SUMS.txt", arm.ChecksumUrl);
        Assert.Equal("Lumora-win-x64.exe", UpdateService.ParseRelease(json, System.Runtime.InteropServices.Architecture.X64)!.AssetName);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v9.0.0\",\"draft\":true,\"assets\":[]}")]
    [InlineData("{\"tag_name\":\"v9.0.0\",\"prerelease\":true,\"assets\":[]}")]
    [InlineData("{\"tag_name\":\"v9.0.0\",\"assets\":[]}")]
    [InlineData("{\"tag_name\":\"v9.0.0\",\"assets\":[{\"name\":\"Lumora-win-x64.exe\",\"browser_download_url\":\"https://evil.example/x.exe\"}]}")]
    [InlineData("[]")]
    public void Ignores_unusable_releases(string json) => Assert.Null(UpdateService.ParseRelease(json, System.Runtime.InteropServices.Architecture.X64));

    [Fact]
    public void Replace_executable_keeps_old_copy_and_installs_new()
    {
        using var t = new TempDir();
        var cur = Path.Combine(t.Path, "Lumora.exe");
        var neu = Path.Combine(t.Path, "neu.exe");
        File.WriteAllText(cur, "alt");
        File.WriteAllText(neu, "neu");
        UpdateService.ReplaceExecutable(cur, neu);
        Assert.Equal("neu", File.ReadAllText(cur));
        Assert.Equal("alt", File.ReadAllText(cur + ".old"));
    }
}

public class AppLogTests
{
    [Fact]
    public void Formats_one_line_per_entry_without_line_breaks_in_message()
    {
        var line = AppLog.Format(new DateTime(2026, 11, 22, 10, 30, 0, DateTimeKind.Utc), "ERROR", "Zeile1\nZeile2", new IOException("kaputt"));
        Assert.StartsWith("2026-11-22 10:30:00Z ERROR Zeile1 Zeile2 | IOException: kaputt", line);
        Assert.Single(line.TrimEnd().Split('\n'));
    }

    [Fact]
    public void Writes_and_rotates_log_file()
    {
        using var t = new TempDir();
        AppLog.Init(t.Path, 1024);
        try
        {
            for (var i = 0; i < 60; i++) AppLog.Info("Eintrag " + i + " " + new string('x', 40));
            Assert.True(File.Exists(Path.Combine(t.Path, "lumora.log")));
            Assert.True(File.Exists(Path.Combine(t.Path, "lumora.log.1")));
            Assert.Contains("Eintrag 59", File.ReadAllText(Path.Combine(t.Path, "lumora.log")));
        }
        finally { AppLog.Shutdown(); }
    }
}
