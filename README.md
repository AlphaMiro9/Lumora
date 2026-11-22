# Lumora – YouTube im eigenen Windows-Fenster (WPF / .NET 8)

Eigenständige Windows-App mit eigenem Namen und eigener Leiste. Sie zeigt die **normale YouTube-Webseite**:
dieselbe Suche, dieselbe Wiedergabe mit Ton, Vollbild, Untertitel, Qualität. Dazu Zurück/Vor, Startseite,
Suchleiste (Strg+L) und Verknüpfungen zu **Abos** und **Bibliothek**.

## Wie es funktioniert

| Thema | Umsetzung |
|---|---|
| Suche und Videos | Die echte YouTube-Seite in **WebView2** (Edge-Engine). Kein API-Schlüssel nötig |
| Anmeldung | Optional: Anmelden auf der YouTube-Seite. Das Profil bleibt unter `%LocalAppData%\Lumora\WebView2` gespeichert. Dann funktionieren Abos, Verlauf und Playlists deines echten Kontos |
| Werbung | Entscheidet YouTube. Lumora blockiert sie nicht (das verbieten die YouTube-Bedingungen). Mit **YouTube Premium** und Anmeldung gibt es keine |
| Sicherheit | Im Fenster bleiben nur https-Seiten von YouTube und der Google-Anmeldung, alles andere öffnet der Standardbrowser |
| Rechte | `asInvoker` – keine Windows-Administratorrechte nötig |

Voraussetzung: Windows 10/11 mit der *WebView2-Runtime* (in Windows 11 und aktuellem Edge enthalten).
Nicht enthalten: eigene Admin- oder Playlist-Verwaltung (das übernimmt YouTube mit deinem Konto).

## Download

Unter **Releases** liegen `Lumora-win-x64.exe` (normale PCs) und `Lumora-win-arm64.exe` (Windows auf ARM). Einfach starten, keine Installation nötig.

## Entwickeln (Visual Studio 2022/2026)

1. `Lumora.sln` öffnen (Workload „.NET-Desktopentwicklung“), Projekt **Lumora** starten (F5).
2. Tests: *Test → Alle Tests ausführen* oder `dotnet test`.
3. Veröffentlichen: `dotnet publish src/Lumora -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64` (ARM64: `-r win-arm64`).

## Rechtliches

YouTube-Inhalte gehören den jeweiligen Urhebern; die Nutzung unterliegt den YouTube-Nutzungsbedingungen.
Lumora ist nicht mit YouTube oder Google verbunden.
