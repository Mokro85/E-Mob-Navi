namespace EvNav.Models;

public readonly record struct GeoPoint(double Lat, double Lon)
{
    public const double EarthRadiusKm = 6371.0;

    private const double Rad = Math.PI / 180.0;

    public double MercY => Math.Log(Math.Tan(Math.PI / 4.0 + Lat * Rad / 2.0));

    public double DistanceTo(GeoPoint o)
    {
        double dLat = (o.Lat - Lat) * Rad;
        double dLon = (o.Lon - Lon) * Rad;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(Lat * Rad) * Math.Cos(o.Lat * Rad) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

public static class GeoMath
{
    private const double Rad = Math.PI / 180.0;

    public static double Haversine(GeoPoint a, GeoPoint b) => a.DistanceTo(b);

    public static double LatFromMerc(double y)
        => 2.0 * Math.Atan(Math.Exp(y)) / Rad - 90.0;
}
