using EvNav.Models;
using EvNav.Services;

namespace EvNav.Controls;

public sealed class RenderWay
{
    public required GeoPoint[] Pts { get; init; }
    public required byte Cls { get; init; }
    public double MinLon { get; set; }
    public double MaxLon { get; set; }
    public double MinY { get; set; }
    public double MaxY { get; set; }
}

public sealed class MapData
{
    public required IReadOnlyList<RenderWay> Roads { get; init; }
    public required RoadGraph? Graph { get; init; }
    public required IReadOnlyList<ChargingStation> Stations { get; init; }
    public required IReadOnlyList<NamedPlace> Places { get; init; }

    public static MapData From(OsmImportResult data, RoadGraph? graph)
    {
        var roads = new List<RenderWay>(data.Highways.Count);
        foreach (var way in data.Highways)
        {
            if (way.NodeIds.Count < 2) continue;
            var pts = new List<GeoPoint>(way.NodeIds.Count);
            foreach (var id in way.NodeIds)
                if (data.Nodes.TryGetValue(id, out var p)) pts.Add(p);
            if (pts.Count < 2) continue;
            var rw = new RenderWay { Pts = pts.ToArray(), Cls = way.Cls };
            double minLon = double.MaxValue, maxLon = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in rw.Pts)
            {
                if (p.Lon < minLon) minLon = p.Lon;
                if (p.Lon > maxLon) maxLon = p.Lon;
                double y = p.MercY;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            rw.MinLon = minLon; rw.MaxLon = maxLon; rw.MinY = minY; rw.MaxY = maxY;
            roads.Add(rw);
        }
        roads.Sort((a, b) => b.Cls.CompareTo(a.Cls));
        return new MapData
        {
            Roads = roads,
            Graph = graph,
            Stations = data.Stations,
            Places = data.Places
        };
    }
}
