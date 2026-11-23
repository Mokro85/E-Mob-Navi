using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using EvNav.Controls;
using EvNav.Models;
using EvNav.Services;
using Microsoft.Win32;

namespace EvNav;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = SettingsStore.Load();
    private OsmImportResult? _osm;
    private RoadGraph? _graph;
    private StationService? _stations;
    private List<NamedPlace> _searchIndex = new();
    private NamedPlace? _selectedPlace;
    private GeoPoint? _start;
    private GeoPoint? _end;
    private int _picking;

    private RouteResult? _lastRoute;
    private List<ChargingStop>? _lastStops;
    private List<ChargingStation>? _lastHighlight;

    public MainWindow()
    {
        InitializeComponent();
        Map.GeoPicked += Map_GeoPicked;
        ProfileToUi(_settings.Vehicle);
        OrsKeyBox.Text = _settings.OrsApiKey;
        OcmKeyBox.Text = _settings.OcmApiKey;
        OnlineCheck.IsChecked = _settings.UseOnline;
        Loaded += async (_, _) =>
        {
            if (_settings.LastOsmFile.Length > 0 && File.Exists(_settings.LastOsmFile))
                await LoadOsmAsync(_settings.LastOsmFile);
        };
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void ProfileToUi(VehicleProfile v)
    {
        var inv = CultureInfo.InvariantCulture;
        BatteryBox.Text = v.BatteryKwh.ToString(inv);
        ConsumptionBox.Text = v.ConsumptionKwhPer100Km.ToString(inv);
        MaxDcBox.Text = v.MaxDcPowerKw.ToString(inv);
        StartSocBox.Text = v.StartSocPct.ToString(inv);
        MinArrivalBox.Text = v.MinArrivalSocPct.ToString(inv);
        MaxChargeBox.Text = v.MaxChargeSocPct.ToString(inv);
        MinDestBox.Text = v.MinDestinationSocPct.ToString(inv);
        MaxDetourBox.Text = v.MaxDetourKm.ToString(inv);
        MinPowerBox.Text = v.MinStationPowerKw.ToString(inv);
        CcsCheck.IsChecked = v.Ccs;
        Type2Check.IsChecked = v.Type2Ac;
        ChademoCheck.IsChecked = v.Chademo;
    }

    private bool TryNum(TextBox box, out double value)
    {
        var s = box.Text.Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private VehicleProfile ReadProfile()
    {
        var p = new VehicleProfile { Name = _settings.Vehicle.Name };
        if (TryNum(BatteryBox, out var v)) p.BatteryKwh = v;
        if (TryNum(ConsumptionBox, out v)) p.ConsumptionKwhPer100Km = v;
        if (TryNum(MaxDcBox, out v)) p.MaxDcPowerKw = v;
        if (TryNum(StartSocBox, out v)) p.StartSocPct = v;
        if (TryNum(MinArrivalBox, out v)) p.MinArrivalSocPct = v;
        if (TryNum(MaxChargeBox, out v)) p.MaxChargeSocPct = v;
        if (TryNum(MinDestBox, out v)) p.MinDestinationSocPct = v;
        if (TryNum(MaxDetourBox, out v)) p.MaxDetourKm = v;
        if (TryNum(MinPowerBox, out v)) p.MinStationPowerKw = v;
        p.Ccs = CcsCheck.IsChecked == true;
        p.Type2Ac = Type2Check.IsChecked == true;
        p.Chademo = ChademoCheck.IsChecked == true;
        return p;
    }

    private async void LoadMap_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "OSM-Daten (*.osm;*.osm.gz;*.xml)|*.osm;*.osm.gz;*.xml|Alle Dateien (*.*)|*.*"
        };
        if (_settings.LastOsmFile.Length > 0)
            dlg.InitialDirectory = Path.GetDirectoryName(_settings.LastOsmFile);
        if (dlg.ShowDialog(this) != true) return;
        await LoadOsmAsync(dlg.FileName);
    }

    private async Task LoadOsmAsync(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            Stream stream = fs;
            byte[] head = new byte[2];
            int read = fs.Read(head, 0, 2);
            fs.Seek(0, SeekOrigin.Begin);
            if (read == 2 && head[0] == 0x1f && head[1] == 0x8b)
                stream = new GZipStream(fs, CompressionMode.Decompress);
            await LoadFromStreamAsync(stream, Path.GetFileName(path), path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler beim Laden der OSM-Datei",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Fehler beim Laden der OSM-Datei.");
        }
    }

    private async Task LoadFromStreamAsync(Stream stream, string label, string? rememberPath)
    {
        SetStatus($"Importiere {label} …");
        LoadMapButton.IsEnabled = false;
        DownloadAreaButton.IsEnabled = false;
        try
        {
            var result = await Task.Run(() => OsmImporter.Import(stream));
            var graph = await Task.Run(() => new RoadGraph(result));
            _osm = result;
            _graph = graph;
            _stations = new StationService(result.Stations);
            _searchIndex = result.Places.ToList();
            Map.Data = MapData.From(result, graph);
            MapInfoText.Text = $"{result.Highways.Count:N0} Wege · {result.Nodes.Count:N0} Knoten · " +
                               $"{result.Stations.Count} Ladestationen · {result.Places.Count:N0} Suchbegriffe";

            var sample = new List<GeoPoint>(result.Stations.Count + result.Places.Count + 2 * result.Highways.Count);
            foreach (var st in result.Stations) sample.Add(st.Pos);
            foreach (var pl in result.Places) sample.Add(pl.Pos);
            foreach (var way in result.Highways)
            {
                if (way.NodeIds.Count == 0) continue;
                if (result.Nodes.TryGetValue(way.NodeIds[0], out var p1)) sample.Add(p1);
                if (result.Nodes.TryGetValue(way.NodeIds[^1], out var p2)) sample.Add(p2);
            }
            Map.FitTo(sample);

            if (rememberPath is not null)
            {
                _settings.LastOsmFile = rememberPath;
                SettingsStore.Save(_settings);
            }
            SetStatus($"Karte geladen: {label}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler beim Laden der Kartendaten",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Fehler beim Laden der Kartendaten.");
        }
        finally
        {
            LoadMapButton.IsEnabled = true;
            DownloadAreaButton.IsEnabled = true;
        }
    }

    private static readonly HttpClient _overpassHttp = new() { Timeout = TimeSpan.FromSeconds(120) };

    private async void DownloadArea_Click(object sender, RoutedEventArgs e)
    {
        var (minLat, minLon, maxLat, maxLon) = Map.CurrentView();
        double kmY = (maxLat - minLat) * 111.0;
        double kmX = (maxLon - minLon) * 111.0 * Math.Max(0.2, Math.Cos((minLat + maxLat) / 2 * Math.PI / 180));
        if (kmX > 45 || kmY > 45)
        {
            MessageBox.Show(this,
                $"Der Kartenbereich ist zu groß ({kmX:F0} × {kmY:F0} km). Bitte hineinzoomen (max. ca. 45 km pro Seite).",
                "Overpass-Download", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DownloadAreaButton.IsEnabled = false;
        try
        {
            SetStatus("Lade Kartenbereich von Overpass (OpenStreetMap) …");
            var query = BuildOverpassQuery(minLat, minLon, maxLat, maxLon);
            using var content = new StringContent(query, Encoding.UTF8, "text/plain");
            using var resp = await _overpassHttp.PostAsync("https://overpass-api.de/api/interpreter", content);
            resp.EnsureSuccessStatusCode();
            var stream = await resp.Content.ReadAsStreamAsync();
            await LoadFromStreamAsync(stream, "Overpass-Bereich", null);
            SetStatus("Kartenbereich geladen (Quelle: © OpenStreetMap-Mitwirkende, ODbL).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Overpass-Download fehlgeschlagen",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Overpass-Download fehlgeschlagen.");
        }
        finally
        {
            DownloadAreaButton.IsEnabled = true;
        }
    }

    private static string BuildOverpassQuery(double minLat, double minLon, double maxLat, double maxLon)
    {
        var inv = CultureInfo.InvariantCulture;
        string box = $"{minLat.ToString(inv)},{minLon.ToString(inv)},{maxLat.ToString(inv)},{maxLon.ToString(inv)}";
        return "[out:xml][timeout:90];(way[\"highway\"](" + box + ");" +
               "node[\"amenity\"=\"charging_station\"](" + box + "););(._;>;);out body;";
    }

    private void TileCheck_Changed(object sender, RoutedEventArgs e)
    {
        Map.ShowOnlineTiles = TileCheck.IsChecked == true;
        Map.InvalidateVisual();
        if (Map.ShowOnlineTiles)
            SetStatus("Online-Kartenbilder aktiv (© OpenStreetMap-Mitwirkende, zwischengespeichert).");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        if (q.Length < 2 || _searchIndex.Count == 0)
        {
            SearchResults.ItemsSource = null;
            SearchResults.Visibility = Visibility.Collapsed;
            return;
        }
        var items = _searchIndex
            .Where(p => p.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .Concat(_searchIndex.Where(p => !p.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                                            && p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Take(25)
            .Select(p => new SearchItem
            {
                Label = p.Name + (p.Kind == "Straße" ? "  (Straße)" : ""),
                Place = p
            })
            .ToList();
        SearchResults.ItemsSource = items;
        SearchResults.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is SearchItem item)
        {
            _selectedPlace = item.Place;
            Map.CenterOn(item.Place.Pos);
            SetStatus($"Ausgewählt: {item.Place.Name} ({item.Place.Kind}) – 'Suche = Start/Ziel' nutzen.");
        }
    }

    private void UseSearchAsStart_Click(object sender, RoutedEventArgs e) => SetPoint(true, _selectedPlace?.Pos);

    private void UseSearchAsEnd_Click(object sender, RoutedEventArgs e) => SetPoint(false, _selectedPlace?.Pos);

    private void PickStart_Click(object sender, RoutedEventArgs e)
    {
        _picking = 1;
        Map.SetPickMode(true);
        StartLabel.FontWeight = FontWeights.Bold;
        SetStatus("Klicken Sie auf eine Straße der Karte, um den START zu setzen.");
    }

    private void PickEnd_Click(object sender, RoutedEventArgs e)
    {
        _picking = 2;
        Map.SetPickMode(false);
        EndLabel.FontWeight = FontWeights.Bold;
        SetStatus("Klicken Sie auf eine Straße der Karte, um das ZIEL zu setzen.");
    }

    private void Map_GeoPicked(GeoPoint geo)
    {
        bool asStart = _picking == 1;
        _picking = 0;
        StartLabel.FontWeight = FontWeights.Normal;
        EndLabel.FontWeight = FontWeights.Normal;
        SetPoint(asStart, geo);
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        (_start, _end) = (_end, _start);
        UpdatePointLabels();
        Map.SetOverlay(_lastRoute, _start, _end, _lastStops, _lastHighlight);
    }

    private void ZoomToRoute_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRoute is not null) Map.FitTo(_lastRoute.Points);
    }

    private void SetPoint(bool isStart, GeoPoint? geo)
    {
        if (geo is null)
        {
            SetStatus("Bitte Suchergebnis wählen oder auf der Karte klicken.");
            return;
        }
        if (_graph is not null)
        {
            int idx = _graph.NearestNode(geo.Value, 5.0);
            if (idx < 0)
            {
                SetStatus("Keine Straße in der Nähe (max. 5 km) – Punkt nicht gesetzt.");
                return;
            }
            geo = _graph.Positions[idx];
        }
        if (isStart) _start = geo; else _end = geo;
        UpdatePointLabels();
        Map.SetOverlay(_lastRoute, _start, _end, _lastStops, _lastHighlight);
        SetStatus((isStart ? "Start" : "Ziel") + " gesetzt: " + FmtPoint(geo.Value));
    }

    private void UpdatePointLabels()
    {
        StartLabel.Text = "Start: " + (_start is null ? "–" : FmtPoint(_start.Value));
        EndLabel.Text = "Ziel: " + (_end is null ? "–" : FmtPoint(_end.Value));
    }

    private static string FmtPoint(GeoPoint p)
        => p.Lat.ToString("F4", CultureInfo.InvariantCulture) + ", " +
           p.Lon.ToString("F4", CultureInfo.InvariantCulture);

    private async void Route_Click(object sender, RoutedEventArgs e)
    {
        if (_osm is null)
        {
            MessageBox.Show(this, "Bitte zuerst eine OSM-Datei laden (z. B. samples/demo.osm).",
                "EvNav", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_start is null || _end is null)
        {
            MessageBox.Show(this, "Bitte Start und Ziel setzen (Karten-Klick oder Suchergebnis).",
                "EvNav", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var profile = ReadProfile();
        if (profile.BatteryKwh < 5 || profile.ConsumptionKwhPer100Km < 3 || profile.MaxDcPowerKw < 3)
        {
            MessageBox.Show(this, "Batterie, Verbrauch und DC-Ladeleistung brauchen plausible Werte.",
                "Fahrzeugprofil", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.Vehicle = profile;
        _settings.OrsApiKey = OrsKeyBox.Text.Trim();
        _settings.OcmApiKey = OcmKeyBox.Text.Trim();
        _settings.UseOnline = OnlineCheck.IsChecked == true;
        SettingsStore.Save(_settings);

        bool online = _settings.UseOnline && _settings.OrsApiKey.Length > 0;
        bool avoidHighways = AvoidHighwaysCheck.IsChecked == true;

        RouteButton.IsEnabled = false;
        try
        {
            SetStatus("Berechne Route …");
            var start = _start.Value;
            var end = _end.Value;
            var graph = _graph;
            RouteResult? route = null;

            if (online)
            {
                try
                {
                    route = await OnlineServices.DirectionsAsync(_settings.OrsApiKey, start, end, avoidHighways);
                }
                catch (Exception ex)
                {
                    SetStatus($"ORS fehlgeschlagen ({ex.Message}) – Offline-Routing wird genutzt.");
                }
            }

            if (route is null && graph is not null)
            {
                route = await Task.Run(() =>
                {
                    int a = graph!.NearestNode(start, 5.0);
                    int b = graph.NearestNode(end, 5.0);
                    if (a < 0 || b < 0) return null;
                    return RoutingService.FindPath(graph, a, b, avoidHighways);
                });
            }

            if (route is null)
            {
                MessageBox.Show(this, "Keine Route gefunden. Start/Ziel evtl. nicht mit dem Straßennetz verbunden.",
                    "Routing", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetStatus("Keine Route gefunden.");
                return;
            }

            SetStatus($"Route: {route.DistanceKm:F0} km · {route.DurationMin:F0} min – suche Ladestationen …");

            var stations = new List<ChargingStation>(_osm.Stations);
            if (online && _settings.OcmApiKey.Length > 0)
            {
                try
                {
                    var (minLat, minLon, maxLat, maxLon) = BoundingBox(route.Points, 0.4);
                    var ocm = await OnlineServices.ChargingStationsInBboxAsync(
                        _settings.OcmApiKey, minLat, minLon, maxLat, maxLon);
                    stations.AddRange(ocm);
                }
                catch (Exception ex)
                {
                    SetStatus($"OpenChargeMap fehlgeschlagen ({ex.Message}) – nur lokale Ladesäulen.");
                }
            }

            bool useGraph = route.NodeIndices.Length > 0 && route.NodeIndices[0] >= 0 && graph is not null;
            var options = await Task.Run(() => OptionFinder.FindAlong(
                useGraph ? graph : null, route, stations, profile, 0.0, route.DistanceKm,
                useGraph ? _stations : null));

            var plan = await Task.Run(() => TripPlanner.Plan(route, options, profile));

            _lastRoute = route;
            _lastStops = plan.Stops.ToList();
            _lastHighlight = plan.Stops.Select(s => s.Station).ToList();
            StopsList.ItemsSource = plan.Stops.Select(s => new StopRow(s)).ToList();
            Map.SetOverlay(route, _start, _end, _lastStops, _lastHighlight);
            Map.FitTo(route.Points);

            if (plan.Success)
            {
                double totalMin = route.DurationMin + plan.ExtraDriveMin + plan.TotalChargeMin;
                SetStatus($"✓ {route.DistanceKm:F0} km · {plan.Stops.Count} Ladestopp(s) · " +
                          $"{FmtMin(plan.TotalChargeMin)} laden · Ankunft mit {plan.ArrivalSocPct:F0} % · " +
                          $"gesamt {FmtMin(totalMin)}");
            }
            else
            {
                SetStatus("⚠ Planung unvollständig: " + plan.Message);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler bei der Routenberechnung",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Fehler bei der Routenberechnung.");
        }
        finally
        {
            RouteButton.IsEnabled = true;
        }
    }

    private static (double MinLat, double MinLon, double MaxLat, double MaxLon) BoundingBox(
        IReadOnlyList<GeoPoint> pts, double paddingDeg)
    {
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        foreach (var p in pts)
        {
            minLat = Math.Min(minLat, p.Lat); maxLat = Math.Max(maxLat, p.Lat);
            minLon = Math.Min(minLon, p.Lon); maxLon = Math.Max(maxLon, p.Lon);
        }
        return (minLat - paddingDeg, minLon - paddingDeg, maxLat + paddingDeg, maxLon + paddingDeg);
    }

    private static string FmtMin(double minutes)
        => minutes >= 60 ? $"{(int)(minutes / 60)}:{minutes % 60:00} h" : $"{minutes:F0} min";
}

public sealed class SearchItem
{
    public required string Label { get; init; }
    public required NamedPlace Place { get; init; }
    public override string ToString() => Label;
}

public sealed class StopRow
{
    public StopRow(ChargingStop s)
    {
        var inv = CultureInfo.InvariantCulture;
        Index = s.Index;
        StationName = s.Station.Name +
            (s.Station.MaxPowerKw > 0 ? $" · {s.Station.MaxPowerKw:F0} kW" : "");
        AlongKm = s.AlongKm.ToString("F0", inv);
        DetourKm = "+ " + s.DetourKm.ToString("F1", inv) + " km";
        ArriveSoc = s.ArriveSocPct.ToString("F0", inv) + " %";
        DepartSoc = s.DepartSocPct.ToString("F0", inv) + " %";
        ChargeKwh = s.ChargeKwh.ToString("F1", inv);
        ChargeMin = s.ChargeMin.ToString("F0", inv);
    }

    public int Index { get; }
    public string StationName { get; }
    public string AlongKm { get; }
    public string DetourKm { get; }
    public string ArriveSoc { get; }
    public string DepartSoc { get; }
    public string ChargeKwh { get; }
    public string ChargeMin { get; }
}
