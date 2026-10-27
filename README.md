# Wintermarkt – Diorama Prototype V0.3

Ein vollständig lokales Wintermarkt-Diorama für Android. Die Szene wird aus echten Voxel-Meshes aufgebaut,
folgt der lokalen Gerätezeit und simuliert ruhige Alltagssituationen mit bis zu sechs NPCs. V0.3 ergänzt einen
touch-optimierten Baumodus mit verschiebbaren Marktständen, Dekorationen, Wegen, Undo/Redo und lokaler,
versionierter Layoutspeicherung.

## Voraussetzungen

- Unity `6000.3.23f1` (Unity 6.3 LTS)
- Universal Render Pipeline 17.3
- Android Build Support einschließlich SDK, NDK und OpenJDK
- Für Gerätetests: Android-Gerät mit aktiviertem USB-Debugging

Es werden keine Netzwerkdienste, Analytics-, Werbe- oder Authentifizierungs-SDKs verwendet.

## Projekt starten

1. Repository in Unity Hub als vorhandenes Projekt hinzufügen.
2. Mit Unity 6.3 LTS öffnen.
3. Die Szene `Assets/WinterMarket/Scenes/WinterMarket.unity` öffnen.
4. Play starten.

Die Szene erzeugt das Diorama beim Start deterministisch. Im regulären Betrieb wird `DateTime.Now` des Geräts
verwendet. Außerhalb der Nacht erscheint die erste Aktivität normalerweise innerhalb von 3,5 bis 16 Sekunden.

### Bedienung

- Das Diorama startet ohne sichtbares HUD. Eine Berührung blendet kurz `Bauen` ein.
- `Bauen` öffnet eine Smartphone-Palette mit 46-dp-Touch-Zielen, zwei Objektspalten und Seitenwechsel.
  Ein Objekt wählen, auf den Boden tippen, bei Bedarf drehen und mit `Setzen` bestätigen.
- Ein platziertes Objekt ohne aktive Vorschau antippen, um es zu verschieben, zu drehen oder zu löschen.
- `Abbruch` ändert das Layout nicht; `Undo` und `Redo` enthalten maximal 40 bestätigte Aktionen.
- `Fertig` schließt den Baumodus vollständig. Die Kamera bleibt stabil und die NPC-Simulation läuft weiter.

Für Entwicklungsbuilds kann das separate Debug-Menü ausdrücklich über `-winterDebugUI` aktiviert werden.

### Debug-Zeit

Eine beliebige Uhrzeit kann in einem Development-Player per Kommandozeile gesetzt werden:

```text
-winterTime=19.0
```

Im Editor kann `TimeOfDayController.SetDebugTime(float)` verwendet werden. Ohne Override gilt immer die lokale
Systemzeit.

## Architektur

- `SceneController`: Laufzeitaufbau und Verdrahtung von Terrain, Simulation und Bau-Subsystem
- `VoxelVolume`, `VoxelGreedyMesher`, `VoxelAssetGenerator`: 0,15-m-Raster, verdeckte Flächen entfernt,
  coplanare Flächen zu Quads zusammengefasst
- `TimeOfDayController`: lokale Uhr, fünf Tagesphasen und Debug-Override
- `LightingController`: kontinuierliche Übergänge zwischen Nacht, Morgen, Nachmittag, Abend und Marktschluss
- `BuildModeController`: Touch-Baumodus, 150 × 150 m Baubereich mit 0,60-m-Raster, Vorschau,
  Validierung, Auswahl und Undo/Redo
- `BuildObjectLibrary`, `BuildObjectVisual`: erweiterbarer Objektkatalog und prozedurale Voxelmodelle
- `BuildLayoutStore`: atomar geschriebene, versionierte JSON-Datei unter `Application.persistentDataPath`
- `PathSystem`: benannte Routen plus dynamische Hindernisse und Interaktionsziele aus dem Layout
- `NPCDirector`: Pooling, Aktivitätsdichte und gewichtete Rollenwahl
- `NPCBehaviour`: erweiterbare Zustandsmaschine, individuelle Kleidung/Körperproportionen,
  Abstandhalten und seltene Begegnungspausen
- `AmbientController`: mehrschichtiger Schneefall und lokal synthetisierte Wind-/Schrittgeräusche
- `QualityController`: Standard-/High-Profil, Render Scale, Schatten, SSAO und Partikellimits
- `LocalSettings`: ausschließlich lokale `PlayerPrefs` für globale Einstellungen
- `FrameDiagnostics`: periodische Framerate- und zehnminütige Soak-Test-Meldungen

Die erwachsenen NPC-Rollen sind Passant, Besucher, Standbetreiber, Lieferant und Reinigungskraft. Kinder erscheinen
nur als kleinere Begleitung eines Passanten oder Besuchers und folgen diesem durch die Szene. Beobachtbare Abläufe
umfassen Durchqueren, Verweilen, Standbesuch, Lieferung und abendliches Aufräumen.

## Rendering und Mobile-Profile

`Standard` zielt auf 30 FPS, 0,85 Render Scale, 1024er Hauptlichtschatten, reduzierte lokale Lichter und dezentes
SSAO. `High` zielt auf 60 FPS, volle Render Scale, 2048er Schatten und zusätzliche Lichter. Lokale Punktlichter werfen
keine Schatten; das Directional Light verwendet weiche dynamische Schatten.

## Layout und Navigation

Das Startlayout enthält die bisherigen Bäckerei- und Getränkestände, Baum, Laternen, Bank, Kisten und Karren
als bearbeitbare Datensätze. Ihre V0.2-Proportionen und Voxelgeometrie werden unverändert übernommen; ihre
Objektursprünge werden einheitlich auf das 0,60-m-Raster ausgerichtet. Der 150 × 150 m große Baubereich verwendet
dieses Raster durchgängig. Die Bibliothek enthält zusätzlich
Kakaostand, kleine Tanne, Schneemann, Feuerkorb, Zaun, Lichterdekoration und Schneepfade. Fußabdrücke verhindern
Überlappungen; nur der bestehende öffentliche Querweg bleibt als Zugang frei. Neue Pfade müssen am öffentlichen
Weg beginnen oder an einen bestehenden Pfad anschließen.

Bestätigte Bauten aktualisieren Hindernisse, Besuchsziele und Lichtquellen. NPCs erkennen den
Navigationswechsel, verlassen neue Hindernisflächen ohne Teleportation und wählen dynamische Stand-, Bank- oder
Feuerkorb-Ziele nur gelegentlich. Nicht erreichbare Ziele werden über die bestehende Routenauswahl übersprungen.

Die Datei `wintermarkt-layout.json` enthält IDs, Typen, 0,60-m-Positionen für neue Änderungen, 90°-Rotationen,
Varianten, Darstellungseinstellungen und eine Formatversion. V1- und V2-Dateien werden beim Laden migriert;
alle Objektursprünge werden dabei auf das Raster normalisiert. Beim Speichern wird zuerst eine temporäre Datei
geschrieben; eine Sicherung wird als `wintermarkt-layout.backup.json` gehalten. Defekte Hauptdateien fallen auf
diese Sicherung oder das Standardlayout zurück. Die Undo-Historie wird absichtlich nicht persistent gespeichert.

## Automatisierung

Im Unity-Menü `Wintermarkt` stehen folgende Befehle bereit:

- `Prepare Project`: URP, Android Player Settings und Startszene erzeugen
- `Capture V0.3 Screenshots`: vier Diorama-Renderings und eine Baumodus-Platzierung schreiben
- `Run Functional Checks`: Mesh-, Zeit-, Rollen-, Szenen-, Baugrundlagen- und Offline-Prüfungen
- `Build Android APK`: Development-APK für ARM64 erzeugen

Kommandozeilenbeispiel:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe" `
  -batchmode -quit -projectPath . `
  -executeMethod WinterMarket.Editor.WinterMarketProjectBuilder.BuildAndroid `
  -logFile android-build.log
```

Ausgaben:

- APK: `Builds/Android/Wintermarkt-v0.3.apk`
- Screenshots: `Artifacts/Screenshots`
- V0.1-Vergleichsaufnahme: `Artifacts/Video/wintermarkt-v0.1-device.mp4`
- Testbericht: `Docs/TEST_REPORT.md`

Die Android-Startaktivität ist `com.unity3d.player.UnityPlayerGameActivity`. Installation per ADB:

```powershell
& "$env:UNITY_ANDROID_SDK\platform-tools\adb.exe" install -r Builds\Android\Wintermarkt-v0.3.apk
& "$env:UNITY_ANDROID_SDK\platform-tools\adb.exe" shell am start -n com.felixseltmann.wintermarkt/com.unity3d.player.UnityPlayerGameActivity
```

## Offene Prüfungspunkte von V0.3

- Auf einem Android-Gerät sind der zehnminütige individuelle Diorama-Soak-Test und die 30-FPS-Messung erneut
  durchzuführen; hierfür liegt noch kein V0.3-Gerätenachweis vor.
- Die Baumodus-UI nutzt absichtlich Unity-IMGUI, um keine dauerhafte Canvas-Instanz und keine externen Assets
  in den Diorama-Modus einzuführen. Eine spätere V0.4 könnte sie ohne Datenmigration durch uGUI ersetzen.
- Keine freie Kamera, Wirtschaft, Fortschritt, Online-Funktionen oder Monetarisierung.
