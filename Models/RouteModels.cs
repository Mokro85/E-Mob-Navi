namespace EvNav.Models;

public sealed class RouteResult
{
    public required List<GeoPoint> Points { get; init; }
    public required double[] CumKm { get; init; }
    public required int[] NodeIndices { get; init; }
    public required double DistanceKm { get; init; }
    public required double DurationMin { get; init; }
}

public sealed class ChargingOption
{
    public required ChargingStation Station { get; init; }
    public int RouteNodeIdx { get; init; } = -1;
    public required double AlongKm { get; init; }
    public required double DetourKm { get; init; }
    public required double DetourMin { get; init; }
}

public sealed class ChargingStop
{
    public required int Index { get; init; }
    public required ChargingStation Station { get; init; }
    public required double AlongKm { get; init; }
    public required double DetourKm { get; init; }
    public required double ArriveSocPct { get; init; }
    public required double DepartSocPct { get; init; }
    public required double ChargeKwh { get; init; }
    public required double ChargeMin { get; init; }
}

public sealed class TripPlan
{
    public List<ChargingStop> Stops { get; } = new();
    public bool Success { get; internal set; }
    public string Message { get; internal set; } = "";
    public double ArrivalSocPct { get; internal set; }
    public double TotalChargeMin { get; internal set; }
    public double TotalDetourKm { get; internal set; }
    public double ExtraDriveMin { get; internal set; }
}
