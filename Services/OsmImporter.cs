using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using EvNav.Models;

namespace EvNav.Services;

public static partial class OsmImporter
{
    public static OsmImportResult Import(string path)
    {
        var result = new OsmImportResult();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        Stream stream = fs;
        if (fs.Length >= 2)
        {
            int b0 = fs.ReadByte();
            int b1 = fs.ReadByte();
            fs.Seek(0, SeekOrigin.Begin);
            if (b0 == 0x1f && b1 == 0x8b) stream = new GZipStream(fs, CompressionMode.Decompress);
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false
        };
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            switch (reader.Name)
            {
                case "node":
                    ReadNode(reader, result);
                    break;
                case "way":
                    ReadWay(reader, result);
                    break;
            }
        }
        BuildStreetIndex(result);
        return result;
    }

    private static void ReadNode(XmlReader reader, OsmImportResult result)
    {
        long id = ReadLong(reader, "id");
        double lat = ReadDouble(reader, "lat");
        double lon = ReadDouble(reader, "lon");
        var pos = new GeoPoint(lat, lon);
        string name = "";
        string? place = null;
        string? amenity = null;
        Dictionary<string, string>? tags = null;
        using (var sub = reader.ReadSubtree())
        {
            while (sub.Read())
            {
                if (sub.NodeType != XmlNodeType.Element || sub.Name != "tag") continue;
                var k = sub.GetAttribute("k");
                if (k is null) continue;
                var v = sub.GetAttribute("v") ?? "";
                switch (k)
                {
                    case "name": name = v; break;
                    case "place": place = v; break;
                    case "amenity": amenity = v; break;
                    default: (tags ??= new Dictionary<string, string>())[k] = v; break;
                }
            }
        }
        result.Nodes[id] = pos;
        if (amenity == "charging_station")
        {
            result.Stations.Add(StationFromTags(id, pos, name, tags, "OSM"));
        }
        else if (name.Length > 0 && (place is not null || amenity is not null))
        {
            var kind = place ?? amenity!;
            if (kind is "city" or "town" or "village" or "suburb" or "hamlet" or "fuel" or "restaurant"
                or "supermarket" or "service" or "bus_station" or "hospital")
            {
                result.Places.Add(new NamedPlace { Name = name, Pos = pos, Kind = kind });
            }
        }
    }

    private static void ReadWay(XmlReader reader, OsmImportResult result)
    {
        long id = ReadLong(reader, "id");
        var nodeIds = new List<long>();
        var tags = new Dictionary<string, string>();
        using (var sub = reader.ReadSubtree())
        {
            while (sub.Read())
            {
                if (sub.NodeType != XmlNodeType.Element) continue;
                if (sub.Name == "nd")
                {
                    var rf = sub.GetAttribute("ref");
                    if (rf is not null && long.TryParse(rf, System.Globalization.CultureInfo.InvariantCulture, out var nid))
                        nodeIds.Add(nid);
                }
                else if (sub.Name == "tag")
                {
                    var k = sub.GetAttribute("k");
                    if (k is not null) tags[k] = sub.GetAttribute("v") ?? "";
                }
            }
        }
        if (nodeIds.Count < 2) return;

        if (tags.TryGetValue("highway", out var hw) && tags.GetValueOrDefault("area") != "yes")
        {
            byte cls = ClassOf(hw);
            if (cls != 255 && !IsBlocked(tags))
            {
                var way = new HighwayWay { Id = id, Cls = cls, Name = tags.GetValueOrDefault("name", "") };
                way.Oneway = ParseOneway(tags);
                if ((cls == 0 || cls == 1) && tags.GetValueOrDefault("oneway") != "no") way.Oneway = 1;
                way.NodeIds.AddRange(nodeIds);
                result.Highways.Add(way);
            }
        }

        if (tags.GetValueOrDefault("amenity") == "charging_station")
        {
            var pts = new List<GeoPoint>();
            foreach (var nid in nodeIds)
                if (result.Nodes.TryGetValue(nid, out var p)) pts.Add(p);
            if (pts.Count > 0)
            {
                var pos = new GeoPoint(pts.Average(p => p.Lat), pts.Average(p => p.Lon));
                result.Stations.Add(StationFromTags(id, pos, tags.GetValueOrDefault("name", ""), tags, "OSM"));
            }
        }
    }

    private static ChargingStation StationFromTags(long id, GeoPoint pos, string name,
        Dictionary<string, string>? tags, string source)
    {
        tags ??= new Dictionary<string, string>();
        var st = new ChargingStation { Id = id, Pos = pos, Source = source };
        double power = 0;
        if (tags.TryGetValue("maxpower", out var mp)) power = Math.Max(power, ParseMaxNumber(mp));
        if (tags.TryGetValue("charging_station:output", out var cso)) power = Math.Max(power, ParseMaxNumber(cso));
        if (tags.TryGetValue("power", out var pw)) power = Math.Max(power, ParseMaxNumber(pw));
        if (tags.TryGetValue("charge", out var ch)) power = Math.Max(power, ParseMaxNumber(ch));
        foreach (var (k, v) in tags)
        {
            if (!k.StartsWith("socket:", StringComparison.Ordinal)) continue;
            var parts = k.Split(':');
            if (parts.Length < 2) continue;
            var socket = parts[1];
            if (parts.Length >= 3 && parts[2] == "combo") socket = socket + "_combo";
            if (v != "no") st.Sockets.Add(socket);
            if (parts[^1] == "output" && v.Length > 0)
                power = Math.Max(power, ParseMaxNumber(v));
        }
        st.MaxPowerKw = power;
        if (name.Length == 0)
            name = tags.GetValueOrDefault("operator", "").Length > 0 ? tags["operator"] : "Ladestation";
        st.Name = name;
        st.Operator = tags.GetValueOrDefault("operator", "");
        if (tags.TryGetValue("capacity", out var cap) && int.TryParse(cap.Split(';')[0].Trim(), out var c)) st.Capacity = c;
        if (tags.TryGetValue("access", out var acc) && acc.Length > 0) st.Access = acc;
        return st;
    }

    private static void BuildStreetIndex(OsmImportResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var way in result.Highways)
        {
            if (way.Name.Length == 0 || way.NodeIds.Count == 0) continue;
            if (!seen.Add(way.Name)) continue;
            long midId = way.NodeIds[way.NodeIds.Count / 2];
            if (result.Nodes.TryGetValue(midId, out var pos))
                result.Places.Add(new NamedPlace { Name = way.Name, Pos = pos, Kind = "Straße" });
        }
        result.Places.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
    }

    public static byte ClassOf(string highway) => highway switch
    {
        "motorway" => 0,
        "motorway_link" => 1,
        "trunk" => 2,
        "trunk_link" => 3,
        "primary" => 4,
        "primary_link" => 5,
        "secondary" => 6,
        "secondary_link" => 7,
        "tertiary" => 8,
        "tertiary_link" => 8,
        "unclassified" => 9,
        "road" => 9,
        "residential" => 10,
        "living_street" => 12,
        "service" => 11,
        "track" => 13,
        _ => 255
    };

    private static bool IsBlocked(Dictionary<string, string> tags)
    {
        var access = tags.GetValueOrDefault("access");
        if (access is "no" or "private") return true;
        var mv = tags.GetValueOrDefault("motor_vehicle");
        if (mv is "no" or "private") return true;
        return false;
    }

    private static int ParseOneway(Dictionary<string, string> tags)
    {
        var ow = tags.GetValueOrDefault("oneway");
        if (ow == "yes" || ow == "1") return 1;
        if (ow == "-1") return -1;
        return 0;
    }

    private static double ParseMaxNumber(string value)
    {
        double best = 0;
        foreach (Match m in NumberRegex().Matches(value))
        {
            var norm = m.Value.Replace(',', '.');
            if (double.TryParse(norm, System.Globalization.CultureInfo.InvariantCulture, out var d) && d > best)
                best = d;
        }
        return best;
    }

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();

    private static long ReadLong(XmlReader r, string attr) =>
        long.TryParse(r.GetAttribute(attr), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double ReadDouble(XmlReader r, string attr) =>
        double.TryParse(r.GetAttribute(attr), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}
