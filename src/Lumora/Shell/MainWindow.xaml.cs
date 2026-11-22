using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace Lumora.Shell;

/// <summary>
/// Lumora: eigene Startseite, eigener Player, Favoriten, Verlauf und Anpassungen rund um YouTube.
/// Suche liefert die YouTube-Seite; Videos laufen im eigenen Player (YouTubes Einbettungs-Player). Werbung wird nicht verändert oder blockiert.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LocalStore _store;
    private WindowState _prevState;
    private WindowStyle _prevStyle;
    private ResizeMode _prevResize;
    private string? _scriptId;
    private bool _onStartPage, _startingInternal;
    private string? _currentVideoId;
    private bool _playerOk, _rawWatch;

    private bool UseOwnPlayer => _playerOk && _store.Settings.OwnPlayer && !_rawWatch;

    public MainWindow()
    {
        InitializeComponent();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumora");
        _store = new LocalStore(Path.Combine(dir, "lumora-data.json"));
        ApplySettingsToUi();

        Loaded += async (_, _) => await StartAsync(dir);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5) { Web.CoreWebView2?.Reload(); e.Handled = true; }
            else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) { GoBack(); e.Handled = true; }
        };
    }

    private async Task StartAsync(string dir)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dir, "WebView2"),
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or IOException)
        {
            ErrorText.Text = "Der Browser-Teil konnte nicht gestartet werden. Bitte installiere die „Microsoft Edge WebView2 Runtime“ " +
                             "(in Windows 11 bereits enthalten) und starte Lumora neu.\n\n" + ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            Web.Visibility = Visibility.Collapsed;
            return;
        }

        var core = Web.CoreWebView2;
        try
        {
            var webDir = Path.Combine(dir, "web");
            PlayerPage.Write(webDir);
            core.SetVirtualHostNameToFolderMapping(YouTubeUrls.PlayerHost, webDir, CoreWebView2HostResourceAccessKind.Allow);
            _playerOk = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _playerOk = false; // ohne eigenen Player weiter: Videos laufen dann auf der YouTube-Seite
        }
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Profile.PreferredColorScheme = SchemeOf(_store.Settings.ColorScheme);
        await InstallScriptAsync();

        core.DocumentTitleChanged += (_, _) => OnTitleChanged();
        core.SourceChanged += (_, _) => OnSourceChanged();
        core.HistoryChanged += (_, _) => { BackButton.IsEnabled = core.CanGoBack; ForwardButton.IsEnabled = core.CanGoForward; };
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
        core.WebMessageReceived += (_, e) => OnWebMessage(e.WebMessageAsJson, e.Source);

        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) { e.Cancel = true; return; }
            if (YouTubeUrls.IsPlayerPage(uri)) return;
            if (UseOwnPlayer && YouTubeUrls.TryGetVideoId(uri) is { } vid)
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(() => PlayVideo(vid)));
                return;
            }
            if (uri.Scheme == "about" || (uri.Scheme == "data" && _startingInternal) || YouTubeUrls.IsInApp(uri)) return;
            e.Cancel = true;
            OpenExternal(uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
            if (YouTubeUrls.IsInApp(uri)) core.Navigate(uri.ToString()); else OpenExternal(uri);
        };

        BackButton.IsEnabled = ForwardButton.IsEnabled = false;
        ShowStartPage();
    }

    // ---------- Startseite ----------

    private void ShowStartPage()
    {
        if (Web.CoreWebView2 is null) return;
        _startingInternal = true;
        _onStartPage = true;
        _rawWatch = false;
        Web.CoreWebView2.NavigateToString(StartPage.Build(_store));
        Title = "Lumora";
    }

    private void OnWebMessage(string json, string source)
    {
        Uri.TryCreate(source, UriKind.Absolute, out var src);
        var fromStart = _onStartPage;
        var fromYouTube = src is not null && YouTubeUrls.IsInApp(src) && src.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase);
        var fromPlayer = YouTubeUrls.IsPlayerPage(src);
        if (!fromStart && !fromYouTube && !fromPlayer) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : null;
            if (text is null) return;
            using var msg = JsonDocument.Parse(text);
            var root = msg.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            var vid = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
            switch (type)
            {
                case "play" when (fromStart || fromYouTube) && YouTubeUrls.IsValidVideoId(vid):
                    _rawWatch = false;
                    PlayVideo(vid!);
                    return;
                case "raw" when fromPlayer && YouTubeUrls.IsValidVideoId(vid):
                    _rawWatch = true;
                    Go(YouTubeUrls.WatchUrl(vid!));
                    return;
            }
            if (!fromStart) return; // alles Weitere nur von der eigenen Startseite
            switch (type)
            {
                case "search":
                    if (root.TryGetProperty("q", out var q)) Search(q.GetString());
                    return;
                case "clearRecent": _store.ClearRecent(); break;
                case "clearSearches": _store.ClearSearches(); break;
                case "unfav":
                    if (YouTubeUrls.IsValidVideoId(vid)) _store.RemoveFavorite(vid!);
                    break;
                default: return;
            }
            ShowStartPage();
        }
        catch (JsonException) { /* ungültige Nachricht ignorieren */ }
    }

    private void PlayVideo(string id)
    {
        if (Web.CoreWebView2 is null) return;
        _onStartPage = false;
        Web.CoreWebView2.Navigate(YouTubeUrls.PlayerUrl(id, _store.Settings.SponsorBlock));
    }

    // ---------- Seitenwechsel verfolgen ----------

    private void OnSourceChanged()
    {
        var uriText = Web.CoreWebView2.Source;
        if (_startingInternal && uriText.StartsWith("data:", StringComparison.Ordinal)) _startingInternal = false;
        else if (!uriText.StartsWith("data:", StringComparison.Ordinal)) _onStartPage = false;

        Uri.TryCreate(uriText, UriKind.Absolute, out var uri);
        _currentVideoId = YouTubeUrls.TryGetVideoId(uri) ?? YouTubeUrls.TryGetPlayerVideoId(uri);
        if (_currentVideoId != null) _store.AddRecent(_currentVideoId, Web.CoreWebView2.DocumentTitle);
        UpdateFavoriteButton();
    }

    private void OnTitleChanged()
    {
        var title = Web.CoreWebView2.DocumentTitle;
        Title = string.IsNullOrWhiteSpace(title) || _onStartPage ? "Lumora" : YouTubeUrls.CleanTitle(title) + " – Lumora";
        if (_currentVideoId != null) _store.UpdateTitle(_currentVideoId, title);
    }

    private void UpdateFavoriteButton()
    {
        var has = _currentVideoId != null;
        FavButton.IsEnabled = has;
        var fav = has && _store.IsFavorite(_currentVideoId!);
        FavButton.Content = fav ? "" : "";
        FavButton.ToolTip = fav ? "Aus Favoriten entfernen" : "Zu Favoriten hinzufügen";
        AutomationProperties.SetName(FavButton, (string)FavButton.ToolTip);
    }

    // ---------- Anpassungen ----------

    private static CoreWebView2PreferredColorScheme SchemeOf(string s) => s switch
    {
        "light" => CoreWebView2PreferredColorScheme.Light,
        "dark" => CoreWebView2PreferredColorScheme.Dark,
        _ => CoreWebView2PreferredColorScheme.Auto,
    };

    private async Task InstallScriptAsync()
    {
        var core = Web.CoreWebView2;
        if (_scriptId != null) core.RemoveScriptToExecuteOnDocumentCreated(_scriptId);
        var css = JsonSerializer.Serialize(StartPage.BuildCss(_store.Settings));
        var own = UseOwnPlayer ? "true" : "false";
        // Klick auf einen Videolink der YouTube-Seite öffnet Lumoras Player (die Seite wechselt sonst ohne Neuladen).
        var click = "if(" + own + "&&window===window.top&&/(^|\\.)youtube\\.com$/.test(location.hostname)){document.addEventListener('click',function(e){" +
                    "if(e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey)return;var a=e.target.closest&&e.target.closest('a[href]');if(!a)return;" +
                    "var u;try{u=new URL(a.href,location.href)}catch(x){return}if(u.pathname!=='/watch')return;var v=u.searchParams.get('v');" +
                    "if(!/^[A-Za-z0-9_-]{11}$/.test(v||''))return;e.preventDefault();e.stopPropagation();" +
                    "window.chrome.webview.postMessage(JSON.stringify({type:'play',id:v}))},true)}";
        var js = click + "(function(){var c=" + css + ";function a(){var s=document.getElementById('lumora-style');" +
                 "if(!s){s=document.createElement('style');s.id='lumora-style';(document.head||document.documentElement).appendChild(s)}s.textContent=c}" +
                 "a();document.addEventListener('DOMContentLoaded',a)})();";
        _scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(js);
    }

    private void ApplySettingsToUi()
    {
        var s = _store.Settings;
        MiShorts.IsChecked = s.HideShorts;
        MiComments.IsChecked = s.HideComments;
        MiFocus.IsChecked = s.FocusMode;
        MiOwn.IsChecked = s.OwnPlayer;
        MiSb.IsChecked = s.SponsorBlock;
        MiAuto.IsChecked = s.ColorScheme == "auto";
        MiLight.IsChecked = s.ColorScheme == "light";
        MiDark.IsChecked = s.ColorScheme == "dark";
        Bar.Background = (SolidColorBrush)new BrushConverter().ConvertFromString(s.Accent)!;
    }

    private async void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        var s = _store.Settings;
        s.HideShorts = MiShorts.IsChecked == true;
        s.HideComments = MiComments.IsChecked == true;
        s.FocusMode = MiFocus.IsChecked == true;
        s.OwnPlayer = MiOwn.IsChecked == true;
        s.SponsorBlock = MiSb.IsChecked == true;
        _store.SaveSettings();
        if (Web.CoreWebView2 is null) return;
        await InstallScriptAsync();
        Web.CoreWebView2.Reload();
    }

    private void OnSchemeChanged(object sender, RoutedEventArgs e)
    {
        _store.Settings.ColorScheme = (string)((MenuItem)sender).Tag;
        _store.SaveSettings();
        ApplySettingsToUi();
        if (Web.CoreWebView2 != null) Web.CoreWebView2.Profile.PreferredColorScheme = SchemeOf(_store.Settings.ColorScheme);
    }

    private void OnAccentChanged(object sender, RoutedEventArgs e)
    {
        _store.Settings.Accent = (string)((MenuItem)sender).Tag;
        _store.SaveSettings();
        ApplySettingsToUi();
        if (_onStartPage) ShowStartPage();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        SettingsMenu.PlacementTarget = SettingsButton;
        SettingsMenu.IsOpen = true;
    }

    // ---------- Navigation ----------

    private void Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || Web.CoreWebView2 is null) return;
        _store.AddSearch(query);
        _onStartPage = false;
        _rawWatch = false;
        Web.CoreWebView2.Navigate(YouTubeUrls.Search(query));
    }

    private static void OpenExternal(Uri uri)
    {
        if (!YouTubeUrls.IsSafeExternal(uri)) return;
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    private void SetFullscreen(bool on)
    {
        if (on)
        {
            _prevState = WindowState; _prevStyle = WindowStyle; _prevResize = ResizeMode;
            Bar.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Normal; // nötig, damit die Taskleiste sicher überdeckt wird
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _prevStyle; ResizeMode = _prevResize; WindowState = _prevState;
            Bar.Visibility = Visibility.Visible;
        }
    }

    private void Go(string url) { _onStartPage = false; Web.CoreWebView2?.Navigate(url); }
    private void GoBack() { if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack(); }

    private void OnStart(object s, RoutedEventArgs e) => ShowStartPage();
    private void OnBack(object s, RoutedEventArgs e) => GoBack();
    private void OnForward(object s, RoutedEventArgs e) { if (Web.CoreWebView2?.CanGoForward == true) Web.CoreWebView2.GoForward(); }
    private void OnReload(object s, RoutedEventArgs e) { if (_onStartPage) ShowStartPage(); else Web.CoreWebView2?.Reload(); }
    private void OnDiscover(object s, RoutedEventArgs e) => Go(YouTubeUrls.Home);
    private void OnSubscriptions(object s, RoutedEventArgs e) => Go(YouTubeUrls.Subscriptions);
    private void OnLibrary(object s, RoutedEventArgs e) => Go(YouTubeUrls.Library);
    private void OnSearch(object s, RoutedEventArgs e) => Search(SearchBox.Text);
    private void OnSearchKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { Search(SearchBox.Text); e.Handled = true; } }
    private void OnFocusSearch(object s, ExecutedRoutedEventArgs e) { SearchBox.Focus(); SearchBox.SelectAll(); }

    private void OnFavorite(object s, RoutedEventArgs e)
    {
        if (_currentVideoId is null) return;
        _store.ToggleFavorite(_currentVideoId, Web.CoreWebView2.DocumentTitle);
        UpdateFavoriteButton();
    }
}
