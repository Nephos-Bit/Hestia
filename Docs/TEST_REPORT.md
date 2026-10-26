# Wintermarkt V0.2 – Ist-Zustand und Testbericht

Datum: 2026-10-26  
Zielplattform: Android, lokal/offline, Hochformat

## Ist-Zustand vor V0.2

- V0.1 hatte einen prozedural erzeugten Voxelmarkt mit zwei Buden, Baum, orthografischer Kamera,
  lokaler Gerätezeit, weichen Hauptlichtschatten, fünf NPC-Rollen und einem Pool von sechs Figuren.
- Die frühere Darstellung ließ ganze Meshes anstelle einzelner Lichtquellen emittieren. Die Buden,
  der Baum, NPC-Proportionen und Laufwege waren noch zu gleichförmig.
- Das berührbare Einstellungsfeld war im regulären Betrachtungsmodus sichtbar. Der Android-Offlinebetrieb
  und das 30-FPS-Ziel waren in V0.1 auf einem Nothing Phone (1) bereits teilweise nachgewiesen.

## Umgesetzt in V0.2

- Beide Buden haben nun tragende Pfosten, tiefe Verkaufsöffnungen, Tresen, sichtbare Dachschichten,
  Schneeauflagen, reduzierte Girlanden und wenige eindeutige Waren.
- Der zentrale Baum hat eine gestufte Voxel-Silhouette, schneebedeckte Stufen, einzelne Dekorationen und
  getrennte Licht-Voxel.
- Fenster, Laternen und Baumlichter emittieren als getrennte Meshes. Das verhindert flächiges Leuchten von
  Holz und Metall. Weiche Hauptlichtschatten, leicht verstärkte Kontaktschatten und dezentes SSAO sind aktiv.
- Die Marktstände wurden weiter nach außen gesetzt, der Baum hat einen eigenen Mittelraum und die Kamera
  wurde entsprechend weiter gefasst.
- Das reguläre Diorama erzeugt kein HUD. Das Debug-Menü erfordert explizit `-winterDebugUI`.
- NPCs variieren nun Mantel-, Mützen- und Schal-Farben, Breite, Größe, Tempo und Animationsphase. Getrennte
  Routen verteilen sie über den Weg; statische Kollisionsflächen verhindern das Durchlaufen von Buden,
  Baum und Lieferobjekten. Seltene Pausen zweier Figuren bilden kurze Begegnungen.
- Die Beleuchtung enthält eine eigene Marktschluss-Stimmung um 22:30. Die vier Capture-Zeitpunkte sind
  07:30, 14:00, 19:00 und 22:30; ohne Debug-Override gilt weiterhin die lokale Gerätezeit.

## Automatisierte Prüfungen

| Prüfung | Status | Nachweis |
|---|---|---|
| C#-Kompilierung Laufzeit und Editor | erfüllt | `Assembly-CSharp.csproj` und `Assembly-CSharp-Editor.csproj` per MSBuild kompiliert; nur zwei bestehende Unity-6-Deprecation-Warnungen in `WinterMarketProjectBuilder`. |
| Voxel-Greedy-Meshing | offen | `WinterMarketVerification.RunFunctionalChecks` vorhanden; Batchlauf konnte wegen Unity-Lizenzdienst nicht abschließen. |
| Tagesphasen inklusive 22:30 | offen | Funktionstest deckt 05:29, 05:30, 09:30, 16:30, 20:30, 22:30 und 23:30 ab. |
| Startmodule, sechs NPCs, Offline-Flag | offen | Funktionstest vorhanden; erneute Unity-Ausführung erforderlich. |
| HUD-freier Diorama-Modus | Code geprüft | `SceneController` erzeugt `DioramaUIController` nur mit `-winterDebugUI`. |
| Vier V0.2-Screenshots | offen | `Capture Four Times` schreibt `morning-0730.png`, `afternoon-1400.png`, `evening-1900.png`, `closing-2230.png`. |
| Zwei Minuten NPC-Aufnahme | offen | Kein Laufzeit-/Aufnahmezugriff verfügbar. |
| Offlinebetrieb im Flugmodus | offen | Kein Android-Gerät im aktuellen Lauf verfügbar. |
| Android-Performance, Ziel 30 FPS | offen | Der V0.1-Gerätetest ist kein Nachweis für die geänderte V0.2-Szene. |

## Einschränkung der aktuellen Verifikation

Der gestartete Unity-Batchlauf wartete auf den lokalen Unity-Lizenzdienst und führte innerhalb des verfügbaren
Laufs keine Methoden aus. Es wurden deshalb keine V0.2-Screenshots, APKs oder Gerätewerte als bestanden markiert.
Die vorhandenen V0.1-Artefakte bleiben unverändert und gelten nicht als V0.2-Nachweis.

## Ausstehende Abnahme

1. Unity-Lizenzdienst herstellen und `Wintermarkt/Run Functional Checks` ausführen.
2. `Wintermarkt/Capture Four Times` ausführen und die vier erzeugten Bilder visuell prüfen.
3. V0.2-APK auf Android installieren, zwei Minuten aufzeichnen sowie Flugmodus und Framerate messen.
