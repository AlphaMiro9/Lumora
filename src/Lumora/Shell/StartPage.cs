using System.Net;
using System.Text;

namespace Lumora.Shell;

/// <summary>Baut Lumoras eigene Startseite und die Anpassungen (CSS) für die YouTube-Seiten.</summary>
public static class StartPage
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Build(LocalStore store)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>Lumora</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<style>:root{--accent:").Append(E(store.Settings.Accent)).Append(";--bg:#f4f7f8;--surface:#fff;--text:#14232a;--muted:#4f646c;--line:#c9d6da}");
        sb.Append("@media(prefers-color-scheme:dark){:root{--bg:#0f1a1f;--surface:#17252c;--text:#e8f1f3;--muted:#9db3ba;--line:#2c4048}}");
        sb.Append("*{box-sizing:border-box}body{margin:0;font-family:'Segoe UI',sans-serif;background:var(--bg);color:var(--text)}");
        sb.Append("main{max-width:1180px;margin:0 auto;padding:40px 28px}h1{font-size:44px;margin:0;color:var(--accent)}");
        sb.Append("p.sub{color:var(--muted);margin:6px 0 24px}form{display:flex;gap:10px;margin-bottom:12px}");
        sb.Append("input{flex:1;font-size:18px;padding:12px 16px;border-radius:12px;border:2px solid var(--line);background:#fff;color:#14232a}");
        sb.Append("input:focus,button:focus-visible,a:focus-visible{outline:3px solid #f59e0b;outline-offset:2px}");
        sb.Append("button{font:inherit;cursor:pointer;border:0;border-radius:10px;padding:10px 18px;background:var(--accent);color:#fff}");
        sb.Append("button.chip{background:var(--surface);color:var(--text);border:1px solid var(--line);padding:6px 14px;margin:0 8px 8px 0;border-radius:20px}");
        sb.Append("button.ghost{background:transparent;color:var(--muted);padding:4px 8px}h2{font-size:20px;margin:30px 0 12px;display:flex;align-items:center;justify-content:space-between}");
        sb.Append(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:16px}");
        sb.Append(".card{position:relative;background:var(--surface);border:1px solid var(--line);border-radius:14px;overflow:hidden}");
        sb.Append(".card a{display:block;color:inherit;text-decoration:none}.card img{width:100%;aspect-ratio:16/9;object-fit:cover;display:block;background:var(--line)}");
        sb.Append(".card span{display:block;padding:10px 12px;font-weight:600;font-size:14px;line-height:1.3;max-height:3.9em;overflow:hidden}");
        sb.Append(".card button.x{position:absolute;top:6px;right:6px;padding:2px 9px;background:#000b;border-radius:14px}.empty{color:var(--muted)}</style></head><body><main>");
        sb.Append("<h1>Lumora</h1><p class=\"sub\">Deine Startseite. Suche, zuletzt Gesehenes und Favoriten liegen nur auf diesem PC.</p>");
        sb.Append("<form id=\"f\"><input id=\"q\" type=\"search\" placeholder=\"YouTube durchsuchen\" aria-label=\"YouTube durchsuchen\" maxlength=\"200\" autofocus><button type=\"submit\">Suchen</button></form>");

        if (store.Searches.Count > 0)
        {
            sb.Append("<div>");
            foreach (var s in store.Searches) sb.Append("<button class=\"chip\" data-q=\"").Append(E(s)).Append("\">").Append(E(s)).Append("</button>");
            sb.Append("<button class=\"ghost\" data-act=\"clearSearches\">Verlauf löschen</button></div>");
        }

        Section(sb, "Favoriten", store.Favorites, removable: true, "Noch keine Favoriten. Öffne ein Video und klicke oben auf den Stern.", null);
        Section(sb, "Zuletzt gesehen", store.Recent, removable: false, "Hier erscheinen die Videos, die du dir ansiehst.", store.Recent.Count > 0 ? "clearRecent" : null);

        sb.Append("<script>var P=function(m){window.chrome.webview.postMessage(JSON.stringify(m))};");
        sb.Append("document.getElementById('f').addEventListener('submit',function(e){e.preventDefault();var q=document.getElementById('q').value;if(q.trim())P({type:'search',q:q})});");
        sb.Append("document.addEventListener('click',function(e){var b=e.target.closest('button');if(!b)return;");
        sb.Append("if(b.dataset.q)P({type:'search',q:b.dataset.q});else if(b.dataset.act)P({type:b.dataset.act});else if(b.dataset.unfav)P({type:'unfav',id:b.dataset.unfav})});");
        sb.Append("</script></main></body></html>");
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title, IReadOnlyList<VideoEntry> items, bool removable, string emptyText, string? clearAct)
    {
        sb.Append("<h2>").Append(E(title));
        if (clearAct != null) sb.Append("<button class=\"ghost\" data-act=\"").Append(clearAct).Append("\">Leeren</button>");
        sb.Append("</h2>");
        if (items.Count == 0) { sb.Append("<p class=\"empty\">").Append(E(emptyText)).Append("</p>"); return; }
        sb.Append("<div class=\"grid\">");
        foreach (var v in items)
        {
            if (!YouTubeUrls.IsValidVideoId(v.Id)) continue;
            sb.Append("<div class=\"card\"><a href=\"").Append(YouTubeUrls.WatchUrl(v.Id)).Append("\"><img loading=\"lazy\" alt=\"\" src=\"")
              .Append(YouTubeUrls.ThumbnailUrl(v.Id)).Append("\"><span>").Append(E(v.Title)).Append("</span></a>");
            if (removable) sb.Append("<button class=\"x\" data-unfav=\"").Append(v.Id).Append("\" aria-label=\"Favorit entfernen: ").Append(E(v.Title)).Append("\">×</button>");
            sb.Append("</div>");
        }
        sb.Append("</div>");
    }

    /// <summary>CSS für die YouTube-Seiten. Werbung wird bewusst nicht angetastet.</summary>
    public static string BuildCss(AppSettings s)
    {
        var css = new StringBuilder();
        if (s.HideShorts)
            css.Append("ytd-reel-shelf-renderer,ytd-rich-shelf-renderer[is-shorts],ytd-guide-entry-renderer:has(a[title=\"Shorts\"]),")
               .Append("ytd-mini-guide-entry-renderer[aria-label=\"Shorts\"],ytd-video-renderer:has(a[href^=\"/shorts/\"]),")
               .Append("ytd-rich-item-renderer:has(a[href^=\"/shorts/\"]){display:none!important}");
        if (s.HideComments) css.Append("ytd-comments#comments{display:none!important}");
        if (s.FocusMode) css.Append("ytd-watch-flexy #secondary,ytd-watch-flexy #related{display:none!important}");
        return css.ToString();
    }
}
