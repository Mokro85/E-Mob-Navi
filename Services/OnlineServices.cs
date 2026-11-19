using System.Net.Http;
using System.Text;
using System.Text.Json;
using EvNav.Models;

namespace EvNav.Services;

public static class OnlineServices
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static async Task<RouteResult?> DirectionsAsync(string apiKey, GeoPoint from, GeoPoint to,
        bool avoidMotorways, CancellationToken ct = default)
    {
        var body = new
        {
            coordinates = new[]
            {
                new[] { from.Lon, from.Lat },
                new[] { to.Lon, to.Lat }
            },
            avoid_features = avoidMotorways ? new[] { "highways" } : Array.Empty<string>()
        };
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.openrouteservice.org/v2/directions/driving-car/json");
        req.Headers.TryAddWithoutValidation("Authorization", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (!root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0) return null;
        var route = routes[0];
        var coords = route.GetProperty("geometry").GetProperty("coordinates");
        var pts = new List<GeoPoint>(coords.GetArrayLength());
        foreach (var c in coords.EnumerateArray())
            pts.Add(new GeoPoint(c[1].GetDouble(), c[0].GetDouble()));
        double distKm = 0, durMin = 0;
        if (route.TryGetProperty("summary", out var summary))
        {
            distKm = summary.GetProperty("distance").GetDouble() / 1000.0;
            durMin = summary.GetProperty("duration").GetDouble() / 60.0;
        }
        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            cum[i] = cum[i - 1] + pts[i - 1].DistanceTo(pts[i]);
        if (distKm <= 0 && cum.Length > 0) distKm = cum[^1];
        return new RouteResult
        {
            Points = pts,
            CumKm = cum,
            NodeIndices = Enumerable.Repeat(-1, pts.Count).ToArray(),
            DistanceKm = distKm,
            DurationMin = durMin
        };
    }

    public static async Task<List<ChargingStation>> ChargingStationsInBboxAsync(string apiKey,
        double minLat, double minLon, double maxLat, double maxLon, CancellationToken ct = default)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var url = "https://api.openchargemap.io/v3/poi/?output=json&compact=true&verbose=false"
                + $"&key={Uri.EscapeDataString(apiKey)}"
                + $"&boundingbox={minLat.ToString(inv)},{minLon.ToString(inv)},{maxLat.ToString(inv)},{maxLon.ToString(inv)}"
                + "&maxresults=2000";
        using var resp = await Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var list = new List<ChargingStation>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("AddressInfo", out var addr)) continue;
            if (!addr.TryGetProperty("Latitude", out var la) || !addr.TryGetProperty("Longitude", out var lo)) continue;
            var st = new ChargingStation
            {
                Id = item.TryGetProperty("ID", out var idEl) ? idEl.GetInt64() : 0,
                Pos = new GeoPoint(la.GetDouble(), lo.GetDouble()),
                Source = "OCM"
            };
            if (addr.TryGetProperty("Title", out var title))
                st.Name = title.GetString() ?? "";
            if (item.TryGetProperty("NumberOfPoints", out var nop) && nop.ValueKind == JsonValueKind.Number)
                st.Capacity = nop.GetInt32();
            if (item.TryGetProperty("Connections", out var conns) && conns.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in conns.EnumerateArray())
                {
                    if (c.TryGetProperty("PowerKW", out var pk) && pk.ValueKind == JsonValueKind.Number)
                        st.MaxPowerKw = Math.Max(st.MaxPowerKw, pk.GetDouble());
                    if (c.TryGetProperty("ConnectionType", out var ctype) &&
                        ctype.TryGetProperty("Title", out var ctitle))
                    {
                        var t = ctitle.GetString() ?? "";
                        if (t.Contains("CCS", StringComparison.OrdinalIgnoreCase)) st.Sockets.Add("type2_combo");
                        else if (t.Contains("CHAdeMO", StringComparison.OrdinalIgnoreCase)) st.Sockets.Add("chademo");
                        else if (t.Contains("Type 2", StringComparison.OrdinalIgnoreCase)) st.Sockets.Add("type2");
                    }
                }
            }
            list.Add(st);
        }
        return list;
    }
}
