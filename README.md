# Lumora – YouTube-Suche mit eigenem Player (WPF / .NET 8)

Eigenständige Windows-App mit eigenem Namen, eigenem Logo, eigener Startseite und **eigenem Videoplayer**.
Die Suche ist die normale YouTube-Suche. Ein Klick auf ein Video öffnet es in Lumoras Player mit eigener Steuerung
(Abspielen, Position, Tempo, Lautstärke, Vollbild, Tastenkürzel) statt auf der YouTube-Seite.

## Funktionen

| Thema | Umsetzung |
|---|---|
| Suche | Die echte YouTube-Suche in **WebView2** (Edge-Engine). Kein API-Schlüssel nötig |
| Player | YouTubes offizieller Einbettungs-Player (IFrame-API) mit Lumoras eigener Bedienleiste. Das YouTube-Logo im Player bleibt, so verlangen es die Regeln |
| SponsorBlock | Optional: überspringt Sponsor-, Eigenwerbungs- und Aufforderungs-Segmente (Daten von sponsor.ajay.app, CC BY-NC-SA 4.0, nur nicht-kommerziell) |
| Datenbank | Zuletzt gesehen, Favoriten, Suchverlauf und Einstellungen liegen in einer lokalen **SQLite**-Datenbank. Alte JSON-Daten werden übernommen |
| Protokoll | Eigene Log-Datei (rotiert automatisch, keine Suchbegriffe). Öffnen über Einstellungen → Protokoll-Ordner |
| Update-Modus | Sucht beim Start nach neuen Releases. „Jetzt aktualisieren“ lädt die neue Exe, prüft die SHA-256-Prüfsumme und startet neu. Abschaltbar |
| Fensterrand | Titelleiste und Rahmen in Lumoras Farbe (Windows 11), eigenes Icon |
| Anmeldung | Optional: Anmelden auf der YouTube-Seite. Das Profil bleibt lokal gespeichert |
| Werbung | Entscheidet YouTube. Lumora blockiert sie nicht (das verbieten die YouTube-Bedingungen). Mit **YouTube Premium** und Anmeldung gibt es keine |
| Sicherheit | Im Fenster bleiben nur https-Seiten von YouTube und Google-Anmeldung, alles andere öffnet der Standardbrowser |
| Rechte | `asInvoker` – keine Windows-Administratorrechte nötig |

Manche Videos erlauben die Einbettung nicht. Dann bietet der Player „Auf YouTube ansehen“ an.
Voraussetzung: Windows 10/11 mit der *WebView2-Runtime* (in Windows 11 und aktuellem Edge enthalten).

## Download

Unter **Releases** liegen `Lumora-win-x64.exe` (normale PCs) und `Lumora-win-arm64.exe` (Windows auf ARM). Einfach starten, keine Installation nötig.
Ein Update ersetzt nur die Exe. Favoriten, Einstellungen und Anmeldung bleiben erhalten. Ältere Versionen bleiben als Release erhalten.

## Entwickeln (Visual Studio 2022/2026)

1. `Lumora.sln` öffnen (Workload „.NET-Desktopentwicklung“), Projekt **Lumora** starten (F5).
2. Tests: *Test → Alle Tests ausführen* oder `dotnet test`.
3. Release: Tag `v*` pushen, GitHub Actions baut beide Exe-Dateien samt `SHA256SUMS.txt`.

## Rechtliches

YouTube-Inhalte gehören den jeweiligen Urhebern; die Nutzung unterliegt den YouTube-Nutzungsbedingungen.
Lumora ist nicht mit YouTube, Google oder SponsorBlock verbunden.
