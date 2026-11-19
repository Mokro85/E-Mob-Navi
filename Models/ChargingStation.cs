namespace EvNav.Models;

public sealed class ChargingStation
{
    public required long Id { get; init; }
    public required GeoPoint Pos { get; init; }
    public string Name { get; set; } = "";
    public string Operator { get; set; } = "";
    public double MaxPowerKw { get; set; }
    public int Capacity { get; set; }
    public string Access { get; set; } = "public";
    public HashSet<string> Sockets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Source { get; set; } = "OSM";

    public double EffectivePowerKw => MaxPowerKw > 0 ? MaxPowerKw : 50.0;

    public bool MatchesVehicle(VehicleProfile p)
    {
        if (EffectivePowerKw + 0.01 < p.MinStationPowerKw) return false;
        if (Sockets.Count == 0) return true;
        if (Access is "private" or "no") return false;
        bool any = false;
        foreach (var s in Sockets)
        {
            if (s.Contains("combo") || s.Contains("ccs")) { if (p.Ccs) return true; any = true; }
            else if (s.Contains("chademo")) { if (p.Chademo) return true; any = true; }
            else if (s.Contains("type2") || s.Contains("type_2")) { if (p.Type2Ac) return true; any = true; }
        }
        return !any;
    }
}
