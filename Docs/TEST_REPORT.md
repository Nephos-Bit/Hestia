# Wintermarkt V0.3 – Testbericht

Datum: 2026-10-26  
Zielplattform: Android, lokal/offline, Hochformat

## Bestandsaufnahme V0.2

| Bereich | Befund | Übernahme in V0.3 |
|---|---|---|
| Diorama/Kamera | Orthografische, stabile Kamera; kein reguläres HUD | unverändert |
| Marktobjekte | Beim Start prozedural und fest erzeugt | durch layoutgesteuerte Voxelobjekte ersetzt |
| Navigation | Eigene benannte Routen, Hindernisdetours; kein NavMesh | um dynamische Hindernisse und Ziele erweitert |
| NPC-Ziele | An festen Waypoints/Routen ausgerichtet | vorhandene Routen beibehalten, Bauziele ergänzt |
| Beleuchtung | Tageszeitcontroller, warme Punktlichter und Emissions-Meshes | dynamische Bau-Lichter werden nachbestückt |
| Speicherung | `PlayerPrefs` für globale Einstellungen | versionierte JSON-Layoutdatei ergänzt |

## Implementiert

- 150 × 150 m großer Baubereich mit 0,60-m-Raster, Grenzen, Fußabdrücken, Drehung in 90°-Schritten, gültiger/ungültiger Vorschau und expliziter Bestätigung.
- Die bestehenden V0.2-Stände, Baum, Laternen, Bank, Kisten und Karren nutzen wieder ihre ursprünglichen Voxelproportionen; ihre Objektursprünge werden auf das 0,60-m-Raster normalisiert.
- Schutz des öffentlichen Querwegs sowie Verhinderung von Überlappungen und Überbauung vorhandener Pfade.
- Bearbeitbare Startobjekte: Bäckerei, Getränkestand, Weihnachtsbaum, zwei Laternen, Bank, Kisten und Handkarren.
- Bibliothek: Kakaostand, kleine Tanne, Schneemann, Bank, Laterne, Feuerkorb, Kisten, Zaun, Lichterdekoration, kleiner Weihnachtsbaum, Handkarren und Schneepfad.
- Smartphone-Baumodus mit mindestens 46-dp-Touch-Zielen, zweispaltiger Objektpalette, Seitenwechsel, Drehen, Bestätigen, Abbrechen, Löschen, Undo und Redo (je maximal 40 bestätigte Aktionen).
- Dynamische Weg-Hindernisse, Besuchsziele für Stände sowie seltene Aufenthaltsziele für Bank/Feuerkorb. Aktive NPCs prüfen eine Navigationsrevision und richten sich ohne Teleportation neu aus.
- Lokales JSON mit atomarem temporärem Schreiben und Backup, Formatversion, IDs, Typen, Positionen, Rotation, Varianten und Darstellungseinstellungen. Siehe [LAYOUT_EXAMPLE.json](LAYOUT_EXAMPLE.json).
- Keine Netzwerk- oder Account-Abhängigkeit; Android `forceInternetPermission` bleibt deaktiviert.

## Verifikation

| Prüfung | Status | Nachweis |
|---|---|---|
| Laufzeit-C# | erfüllt | `dotnet build Assembly-CSharp.csproj --no-restore`: 0 Fehler, 0 Warnungen |
| Editor-C# | erfüllt | `dotnet build Assembly-CSharp-Editor.csproj --no-restore`: 0 Fehler, 2 bestehende Unity-6-Deprecation-Warnungen |
| Automatisierte Unity-Funktionschecks | offen | Batchlauf wurde durch eine bereits geöffnete Unity-Instanz für dasselbe Projekt gesperrt; es wurde keine Instanz beendet |
| Diorama-Renderings | offen | `Wintermarkt/Capture V0.3 Screenshots` erzeugt vier Tageszeiten in `Artifacts/Screenshots` |
| Baumodus-Rendering | offen | derselbe Menüpunkt erzeugt `build-mode-placement.png` mit Raster und Kakaostand-Vorschau |
| Android-APK | offen | Buildmethode erzeugt bei installierter Android-Toolchain `Builds/Android/Wintermarkt-v0.3.apk` |
| Wiederherstellung | Code geprüft | `BuildLayoutStore` liest Hauptdatei, Backup oder Standardlayout und verwendet keine Szenenreferenzen |
| Android-30-FPS und 10-Minuten-Test | offen | kein V0.3-Gerätetest in diesem Lauf |

## Auszuführende Unity-Befehle

1. `Wintermarkt/Run Functional Checks`
2. `Wintermarkt/Capture V0.3 Screenshots`
3. `Wintermarkt/Build Android APK`
4. Auf Android: Objekt versetzen, Laterne und Bank ergänzen, Baumodus verlassen, App beenden und neu starten; anschließend zehn Minuten beobachten und Framerate messen.

## Offene Punkte

- Die Performance-Anforderung von möglichst stabilen 30 FPS ist erst nach einem V0.3-Gerätetest erfüllt.
- Das tatsächliche APK und die fünf neuen V0.3-Renderings können erst nach Freigabe der aktuell offenen Unity-Instanz erzeugt werden.
- Die derzeitige IMGUI-Bauoberfläche ist bewusst klein und offline; eine spätere UI-Modernisierung darf das Layoutformat nicht verändern.
