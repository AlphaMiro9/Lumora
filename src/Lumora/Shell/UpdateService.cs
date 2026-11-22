using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lumora.Shell;

public sealed record UpdateInfo(Version Version, string ReleaseUrl, string AssetName, string AssetUrl, string? ChecksumUrl);

/// <summary>
/// Update-Modus: fragt bei GitHub die neueste Lumora-Version ab (öffentliche Schnittstelle, kein Schlüssel).
/// Der Download wird nur von github.com/AlphaMiro9/Lumora/releases akzeptiert und gegen die SHA-256-Prüfsumme
/// aus dem Release geprüft. Ohne passende Prüfsumme wird nichts installiert.
/// </summary>
public static class UpdateService
{
    public const string Repo = "AlphaMiro9/Lumora";
    public const string ChecksumFile = "SHA256SUMS.txt";
    private const long MaxDownloadBytes = 400L * 1024 * 1024;

    public static string ReleasesPage => "https://github.com/" + Repo + "/releases/latest";

    public static Version Current
        => Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static Version Normalize(Version v) => new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    /// <summary>Liest Tags wie "v0.5.0" oder "0.5.0-beta" (Teil nach "-" wird ignoriert).</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        var cut = t.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) t = t[..cut];
        return Version.TryParse(t, out var v) ? Normalize(v) : null;
    }

    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    public static string AssetNameFor(Architecture arch)
        => arch == Architecture.Arm64 ? "Lumora-win-arm64.exe" : "Lumora-win-x64.exe";

    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        return u.Scheme == Uri.UriSchemeHttps
            && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && u.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTrustedReleasePage(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        return u.Scheme == Uri.UriSchemeHttps
            && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && u.AbsolutePath.StartsWith("/" + Repo + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sucht in einer Datei im Format "hash  dateiname" die Prüfsumme (64 Hex-Zeichen) zu einem Dateinamen.</summary>
    public static string? ParseChecksum(string? text, string fileName)
    {
        if (text is null) return null;
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            var name = parts[1].Trim().TrimStart('*');
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase) && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Flag(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    /// <summary>Wertet die GitHub-Antwort für "neuestes Release" aus. Entwürfe, Vorabversionen und unfertige Releases ohne Exe ergeben null.</summary>
    public static UpdateInfo? ParseRelease(string json, Architecture arch)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || Flag(r, "draft") || Flag(r, "prerelease")) return null;
        var version = ParseTag(Str(r, "tag_name"));
        if (version is null) return null;
        var page = Str(r, "html_url");
        var want = AssetNameFor(arch);
        string? asset = null, assetUrl = null, sums = null;
        if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = Str(a, "name");
                var url = Str(a, "browser_download_url");
                if (name is null || !IsTrustedDownloadUrl(url)) continue;
                if (name == want) { asset = name; assetUrl = url; }
                else if (name == ChecksumFile) sums = url;
            }
        }
        if (asset is null || assetUrl is null) return null;
        return new UpdateInfo(version, IsTrustedReleasePage(page) ? page! : ReleasesPage, asset, assetUrl, sums);
    }

    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repo + "/releases/latest");
        req.Headers.UserAgent.ParseAdd("Lumora/" + Current);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // noch kein Release oder Repository nicht öffentlich
        resp.EnsureSuccessStatusCode();
        var info = ParseRelease(await resp.Content.ReadAsStringAsync(ct), RuntimeInformation.ProcessArchitecture);
        return info is not null && IsNewer(info.Version, Current) ? info : null;
    }

    /// <summary>Lädt die neue Exe, prüft die Prüfsumme und gibt den Pfad der geprüften Datei zurück.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo info, string folder, IProgress<int>? progress, CancellationToken ct = default)
    {
        if (info.ChecksumUrl is null) throw new InvalidOperationException("Das Release enthält keine Prüfsumme (" + ChecksumFile + ").");
        if (!IsTrustedDownloadUrl(info.AssetUrl) || !IsTrustedDownloadUrl(info.ChecksumUrl)) throw new InvalidOperationException("Unbekannte Download-Adresse.");

        var sumsText = await http.GetStringAsync(info.ChecksumUrl, ct);
        var expected = ParseChecksum(sumsText, info.AssetName) ?? throw new InvalidDataException("Keine Prüfsumme für " + info.AssetName + " gefunden.");

        Directory.CreateDirectory(folder);
        var part = Path.Combine(folder, "Lumora-" + info.Version + ".exe.part");
        var final = Path.Combine(folder, "Lumora-" + info.Version + ".exe");
        try
        {
            using var resp = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            if (total > MaxDownloadBytes) throw new InvalidDataException("Die Datei ist ungewöhnlich groß.");
            using var sha = SHA256.Create();
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(part))
            {
                var buf = new byte[81920];
                long done = 0;
                int n, lastPct = -1;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    done += n;
                    if (done > MaxDownloadBytes) throw new InvalidDataException("Die Datei ist ungewöhnlich groß.");
                    sha.TransformBlock(buf, 0, n, null, 0);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    if (total > 0)
                    {
                        var pct = (int)(done * 100 / total);
                        if (pct != lastPct) { lastPct = pct; progress?.Report(pct); }
                    }
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }
            var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (actual != expected) throw new InvalidDataException("Die Prüfsumme der heruntergeladenen Datei stimmt nicht. Das Update wird nicht installiert.");
            File.Move(part, final, overwrite: true);
            return final;
        }
        catch
        {
            try { File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Ersetzt die laufende Exe: die alte wird in "*.old" umbenannt (das geht auch während sie läuft), die neue kopiert.</summary>
    public static void ReplaceExecutable(string currentExe, string newExe)
    {
        var old = currentExe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(currentExe, old);
        try { File.Copy(newExe, currentExe, overwrite: false); }
        catch
        {
            try { File.Move(old, currentExe, overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Räumt die beim letzten Update übrig gebliebene alte Exe weg.</summary>
    public static void CleanupOld()
    {
        try
        {
            var p = Environment.ProcessPath;
            if (p != null && File.Exists(p + ".old")) File.Delete(p + ".old");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
