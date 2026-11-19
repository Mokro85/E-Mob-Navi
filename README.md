# EvNav – Offline-EV-Navigation für Windows (C# / WPF, .NET 8)

Navigations-App für Windows mit OpenStreetMap-Kartendaten, Ladesäulen-Anzeige entlang der
Route und automatischer Ladestopp-Planung für Elektroautos. **Komplett offline nutzbar** –
kein NuGet-Paket, keine Internetverbindung nötig. OpenRouteService (ORS) und OpenChargeMap
(OCM) sind **optionale** Online-Erweiterungen.

## Funktionen

- **Vektorkarte aus OSM-Daten** (eigener Renderer, kein Tile-Server): Straßenklassen-farbig,
  Zoom (Mausrad), Verschieben (Ziehen), Mercator-Projektion
- **A*-Routing** über einen lokalen Straßengraph (Zeitoptimum, Geschwindigkeiten je
  Straßenklasse, Einbahn-Handling, Option „Autobahn meiden")
- **Ladesäulen-Anzeige** entlang der Route (aus OSM `amenity=charging_station`, optional aus
  OpenChargeMap) mit Filter nach Steckertyp (CCS / Typ 2 / CHAdeMO) und Mindestleistung
- **Ladestopp-Planung** (ABRP-Prinzip): Reichweitenfenster aus Batterie, Verbrauch und SoC;
  Wahl der Station nach Fortschritt/Ladeleistung/Umweg; Ladung bis Ladegrenze bzw. so weit
  nötig, um das Ziel mit gewünschtem Rest-SoC zu erreichen
- **Ausgabe pro Stopp**: Position (km), Umweg, Ankunfts-/Abfahrts-SoC, geladene kWh, Ladedauer
- **Suche** nach Straßen-/Ortsnamen aus der geladenen OSM-Datei, Start/Ziel per Klick
- **Fahrzeugprofile** und Einstellungen werden unter `%APPDATA%\EvNav\settings.json` gespeichert

## Build & Start

Voraussetzung: Windows 10/11 und [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd EvNav
dotnet run
```

Erster Test: `OSM-Datei laden` → `samples\demo.osm` (synthetische Kleinstadt mit 4 Ladestationen),
Start/Ziel per Klick auf Straßen setzen, dann „Route + Ladestopps berechnen".

## Echte Kartendaten (OpenStreetMap)

Die App liest **OSM-XML** (`.osm`, auch gzip `.osm.gz`). Empfehlungen:

1. **Regional-Auszug von Geofabrik**: <https://download.geofabrik.de/europe/germany.html>
   (Datei `.osm.pbf`), dann in XML wandeln:
   ```bash
   osmium cat bayern-latest.osm.pbf -o bayern.osm
   ```
   (osmium-tool; alternativ `osmconvert bayern-latest.osm.pbf -o=bayern.osm`)
2. **Kleiner Ausschnitt über Overpass Turbo**: `Export → raw data → als XML` speichern.

Hinweis: Für ganze Bundesländer genügt ein Rechner mit 8 GB RAM; der Graph liegt im Speicher.

## Optionale Online-Dienste

| Dienst | Zweck | Key |
|---|---|---|
| [OpenRouteService](https://openrouteservice.org/) | Routen besseren Kartenmaterials statt lokalem Graph | Kostenlos, `Authorization`-Header |
| [OpenChargeMap](https://openchargemap.org/site/develop/api) | Frischere/umfangreichere Ladesäulen-Daten | Kostenlos, URL-Parameter `key` |

Beide sind optional: ohne Key läuft alles rein offline aus der geladenen OSM-Datei.

## Lizenz-/Pflichtangaben

- Kartendaten: © [OpenStreetMap-Mitwirkende](https://www.openstreetmap.org/copyright),
  Lizenz ODbL – Copyright-Vermerk bei Weitergabe sichtbar mitliefern
- Ladesäulen via OpenChargeMap: Lizenzbedingungen von OCM beachten

## Architektur

```
Models/            GeoPoint, VehicleProfile, ChargingStation, RouteResult, TripPlan …
Services/
  OsmImporter      Streaming-Parser (XmlReader) für .osm/.osm.gz: Knoten, Wege,
                   Ladesäulen (Leistung/Steckertypen/Capacity), Suchindex
  RoadGraph        Straßengraph + Rasterindex für „nächster Straßenknoten"
  RoutingService   A* auf Zeit (min), Heuristik = Vogelflug/Max-Tempo
  StationService   Rasterindex der Ladestationen; OptionFinder liefert Kandidaten
                   entlang der Route (exakter A*-Umweg im Offline-Modus)
  TripPlanner      SoC-Simulation & Stopp-Wahl (greedy: Fortschritt + Leistung − Umweg)
  OnlineServices   ORS-Directions & OCM-POI (nur bei gesetztem Key)
Controls/          MapControl (WPF-FrameworkElement, eigener Vektor-Renderer) + MapData
MainWindow         UI-Verdrahtung, Profileingabe, Stoppliste
```

## Vereinfachungen (v1)

- Verbrauch konstant über alle Straßenklassen (kein Höhenprofil, keine Temperatur)
- Ladeprofil als Durchschnittsfaktor (65 % der Nennleistung) statt echter Ladekurve
- Kein Live-Verkehr, keine Sperrungen
- Umweg-Präzision: Offline exakt per A*, Online (ORS) als Schätzung (Faktor 1.4)

Mögliche Erweiterungen: Ladekurven-Modelle pro Fahrzeug, Höhenprofil (SRTM),
GPX-Export, mehrere Routenalternativen, Live-Verkehr.
