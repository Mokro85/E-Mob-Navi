namespace EvNav.Models;

public sealed class OsmImportResult
{
    public Dictionary<long, GeoPoint> Nodes { get; } = new();
    public List<HighwayWay> Highways { get; } = new();
    public List<ChargingStation> Stations { get; } = new();
    public List<NamedPlace> Places { get; } = new();
}

public sealed class HighwayWay
{
    public required long Id { get; init; }
    public required byte Cls { get; init; }
    public string Name { get; set; } = "";
    public int Oneway { get; set; }
    public List<long> NodeIds { get; init; } = new();
}

public sealed class NamedPlace
{
    public required string Name { get; init; }
    public required GeoPoint Pos { get; init; }
    public required string Kind { get; init; }
}
