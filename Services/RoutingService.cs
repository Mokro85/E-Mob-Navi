using EvNav.Models;

namespace EvNav.Services;

public static class RoutingService
{
    public static RouteResult? FindPath(RoadGraph g, int start, int goal, bool avoidMotorways)
    {
        if (start < 0 || goal < 0 || start >= g.Count || goal >= g.Count) return null;
        if (start == goal)
        {
            return new RouteResult
            {
                Points = new() { g.Positions[start] },
                CumKm = new double[] { 0 },
                NodeIndices = new[] { start },
                DistanceKm = 0,
                DurationMin = 0
            };
        }

        double maxSpeed = avoidMotorways ? 100 : 110;
        var dist = new double[g.Count];
        Array.Fill(dist, double.MaxValue);
        var prev = new int[g.Count];
        Array.Fill(prev, -1);
        var done = new bool[g.Count];
        var pq = new PriorityQueue<int, double>();
        dist[start] = 0;
        pq.Enqueue(start, 0.0);

        while (pq.TryDequeue(out int u, out _))
        {
            if (done[u]) continue;
            done[u] = true;
            if (u == goal) break;
            foreach (var e in g.Adj[u])
            {
                if (avoidMotorways && e.Cls <= 3) continue;
                double w = e.Km / Math.Max(5, e.Kmh) * 60.0;
                double nd = dist[u] + w;
                if (nd < dist[e.To] - 1e-9)
                {
                    dist[e.To] = nd;
                    prev[e.To] = u;
                    pq.Enqueue(e.To, nd + Heuristic(g.Positions[e.To], g.Positions[goal], maxSpeed));
                }
            }
        }

        if (prev[goal] < 0 && goal != start) return null;

        var path = new List<int>();
        int cur = goal;
        while (cur >= 0) { path.Add(cur); cur = prev[cur]; }
        path.Reverse();

        var pts = new List<GeoPoint>(path.Count);
        var cum = new double[path.Count];
        var nodes = new int[path.Count];
        double km = 0, minutes = 0;
        for (int i = 0; i < path.Count; i++)
        {
            pts.Add(g.Positions[path[i]]);
            nodes[i] = path[i];
            cum[i] = km;
            if (i > 0)
            {
                int a = path[i - 1], b = path[i];
                foreach (var e in g.Adj[a])
                {
                    if (e.To == b)
                    {
                        minutes += e.Km / Math.Max(5, e.Kmh) * 60.0;
                        break;
                    }
                }
                km += g.Positions[a].DistanceTo(g.Positions[b]);
            }
        }

        return new RouteResult
        {
            Points = pts,
            CumKm = cum,
            NodeIndices = nodes,
            DistanceKm = km,
            DurationMin = minutes
        };
    }

    private static double Heuristic(GeoPoint a, GeoPoint b, double maxSpeedKmh)
        => a.DistanceTo(b) / maxSpeedKmh * 60.0 * 0.95;
}
