# Lumora – YouTube-Suche mit eigenem Player (WPF / .NET 8)

Eigenständige Windows-App mit eigenem Namen, eigener Startseite und **eigenem Videoplayer**.
Die Suche ist die normale YouTube-Suche. Ein Klick auf ein Video öffnet es in Lumoras Player mit eigener Steuerung
(Abspielen, Position, Tempo, Lautstärke, Vollbild, Tastenkürzel) statt auf der YouTube-Seite.

## Funktionen

| Thema | Umsetzung |
|---|---|
| Suche | Die echte YouTube-Suche in **WebView2** (Edge-Engine). Kein API-Schlüssel nötig |
| Player | YouTubes offizieller Einbettungs-Player (IFrame-API) mit Lumoras eigener Bedienleiste |
| SponsorBlock | Optional: überspringt Sponsor-, Eigenwerbungs- und Aufforderungs-Segmente (Daten von sponsor.ajay.app, CC BY-NC-SA 4.0, nur nicht-kommerziell) |
| Startseite | Zuletzt gesehen, Favoriten, Suchverlauf – nur lokal auf deinem PC |
| Anmeldung | Optional: Anmelden auf der YouTube-Seite. Das Profil bleibt unter `%LocalAppData%\Lumora\WebView2` gespeichert |
| Werbung | Entscheidet YouTube. Lumora blockiert sie nicht (das verbieten die YouTube-Bedingungen). Mit **YouTube Premium** und Anmeldung gibt es keine |
| Sicherheit | Im Fenster bleiben nur https-Seiten von YouTube und Google-Anmeldung, alles andere öffnet der Standardbrowser |
| Rechte | `asInvoker` – keine Windows-Administratorrechte nötig |

Manche Videos erlauben die Einbettung nicht. Dann bietet der Player „Auf YouTube ansehen“ an.
Unter Einstellungen (Zahnrad) lassen sich der eigene Player und SponsorBlock ein- und ausschalten.

Voraussetzung: Windows 10/11 mit der *WebView2-Runtime* (in Windows 11 und aktuellem Edge enthalten).

## Download

Unter **Releases** liegen `Lumora-win-x64.exe` (normale PCs) und `Lumora-win-arm64.exe` (Windows auf ARM). Einfach starten, keine Installation nötig.
Ein Update ersetzt nur die Exe. Favoriten, Einstellungen und Anmeldung bleiben erhalten.

## Entwickeln (Visual Studio 2022/2026)

1. `Lumora.sln` öffnen (Workload „.NET-Desktopentwicklung“), Projekt **Lumora** starten (F5).
2. Tests: *Test → Alle Tests ausführen* oder `dotnet test`.
3. Release: Tag `v*` pushen, GitHub Actions baut beide Exe-Dateien.

## Rechtliches

YouTube-Inhalte gehören den jeweiligen Urhebern; die Nutzung unterliegt den YouTube-Nutzungsbedingungen.
Lumora ist nicht mit YouTube, Google oder SponsorBlock verbunden.
