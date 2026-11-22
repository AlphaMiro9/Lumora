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
    public void Corrupt_file_and_bad_settings_fall_back()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "d.json");
        File.WriteAllText(path, "{ kaputt");
        Assert.Empty(new LocalStore(path).Recent);
        File.WriteAllText(path, """{ "Settings": { "Accent": "red;}</style>", "ColorScheme": "x" } }""");
        var s = new LocalStore(path);
        Assert.Equal(LocalStore.DefaultAccent, s.Settings.Accent);
        Assert.Equal("auto", s.Settings.ColorScheme);
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
