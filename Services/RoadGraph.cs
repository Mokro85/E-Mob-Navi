using EvNav.Models;

namespace EvNav.Services;

public readonly record struct Edge(int To, float Km, float Kmh, byte Cls);

public sealed class RoadGraph
{
    public GeoPoint[] Positions { get; }
    public Edge[][] Adj { get; }
    public int Count => Positions.Length;

    private readonly Dictionary<(int X, int Y), List<int>> _grid = new();
    private const double CellDeg = 0.02;

    public RoadGraph(OsmImportResult data)
    {
        var idMap = new Dictionary<long, int>();
        var positions = new List<GeoPoint>();
        foreach (var way in data.Highways)
        {
            foreach (var id in way.NodeIds)
            {
                if (!idMap.ContainsKey(id) && data.Nodes.TryGetValue(id, out var p))
                {
                    idMap[id] = positions.Count;
                    positions.Add(p);
                }
            }
        }

        var adj = new List<Edge>[positions.Count];
        for (int i = 0; i < adj.Length; i++) adj[i] = new List<Edge>();
        int skipped = 0;

        foreach (var way in data.Highways)
        {
            for (int i = 0; i + 1 < way.NodeIds.Count; i++)
            {
                if (!idMap.TryGetValue(way.NodeIds[i], out int a)) { skipped++; continue; }
                if (!idMap.TryGetValue(way.NodeIds[i + 1], out int b)) { skipped++; continue; }
                if (a == b) continue;
                double km = Hav(positions[a], positions[b]);
                if (km < 0.0005) continue;
                float kmh = Speed(way.Cls);
                if (way.Oneway >= 0)
                {
                    adj[a].Add(new Edge(b, (float)km, kmh, way.Cls));
                    if (a != b && way.Oneway == 0) adj[b].Add(new Edge(a, (float)km, kmh, way.Cls));
                }
                else
                {
                    adj[b].Add(new Edge(a, (float)km, kmh, way.Cls));
                }
            }
        }

        Positions = positions.ToArray();
        Adj = adj.Select(l => l.ToArray()).ToArray();
        for (int i = 0; i < Positions.Length; i++)
        {
            var key = Key(Positions[i]);
            if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>();
            list.Add(i);
        }
    }

    public int NearestNode(GeoPoint p, double maxKm)
    {
        double latRad = p.Lat * Math.PI / 180.0;
        int latCells = (int)Math.Ceiling(maxKm / 111.0 / CellDeg);
        double lonKmPerDeg = 111.0 * Math.Max(0.2, Math.Cos(latRad));
        int lonCells = (int)Math.Ceiling(maxKm / lonKmPerDeg / CellDeg);
        var baseKey = Key(p);
        int best = -1;
        double bestD = maxKm;
        for (int dy = -latCells; dy <= latCells; dy++)
        {
            for (int dx = -lonCells; dx <= lonCells; dx++)
            {
                if (!_grid.TryGetValue((baseKey.X + dx, baseKey.Y + dy), out var list)) continue;
                foreach (var idx in list)
                {
                    double d = Hav(Positions[idx], p);
                    if (d < bestD) { bestD = d; best = idx; }
                }
            }
        }
        return best;
    }

    private static double Hav(GeoPoint a, GeoPoint b) => a.DistanceTo(b);
    private static (int X, int Y) Key(GeoPoint p)
        => ((int)Math.Floor(p.Lon / CellDeg), (int)Math.Floor(p.Lat / CellDeg));

    public static float Speed(byte cls) => cls switch
    {
        0 => 110,
        1 => 60,
        2 => 95,
        3 => 60,
        4 => 75,
        5 => 50,
        6 => 65,
        7 => 50,
        8 => 55,
        9 => 60,
        10 => 35,
        11 => 20,
        12 => 15,
        13 => 25,
        _ => 40
    };
}
