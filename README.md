# Wintermarkt – Diorama Prototype V0.2

Ein vollständig lokales, passiv beobachtbares Wintermarkt-Diorama für Android. Die Szene wird aus echten
Voxel-Meshes aufgebaut, folgt der lokalen Gerätezeit und simuliert ruhige Alltagssituationen mit bis zu sechs NPCs.
V0.2 ergänzt handgearbeitete Buden, gestufte Schneekanten am Baum, getrennte Emissions-Voxel, dezente AO und
vielfältigere Figurenbewegungen.

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

- Das Diorama benötigt keine Eingabe.
- Der Diorama-Modus enthält kein sichtbares HUD und reagiert nicht mit einem Einstellungsmenü auf Berührung.
- Die Kamera ist absichtlich stabil und nicht frei drehbar.

Für Entwicklungsbuilds kann das separate Debug-Menü ausdrücklich über `-winterDebugUI` aktiviert werden.

### Debug-Zeit

Eine beliebige Uhrzeit kann in einem Development-Player per Kommandozeile gesetzt werden:

```text
-winterTime=19.0
```

Im Editor kann `TimeOfDayController.SetDebugTime(float)` verwendet werden. Ohne Override gilt immer die lokale
Systemzeit.

## Architektur

- `SceneController`: Laufzeitaufbau und Verdrahtung des Dioramas
- `VoxelVolume`, `VoxelGreedyMesher`, `VoxelAssetGenerator`: 0,15-m-Raster, verdeckte Flächen entfernt,
  coplanare Flächen zu Quads zusammengefasst
- `TimeOfDayController`: lokale Uhr, fünf Tagesphasen und Debug-Override
- `LightingController`: kontinuierliche Übergänge zwischen Nacht, Morgen, Nachmittag, Abend und Marktschluss
- `PathSystem`: benannte, plausible Routen und Ziele
- `NPCDirector`: Pooling, Aktivitätsdichte und gewichtete Rollenwahl
- `NPCBehaviour`: erweiterbare Zustandsmaschine, individuelle Kleidung/Körperproportionen,
  Abstandhalten und seltene Begegnungspausen
- `AmbientController`: mehrschichtiger Schneefall und lokal synthetisierte Wind-/Schrittgeräusche
- `QualityController`: Standard-/High-Profil, Render Scale, Schatten, SSAO und Partikellimits
- `LocalSettings`: ausschließlich lokale `PlayerPrefs`
- `FrameDiagnostics`: periodische Framerate- und zehnminütige Soak-Test-Meldungen

Die erwachsenen NPC-Rollen sind Passant, Besucher, Standbetreiber, Lieferant und Reinigungskraft. Kinder erscheinen
nur als kleinere Begleitung eines Passanten oder Besuchers und folgen diesem durch die Szene. Beobachtbare Abläufe
umfassen Durchqueren, Verweilen, Standbesuch, Lieferung und abendliches Aufräumen.

## Rendering und Mobile-Profile

`Standard` zielt auf 30 FPS, 0,85 Render Scale, 1024er Hauptlichtschatten, reduzierte lokale Lichter und dezentes
SSAO. `High` zielt auf 60 FPS, volle Render Scale, 2048er Schatten und zusätzliche Lichter. Lokale Punktlichter werfen
keine Schatten; das Directional Light verwendet weiche dynamische Schatten.

## Automatisierung

Im Unity-Menü `Wintermarkt` stehen folgende Befehle bereit:

- `Prepare Project`: URP, Android Player Settings und Startszene erzeugen
- `Capture Four Times`: Renderings für 07:30, 14:00, 19:00 und 22:30 schreiben
- `Run Functional Checks`: Mesh-, Zeit-, Rollen-, Szenen- und Offline-Prüfungen
- `Build Android APK`: Development-APK für ARM64 erzeugen

Kommandozeilenbeispiel:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe" `
  -batchmode -quit -projectPath . `
  -executeMethod WinterMarket.Editor.WinterMarketProjectBuilder.BuildAndroid `
  -logFile android-build.log
```

Ausgaben:

- APK: `Builds/Android/Wintermarkt-v0.2.apk`
- Screenshots: `Artifacts/Screenshots`
- V0.1-Vergleichsaufnahme: `Artifacts/Video/wintermarkt-v0.1-device.mp4`
- Testbericht: `Docs/TEST_REPORT.md`

Die Android-Startaktivität ist `com.unity3d.player.UnityPlayerGameActivity`. Installation per ADB:

```powershell
& "$env:UNITY_ANDROID_SDK\platform-tools\adb.exe" install -r Builds\Android\Wintermarkt-v0.2.apk
& "$env:UNITY_ANDROID_SDK\platform-tools\adb.exe" shell am start -n com.felixseltmann.wintermarkt/com.unity3d.player.UnityPlayerGameActivity
```

## Bekannte Grenzen von V0.2

- Keine freie Kamera oder spielerische Progression
- Keine Wirtschaftssimulation oder fortlaufende Hintergrundsimulation
- Laufanimationen und Alltagstätigkeiten sind bewusst einfache prozedurale Voxelanimationen
- Audio ist synthetisiert und atmosphärisch reduziert; es gibt keine Musik
- Dynamische globale Beleuchtung und Raytracing werden bewusst nicht verwendet
