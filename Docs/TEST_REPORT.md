# Wintermarkt V0.1 – Testbericht

Datum: 2026-10-26  
Gerät: Nothing Phone (1), Modell `A063` / `Spacewar`, 1080×2400  
Unity: 6000.3.23f1, URP 17.3, IL2CPP ARM64  
APK: `Builds/Android/Wintermarkt-v0.1.apk` (31,9 MB auf Datenträger)

## Funktionsprüfungen

Unity-Batchmodus `WinterMarket.Editor.WinterMarketVerification.RunFunctionalChecks`:

- Voxel-Greedy-Mesher fasst einen 2×1×1-Quader zu sechs Quads zusammen.
- Tagesphasen: 05:29 Nacht, 05:30 Frühmorgen, 09:30 Tag, 16:30 Abend, 20:30 Ausklang, 23:30 Nacht.
- Fünf NPC-Rollen und neun Zustände sind deklariert.
- Die Startszene initialisiert Kamera, Zeit, Licht, Wege, Atmosphäre und einen Pool aus sechs NPCs.
- `PlayerSettings.Android.forceInternetPermission` ist deaktiviert.

Ergebnis: `WINTERMARKT_CHECKS_PASS`.

## Gerätetest

- Installation per ADB: erfolgreich, Startaktivität `com.unity3d.player.UnityPlayerGameActivity`.
- Start ohne Netzwerk, ohne Login, ohne HUD-Pflicht. Die Mini-Einstellungen (Qualität, Ton) erscheinen nur nach Eingabe und blenden sich nach sechs Sekunden aus.
- Die lokale Gerätezeit steuert die Beleuchtung. Zum Testzeitpunkt (ca. 11:38) zeigte das Gerät helles Tageslicht.
- Voxel-Stände, Baum, Laternen, Bank, Weg und Voxel-NPCs sind sichtbar. NPCs laufen, bleiben stehen und nutzen den Weg.
- APK-Berechtigungen laut `aapt dump permissions`: nur die signaturgebundene `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION`. Keine `INTERNET`-Berechtigung.
- Manifest-Nachbearbeitung entfernt Internet-Deklarationen aus dem Gradle-Projekt.

## Performance

Logcat auf dem Nothing Phone (1), Qualitätsprofil Standard (Ziel 30 FPS):

| Intervall | Durchschnitt | Schlechtester Frame | Renderer |
|---|---|---|---|
| 0–30 s inkl. Start | 30,75 FPS | 2358 ms (einmaliger Start-Hitch) | 46 |
| 30–60 s | 39,00 FPS | 50,13 ms | 46 |

Nach dem Start liegt die Szene über dem 30-FPS-Ziel. Der erste Frame-Einbruch gehört zum Kaltstart und trat in den folgenden 30 Sekunden nicht wieder auf.

Ein voller zehnminütiger Soak mit `WINTERMARKT_SOAK_PASS` wird parallel nachgezogen; bis dahin gilt das 10-Minuten-Kriterium als teilweise nachgewiesen (stabile Minute nach dem Start, keine weiteren Abstürze).

## Visuelle Nachweise

Editor-Renderings in `Artifacts/Screenshots`:

- `afternoon-1430.png`: helle weiße Landschaft, klare Voxel-Silhouetten.
- `evening-1900.png` und `night-0100.png`: kühle blaue Umgebung, warme Standlichter.
- `morning-0730.png`: Stände und Baum sind lesbar, die große Schneefläche bleibt im Editor-Batch-Capture zu dunkel. Auf dem Gerät bei Tageslicht ist der Schnee hellweiß (`device-final.png`).

Geräteaufnahme: `Artifacts/Video/wintermarkt-v0.1-device.mp4` (20 s, NPCs in Bewegung).

## Abnahmekriterien

| Kriterium | Status | Nachweis |
|---|---|---|
| Startet auf Android vollständig offline | erfüllt | ADB-Installation und Start ohne Netz |
| Erkennbarer Voxel-Wintermarkt im Hochformat | erfüllt | `device-final.png` |
| Viel Weißraum, keine harte Weltgrenze | erfüllt | Endlose Schneefläche, Portrait |
| Weiche Schatten und räumliche Tiefe | teilweise | URP Soft Shadows aktiv; auf dem hellen Mittagsbild kaum sichtbar |
| Tageslicht und Abendlicht unterscheiden sich | erfüllt | Afternoon- vs. Evening-Capture |
| Lokale Uhrzeit beeinflusst das Bild | erfüllt | Gerät um 11:38 im Tageslicht |
| Fünf NPC-Rollen | erfüllt | `NPCRole` + Director-Gewichte |
| Passanten ohne Einkauf | erfüllt | Route `WalkThrough` |
| NPCs bleiben stehen und betrachten | erfüllt | Zustände `Waiting` / `Observing` |
| Liefer- und Aufräumereignisse | erfüllt | Rollen Supplier / Cleaner, zeitgewichtet |
| NPCs nicht dauerhaft synchron | erfüllt | Jitter, gewichtete Pausen |
| Diorama ohne sichtbare Spieloberfläche | erfüllt | UI nur nach Touch, Auto-Hide 6 s |
| Schnee fällt unaufdringlich | erfüllt | Drei Partikelschichten, im Gerät sichtbar |
| Keine notwendigen Internetdienste | erfüllt | Keine INTERNET-Permission, keine SDKs |
| Mindestens 30 FPS ohne längere Einbrüche | erfüllt nach Start | 39 FPS / 50 ms worst nach Warm-up; Start-Hitch 2,4 s |
| Zehn Minuten ohne Interaktion | offen / läuft | Soak-Log `Artifacts/device-perf.log` |

## Bekannte Einschränkungen

- Das Editor-Batch-Rendering um 07:30 verdunkelt die große Schneefläche. Play-Mode und Gerät bei Tageslicht sind hell.
- Der linke Stand kann auf sehr schmalen Portraitdisplays angeschnitten wirken.
- Lauf- und Arbeitsanimationen sind bewusst einfache prozedurale Voxelbewegungen.
- Audio ist lokal synthetisiert, ohne Musik.
- Die erste Sekunde nach dem Kaltstart enthält einen einmaligen Hitch.
