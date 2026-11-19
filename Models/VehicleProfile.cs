namespace EvNav.Models;

public sealed class VehicleProfile
{
    public string Name { get; set; } = "Mein E-Auto";
    public double BatteryKwh { get; set; } = 60.0;
    public double ConsumptionKwhPer100Km { get; set; } = 17.0;
    public double MaxDcPowerKw { get; set; } = 120.0;
    public double StartSocPct { get; set; } = 100.0;
    public double MinArrivalSocPct { get; set; } = 15.0;
    public double MaxChargeSocPct { get; set; } = 80.0;
    public double MinDestinationSocPct { get; set; } = 10.0;
    public double MaxDetourKm { get; set; } = 3.0;
    public double MinStationPowerKw { get; set; } = 50.0;
    public bool Ccs { get; set; } = true;
    public bool Type2Ac { get; set; } = true;
    public bool Chademo { get; set; }
}
