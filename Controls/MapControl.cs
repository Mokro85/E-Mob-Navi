using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EvNav.Models;
using EvNav.Services;

namespace EvNav.Controls;

public sealed class MapControl : FrameworkElement
{
    private static readonly Brush Bg = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xEF, 0xE9)));
    private static readonly Brush RouteBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x6F, 0xD8)));
    private static readonly Brush CasingBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly Brush StationBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x9E, 0x44)));
    private static readonly Brush StopBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x64, 0xC8)));
    private static readonly Brush StartBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x9E, 0x44)));
    private static readonly Brush DestBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD6, 0x3A, 0x2F)));
    private static readonly Pen RouteCasingPen = FrozenPen(CasingBrush, 9);
    private static readonly Pen RoutePen = FrozenPen(RouteBrush, 5);
    private static readonly Pen MarkerPen = FrozenPen(CasingBrush, 1.6);

    private static readonly Dictionary<byte, Pen> RoadPens = BuildRoadPens();

    private GeoPoint _center = new(49.0, 10.0);
    private double _zoom = 1500;
    private MapData? _data;
    private RouteResult? _route;
    private GeoPoint? _start;
    private GeoPoint? _end;
    private IReadOnlyList<ChargingStop>? _stops;
    private IReadOnlyList<ChargingStation>? _highlight;

    private bool _dragging;
    private bool _moved;
    private Point _dragPos;
    private double _downLon;
    private double _downMercY;
    private bool _pickStart;
    private bool _pickEnd;

    public event Action<GeoPoint>? GeoPicked;

    public MapData? Data
    {
        get => _data;
        set { _data = value; InvalidateVisual(); }
    }

    public void SetOverlay(RouteResult? route, GeoPoint? start, GeoPoint? end,
        IReadOnlyList<ChargingStop>? stops, IReadOnlyList<ChargingStation>? highlight)
    {
        _route = route;
        _start = start;
        _end = end;
        _stops = stops;
        _highlight = highlight;
        InvalidateVisual();
    }

    public void SetPickMode(bool isStart)
    {
        _pickStart = isStart;
        _pickEnd = !isStart;
        Cursor = Cursors.Cross;
    }

    public void ClearPickMode()
    {
        _pickStart = _pickEnd = false;
        Cursor = Cursors.Arrow;
    }

    public void CenterOn(GeoPoint p, double? zoom = null)
    {
        _center = p;
        if (zoom is not null) _zoom = zoom.Value;
        InvalidateVisual();
    }

    public void FitTo(IEnumerable<GeoPoint> points)
    {
        double minLon = double.MaxValue, maxLon = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            if (p.Lon < minLon) minLon = p.Lon;
            if (p.Lon > maxLon) maxLon = p.Lon;
            double y = p.MercY;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        double w = ActualWidth > 10 ? ActualWidth : RenderSize.Width;
        double h = ActualHeight > 10 ? ActualHeight : RenderSize.Height;
        if (w < 10 || h < 10 || minLon == double.MaxValue) return;
        double dx = Math.Max(maxLon - minLon, 1e-4);
        double dy = Math.Max(maxY - minY, 1e-4);
        _zoom = Math.Clamp(Math.Min(w / dx, h / dy) * 0.82, 20, 3_000_000);
        _center = new GeoPoint((LatFromY(minY) + LatFromY(maxY)) / 2.0, (minLon + maxLon) / 2.0);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = Math.Max(1, ActualWidth);
        double h = Math.Max(1, ActualHeight);
        dc.DrawRectangle(Bg, null, new Rect(0, 0, w, h));
        if (_data is null) return;

        var viewport = new Rect(0, 0, w, h);
        double leftLon = LonAt(0) - 0.05;
        double rightLon = LonAt(w) + 0.05;
        double topY = _center.MercY + h / 2 / _zoom + 0.02;
        double bottomY = _center.MercY - h / 2 / _zoom - 0.02;

        var clip = new RectangleGeometry(viewport);
        clip.Freeze();
        dc.PushClip(clip);
        {
            foreach (var way in _data.Roads)
            {
                if (way.MinLon > rightLon || way.MaxLon < leftLon ||
                    way.MaxY < bottomY || way.MinY > topY) continue;
                dc.DrawGeometry(null, PenFor(way.Cls), WayGeometry(way));
            }

            foreach (var st in _data.Stations)
            {
                var p = ToScreen(st.Pos);
                if (!viewport.Contains(p)) continue;
                bool hi = _highlight is not null && _highlight.Contains(st);
                double r = hi ? 6 : 3.2;
                dc.DrawEllipse(hi ? StopBrush : StationBrush, MarkerPen, p, r, r);
            }

            if (_route is not null && _route.Points.Count > 1)
            {
                var geo = PolyGeometry(_route.Points);
                dc.DrawGeometry(null, RouteCasingPen, geo);
                dc.DrawGeometry(null, RoutePen, geo);
            }

            if (_stops is not null)
            {
                foreach (var stop in _stops)
                {
                    var p = ToScreen(stop.Station.Pos);
                    if (!viewport.Contains(p)) continue;
                    dc.DrawEllipse(StopBrush, MarkerPen, p, 9, 9);
                    var ft = Text(stop.Index.ToString(), 11, CasingBrush);
                    dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
                }
            }

            DrawMarker(dc, _start, "A", StartBrush);
            DrawMarker(dc, _end, "B", DestBrush);
        }

        dc.Pop();
    }

    private void DrawMarker(DrawingContext dc, GeoPoint? gp, string label, Brush fill)
    {
        if (gp is null) return;
        var p = ToScreen(gp.Value);
        dc.DrawEllipse(fill, MarkerPen, p, 9, 9);
        var ft = Text(label, 11, CasingBrush);
        dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
    }

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private static Pen FrozenPen(Brush b, double t)
    {
        var pen = new Pen(b, t);
        pen.Freeze();
        return pen;
    }

    private static Dictionary<byte, Pen> BuildRoadPens() => new()
    {
        [0] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x9C, 0x6B))), 6),
        [1] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x9C, 0x6B))), 4),
        [2] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xC3, 0x70))), 5),
        [3] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xC3, 0x70))), 3.5),
        [4] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xFA, 0xE0, 0xA0))), 4.5),
        [5] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xFA, 0xE0, 0xA0))), 3),
        [6] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xC6))), 4),
        [7] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xC6))), 3),
        [8] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))), 3.4),
        [9] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))), 3),
        [10] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))), 2.8),
        [11] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE))), 2),
        [12] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF3, 0xEC))), 2.4),
        [13] = FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xB8))), 2)
    };

    private static Pen PenFor(byte cls) =>
        RoadPens.TryGetValue(cls, out var pen)
            ? pen
            : FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))), 2.5);

    private StreamGeometry WayGeometry(RenderWay way)
    {
        var geo = new StreamGeometry();
        var first = ToScreen(way.Pts[0]);
        var rest = new List<Point>(way.Pts.Length - 1);
        for (int i = 1; i < way.Pts.Length; i++)
            rest.Add(ToScreen(way.Pts[i]));
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(first, false, false);
            ctx.PolyLineTo(rest, true, false);
        }
        geo.Freeze();
        return geo;
    }

    private StreamGeometry PolyGeometry(IReadOnlyList<GeoPoint> pts)
    {
        var geo = new StreamGeometry();
        var first = ToScreen(pts[0]);
        var rest = new List<Point>(pts.Count - 1);
        for (int i = 1; i < pts.Count; i++)
            rest.Add(ToScreen(pts[i]));
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(first, false, false);
            ctx.PolyLineTo(rest, true, false);
        }
        geo.Freeze();
        return geo;
    }

    private double LonAt(double x) => _center.Lon + (x - ActualWidth / 2) / _zoom;

    private double LatAt(double y) => LatFromY(_center.MercY - (y - ActualHeight / 2) / _zoom);

    private static double LatFromY(double mercY) => GeoMath.LatFromMerc(mercY);

    private Point ToScreen(GeoPoint p) => new(
        (p.Lon - _center.Lon) * _zoom + ActualWidth / 2,
        ActualHeight / 2 - (p.MercY - _center.MercY) * _zoom);

    private GeoPoint ToGeo(Point p) => new(LatAt(p.Y), LonAt(p.X));

    private static FormattedText Text(string s, double size, Brush brush)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, 1.0);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        _moved = false;
        _dragPos = e.GetPosition(this);
        _downLon = _center.Lon;
        _downMercY = _center.MercY;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        double dx = p.X - _dragPos.X;
        double dy = p.Y - _dragPos.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 4) _moved = true;
        _center = new GeoPoint(LatFromY(_downMercY - dy / _zoom), _downLon - dx / _zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        if (!_moved && (_pickStart || _pickEnd))
        {
            GeoPicked?.Invoke(ToGeo(e.GetPosition(this)));
            ClearPickMode();
        }
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var mPos = e.GetPosition(this);
        double mLon = _center.Lon + (mPos.X - ActualWidth / 2) / _zoom;
        double mMerc = _center.MercY - (mPos.Y - ActualHeight / 2) / _zoom;
        double f = e.Delta > 0 ? 1.3 : 1.0 / 1.3;
        _zoom = Math.Clamp(_zoom * f, 20, 3_000_000);
        _center = new GeoPoint(
            LatFromY(mMerc - (mPos.Y - ActualHeight / 2) / _zoom),
            mLon - (mPos.X - ActualWidth / 2) / _zoom);
        InvalidateVisual();
        e.Handled = true;
    }
}
