using EvNav.Models;

namespace EvNav.Services;

public sealed class StationService
{
    private readonly IReadOnlyList<ChargingStation> _all;
    private readonly Dictionary<(int X, int Y), List<ChargingStation>> _grid = new();
    public const double CellDeg = 0.01;

    public StationService(IEnumerable<ChargingStation> stations)
    {
        _all = stations.ToList();
        foreach (var st in _all)
        {
            var key = Key(st.Pos);
            if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<ChargingStation>();
            list.Add(st);
        }
    }

    public IReadOnlyList<ChargingStation> All => _all;

    public List<ChargingStation> Near(GeoPoint p, double radiusKm, VehicleProfile profile)
    {
        int latCells = (int)Math.Ceiling(radiusKm / 111.0 / CellDeg);
        double lonKmPerDeg = 111.0 * Math.Max(0.2, Math.Cos(p.Lat * Math.PI / 180.0));
        int lonCells = (int)Math.Ceiling(radiusKm / lonKmPerDeg / CellDeg);
        var baseKey = Key(p);
        var found = new List<ChargingStation>();
        for (int dy = -latCells; dy <= latCells; dy++)
        {
            for (int dx = -lonCells; dx <= lonCells; dx++)
            {
                if (!_grid.TryGetValue((baseKey.X + dx, baseKey.Y + dy), out var list)) continue;
                foreach (var st in list)
                {
                    if (st.Pos.DistanceTo(p) <= radiusKm && st.MatchesVehicle(profile))
                        found.Add(st);
                }
            }
        }
        return found;
    }

    public static (int X, int Y) Key(GeoPoint p)
        => ((int)Math.Floor(p.Lon / CellDeg), (int)Math.Floor(p.Lat / CellDeg));
}

public static class OptionFinder
{
    private const double AvgDetourSpeedKmh = 35;

    public static List<ChargingOption> FindAlong(RoadGraph? graph, RouteResult route,
        IReadOnlyList<ChargingStation> stations, VehicleProfile profile,
        double fromKm, double toKm, StationService? service)
    {
        var options = new List<ChargingOption>();
        if (toKm <= fromKm) return options;

        if (service is not null)
        {
            var seen = new HashSet<long>();
            var nodeCache = new Dictionary<long, int>();
            for (int i = 0; i < route.Points.Count; i++)
            {
                double along = route.CumKm[i];
                if (along < fromKm || along > toKm) continue;
                foreach (var st in service.Near(route.Points[i], profile.MaxDetourKm * 1.6, profile))
                {
                    if (!seen.Add(st.Id)) continue;
                    double reachKm = Math.Max(1.0, profile.MaxDetourKm);
                    if (!nodeCache.TryGetValue(st.Id, out int stNode))
                    {
                        stNode = graph?.NearestNode(st.Pos, reachKm) ?? -1;
                        nodeCache[st.Id] = stNode;
                    }
                    double detKm = TransitionDetourKm(graph, route.NodeIndices[i], stNode, st.Pos, route.Points[i]);
                    double oneWay = detKm;
                    if (graph is not null && route.NodeIndices[i] >= 0 && stNode >= 0)
                    {
                        var path = RoutingService.FindPath(graph!, route.NodeIndices[i], stNode, false);
                        if (path is not null) oneWay = path.DistanceKm;
                    }
                    options.Add(new ChargingOption
                    {
                        Station = st,
                        RouteNodeIdx = route.NodeIndices[i],
                        AlongKm = along,
                        DetourKm = oneWay * 2.0,
                        DetourMin = oneWay * 2.0 / AvgDetourSpeedKmh * 60.0
                    });
                }
            }
        }
        else
        {
            var cellToSamples = new Dictionary<(int X, int Y), List<int>>();
            for (int i = 0; i < route.Points.Count; i++)
            {
                if (route.CumKm[i] < fromKm || route.CumKm[i] > toKm) continue;
                var key = StationService.Key(route.Points[i]);
                if (!cellToSamples.TryGetValue(key, out var list)) cellToSamples[key] = list = new List<int>();
                list.Add(i);
            }
            foreach (var st in stations)
            {
                if (!st.MatchesVehicle(profile)) continue;
                double bestD = double.MaxValue;
                int bestIdx = -1;
                int r = (int)Math.Ceiling(profile.MaxDetourKm * 1.6 / 111.0 / StationService.CellDeg);
                var baseKey = StationService.Key(st.Pos);
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (!cellToSamples.TryGetValue((baseKey.X + dx, baseKey.Y + dy), out var list)) continue;
                        foreach (var i in list)
                        {
                            double d = st.Pos.DistanceTo(route.Points[i]);
                            if (d < bestD) { bestD = d; bestIdx = i; }
                        }
                    }
                }
                if (bestIdx < 0 || bestD > profile.MaxDetourKm) continue;
                double oneWay = bestD * 1.4;
                options.Add(new ChargingOption
                {
                    Station = st,
                    RouteNodeIdx = route.NodeIndices[bestIdx],
                    AlongKm = route.CumKm[bestIdx],
                    DetourKm = oneWay * 2.0,
                    DetourMin = oneWay * 2.0 / AvgDetourSpeedKmh * 60.0
                });
            }
        }

        options.Sort((a, b) => a.AlongKm.CompareTo(b.AlongKm));
        return options;
    }

    private static double TransitionDetourKm(RoadGraph? graph, int routeNode, int stationNode,
        GeoPoint stationPos, GeoPoint routePos)
    {
        if (graph is null || routeNode < 0 || stationNode < 0)
            return routePos.DistanceTo(stationPos) * 1.4;
        return routePos.DistanceTo(graph.Positions[stationNode]) * 1.4;
    }
}
