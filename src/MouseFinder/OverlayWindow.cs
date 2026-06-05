using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace MouseFinder;

/// <summary>
/// Full-screen transparent overlay with 9 indicator styles.
/// Click-through, non-activating, never steals focus.
///
/// Uses a single DrawingVisual + OnRender for zero-allocation animation
/// (no WPF Shape objects are created per frame).
/// Covers the entire virtual screen (all monitors).
/// </summary>
public class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public bool IsIndicatorVisible { get; private set; }

    private readonly DispatcherTimer _animTimer;
    private readonly DispatcherTimer _autoHide;
    private readonly IndicatorSurface _surface;
    private AppSettings _settings;
    private double _phase;
    private Point _pos;
    private Color _baseColor;
    private EdgeDirection _edgeDirection = EdgeDirection.None;

    public OverlayWindow(AppSettings settings)
    {
        _settings = settings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        // Cover the entire virtual screen (all monitors)
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _surface = new IndicatorSurface();
        Content = _surface;

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animTimer.Tick += (_, _) => Draw();

        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoHide.Tick += (_, _) => HideIndicator();

        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };

        ParseColor();
    }

    public void ShowIndicator(Point mousePos, EdgeDirection direction = EdgeDirection.None)
    {
        _pos = mousePos;
        _edgeDirection = direction;
        IsIndicatorVisible = true;
        _phase = 0;
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
        _animTimer.Start();
        _autoHide.Start();
        Draw();
    }

    public void HideIndicator()
    {
        IsIndicatorVisible = false;
        _animTimer.Stop();
        _autoHide.Stop();
        _surface.Clear();
        if (IsVisible) Hide();
    }

    public void ForceShow()
    {
        var pos = System.Windows.Forms.Cursor.Position;
        double dpi = GetDpiForSystem();
        double scale = dpi / 96.0;
        // Convert screen coordinates to virtual-screen-relative coordinates
        double x = pos.X / scale - SystemParameters.VirtualScreenLeft;
        double y = pos.Y / scale - SystemParameters.VirtualScreenTop;
        ShowIndicator(new Point(x, y));
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        ParseColor();
    }

    private void ParseColor()
    {
        try { _baseColor = (Color)ColorConverter.ConvertFromString(_settings.IndicatorColor); }
        catch { _baseColor = Colors.DeepSkyBlue; }
    }

    private void Draw()
    {
        _phase += 0.06;
        _surface.Draw(_settings, _pos, _phase, _baseColor, _edgeDirection);
    }

    // ════════════════════════════════════════════════════════════════
    //  IndicatorSurface — single FrameworkElement, zero-alloc rendering
    // ════════════════════════════════════════════════════════════════

    private class IndicatorSurface : FrameworkElement
    {
        private AppSettings? _settings;
        private Point _pos;
        private double _phase;
        private Color _baseColor;
        private EdgeDirection _edgeDirection = EdgeDirection.None;

        protected override void OnRender(DrawingContext dc)
        {
            if (_settings == null) return;

            // If EdgeArrow style is selected, show edge arrow when direction is available
            if (_settings.Style == IndicatorStyle.EdgeArrow && _edgeDirection != EdgeDirection.None)
            {
                DrawEdgeArrow(dc);
                return;
            }

            switch (_settings.Style)
            {
                case IndicatorStyle.GlowRing: DrawGlowRing(dc); break;
                case IndicatorStyle.Arrow: DrawArrow(dc); break;
                case IndicatorStyle.Ripple: DrawRipple(dc); break;
                case IndicatorStyle.Spotlight: DrawSpotlight(dc); break;
                case IndicatorStyle.Crosshair: DrawCrosshair(dc); break;
                case IndicatorStyle.Beacon: DrawBeacon(dc); break;
                case IndicatorStyle.BigArrow: DrawBigArrow(dc); break;
                case IndicatorStyle.Target: DrawTarget(dc); break;
                case IndicatorStyle.Spiral: DrawSpiral(dc); break;
                case IndicatorStyle.EdgeArrow: DrawBigArrow(dc); break; // Fallback for global mode
            }
        }

        public void Draw(AppSettings settings, Point pos, double phase, Color baseColor, EdgeDirection edgeDirection = EdgeDirection.None)
        {
            _settings = settings;
            _pos = pos;
            _phase = phase;
            _baseColor = baseColor;
            _edgeDirection = edgeDirection;
            InvalidateVisual();
        }

        public void Clear()
        {
            _settings = null;
            InvalidateVisual();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private Pen MakePen(byte a, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(a, _baseColor.R, _baseColor.G, _baseColor.B)), thickness);
            pen.Freeze();
            return pen;
        }

        private Pen MakePen(Color c, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(c), thickness);
            pen.Freeze();
            return pen;
        }

        private SolidColorBrush MakeBrush(byte a)
        {
            var b = new SolidColorBrush(Color.FromArgb(a, _baseColor.R, _baseColor.G, _baseColor.B));
            b.Freeze();
            return b;
        }

        private void DrawEllipse(DrawingContext dc, double cx, double cy, double radius,
            Pen stroke, Brush? fill = null)
        {
            dc.DrawEllipse(fill ?? Brushes.Transparent, stroke, new Point(cx, cy), radius, radius);
        }

        private void DrawDot(DrawingContext dc, double cx, double cy, double radius, Brush fill)
        {
            dc.DrawEllipse(fill, null, new Point(cx, cy), radius, radius);
        }

        private void DrawLine(DrawingContext dc, double x1, double y1, double x2, double y2, Pen pen)
        {
            dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
        }

        private void DrawDash(DrawingContext dc, double dx, double dy, double angle, double len, Pen pen)
        {
            dc.DrawLine(pen,
                new Point(dx - len * Math.Cos(angle), dy - len * Math.Sin(angle)),
                new Point(dx + len * Math.Cos(angle), dy + len * Math.Sin(angle)));
        }

        private void DrawRotatedLine(DrawingContext dc, double x1, double y1, double x2, double y2,
            Pen pen, double pivotX, double pivotY, double angle)
        {
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            double rx1 = pivotX + (x1 - pivotX) * cos - (y1 - pivotY) * sin;
            double ry1 = pivotY + (x1 - pivotX) * sin + (y1 - pivotY) * cos;
            double rx2 = pivotX + (x2 - pivotX) * cos - (y2 - pivotY) * sin;
            double ry2 = pivotY + (x2 - pivotX) * sin + (y2 - pivotY) * cos;
            dc.DrawLine(pen, new Point(rx1, ry1), new Point(rx2, ry2));
        }

        // ── Edge Arrow (边缘箭头) ──────────────────────────────────
        // A large static arrow pointing toward the mouse from the edge

        private void DrawEdgeArrow(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 2.0;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.08 * Math.Sin(_phase * 2) : 1.0;
            double arrowSize = size * pulse;

            // Arrow body extends from edge toward mouse
            double bodyLen = arrowSize * 2.0;
            double wingHalf = arrowSize * 0.6;

            // Calculate arrow position and direction based on edge
            double tipX, tipY, bodyX, bodyY;
            double dirX = 0, dirY = 0; // direction from body to tip

            switch (_edgeDirection)
            {
                case EdgeDirection.Top:
                    tipX = cx;
                    tipY = cy;
                    tipY = Math.Max(cy, SystemParameters.VirtualScreenTop + arrowSize);
                    bodyX = cx;
                    bodyY = tipY + bodyLen;
                    dirX = 0;
                    dirY = -1;
                    break;
                case EdgeDirection.Bottom:
                    tipX = cx;
                    tipY = cy;
                    tipY = Math.Min(cy, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - arrowSize);
                    bodyX = cx;
                    bodyY = tipY - bodyLen;
                    dirX = 0;
                    dirY = 1;
                    break;
                case EdgeDirection.Left:
                    tipX = cx;
                    tipX = Math.Max(cx, SystemParameters.VirtualScreenLeft + arrowSize);
                    tipY = cy;
                    bodyX = tipX + bodyLen;
                    bodyY = cy;
                    dirX = -1;
                    dirY = 0;
                    break;
                case EdgeDirection.Right:
                    tipX = cx;
                    tipX = Math.Min(cx, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - arrowSize);
                    tipY = cy;
                    bodyX = tipX - bodyLen;
                    bodyY = cy;
                    dirX = 1;
                    dirY = 0;
                    break;
                default:
                    return;
            }

            // Perpendicular direction
            double px = -dirY, py = dirX;

            // Wing points
            double w1x = bodyX + px * wingHalf, w1y = bodyY + py * wingHalf;
            double w2x = bodyX - px * wingHalf, w2y = bodyY - py * wingHalf;

            // Tail end
            double tailX = bodyX - dirX * bodyLen * 0.5;
            double tailY = bodyY - dirY * bodyLen * 0.5;

            // Tail notch
            double notchDepth = arrowSize * 0.4;
            double n1x = w1x - dirX * notchDepth + px * notchDepth * 0.3;
            double n1y = w1y - dirY * notchDepth + py * notchDepth * 0.3;
            double n2x = w2x - dirX * notchDepth - px * notchDepth * 0.3;
            double n2y = w2y - dirY * notchDepth - py * notchDepth * 0.3;

            // Glow
            var glowBrush = MakeBrush(40);
            dc.DrawEllipse(glowBrush, null, new Point(tipX, tipY), arrowSize * 0.3, arrowSize * 0.3);

            // Arrow shape
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(tipX, tipY), true, true);
                ctx.LineTo(new Point(w1x, w1y), true, false);
                ctx.LineTo(new Point(n1x, n1y), true, false);
                ctx.LineTo(new Point(tailX, tailY), true, false);
                ctx.LineTo(new Point(n2x, n2y), true, false);
                ctx.LineTo(new Point(w2x, w2y), true, false);
            }
            geo.Freeze();

            // Fill + stroke
            var fillBrush = MakeBrush(200);
            var strokePen = MakePen(Color.FromArgb(255, 255, 255, 255), 3);
            dc.DrawGeometry(fillBrush, strokePen, geo);

            // Center dot at tip
            DrawDot(dc, tipX, tipY, 6, MakeBrush(255));
        }

        // ── Style 1: Glow Ring (脉冲光环) ───────────────────────────

        private void DrawGlowRing(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize / 2;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.12 * Math.Sin(_phase * 2.5) : 1.0;
            double radius = r * pulse;

            // Outer glow
            for (int i = 3; i >= 1; i--)
            {
                double gr = radius + i * 8;
                byte a = (byte)(20 / i);
                DrawEllipse(dc, cx, cy, gr, MakePen(a, 6));
            }

            // Main ring
            DrawEllipse(dc, cx, cy, radius, MakePen(200, 3), MakeBrush(30));

            // Center dot
            double dr = 4 * pulse;
            DrawDot(dc, cx, cy, dr, MakeBrush(220));

            // Rotating dashes
            double dashR = radius * 0.7;
            var dashPen = MakePen(120, 2);
            for (int i = 0; i < 4; i++)
            {
                double a = _phase * 1.5 + Math.PI * 2 * i / 4;
                double dx = cx + dashR * Math.Cos(a);
                double dy = cy + dashR * Math.Sin(a);
                DrawDash(dc, dx, dy, a, 6 * pulse, dashPen);
            }
        }

        // ── Style 2: Arrow (旋转箭头) ───────────────────────────────

        private void DrawArrow(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 0.5;
            double orbitR = size * 1.8;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.1 * Math.Sin(_phase * 2) : 1.0;
            var brush = MakeBrush(220);
            var glowBrush = MakeBrush(60);
            var centerGlowBrush = MakeBrush(100);
            var strokePen = MakePen(Color.FromArgb(180, 255, 255, 255), 1.5);
            var tailPen = MakePen(100, 2);

            // Center glow
            DrawEllipse(dc, cx, cy, 12 * pulse, MakePen(0, 0), glowBrush);
            dc.DrawEllipse(centerGlowBrush, null, new Point(cx, cy), 12 * pulse, 12 * pulse);

            // 3 rotating arrows pointing inward toward center
            for (int i = 0; i < 3; i++)
            {
                double angle = _phase * 1.2 + Math.PI * 2 * i / 3;
                double ax = cx + orbitR * Math.Cos(angle);
                double ay = cy + orbitR * Math.Sin(angle);

                double toCenter = Math.Atan2(cy - ay, cx - ax);
                double arrowLen = size * pulse * 0.6;

                double tipX = ax + arrowLen * Math.Cos(toCenter);
                double tipY = ay + arrowLen * Math.Sin(toCenter);

                double wing1A = toCenter + Math.PI * 0.75;
                double wing2A = toCenter - Math.PI * 0.75;
                double wingLen = arrowLen * 0.6;

                // Arrow polygon
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(tipX, tipY), true, true);
                    ctx.LineTo(new Point(ax + wingLen * Math.Cos(wing1A), ay + wingLen * Math.Sin(wing1A)), true, false);
                    ctx.LineTo(new Point(ax, ay), true, false);
                    ctx.LineTo(new Point(ax + wingLen * Math.Cos(wing2A), ay + wingLen * Math.Sin(wing2A)), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(brush, strokePen, geo);

                // Trailing line
                double tailLen = size * 0.8;
                DrawLine(dc, ax, ay,
                    ax - tailLen * Math.Cos(toCenter),
                    ay - tailLen * Math.Sin(toCenter), tailPen);
            }

            // Center dot
            DrawDot(dc, cx, cy, 5 * pulse, brush);
        }

        // ── Style 3: Ripple (涟漪水波) ──────────────────────────────

        private void DrawRipple(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double maxR = _settings!.IndicatorSize * 1.5;
            int ringCount = 4;

            for (int i = 0; i < ringCount; i++)
            {
                double t = (_phase * 0.8 + i * 0.8) % (ringCount * 0.8);
                double progress = t / (ringCount * 0.8);
                double radius = progress * maxR;
                byte alpha = (byte)(180 * (1 - progress));

                if (radius > 0)
                {
                    double strokeW = 2.5 * (1 - progress * 0.5);
                    DrawEllipse(dc, cx, cy, radius, MakePen(alpha, strokeW));
                }
            }

            // Center dot
            double dotPulse = _settings.PulseAnimation ? 1.0 + 0.2 * Math.Sin(_phase * 3) : 1.0;
            DrawDot(dc, cx, cy, 5 * dotPulse, MakeBrush(200));
        }

        // ── Style 4: Spotlight (聚光灯) ─────────────────────────────

        private void DrawSpotlight(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 2;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.08 * Math.Sin(_phase * 2) : 1.0;
            double r = size * pulse;

            // Radial gradient circle
            var grad = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(80, _baseColor.R, _baseColor.G, _baseColor.B), 0),
                    new(Color.FromArgb(40, _baseColor.R, _baseColor.G, _baseColor.B), 0.4),
                    new(Color.FromArgb(10, _baseColor.R, _baseColor.G, _baseColor.B), 0.7),
                    new(Color.FromArgb(0, _baseColor.R, _baseColor.G, _baseColor.B), 1)
                }
            };
            grad.Freeze();

            dc.DrawEllipse(grad, null, new Point(cx, cy), r, r);

            // Bright inner ring
            DrawEllipse(dc, cx, cy, r * 0.25, MakePen(60, 2));

            // Center cross
            double crossSize = 8;
            var crossPen = MakePen(180, 1.5);
            DrawLine(dc, cx - crossSize, cy, cx + crossSize, cy, crossPen);
            DrawLine(dc, cx, cy - crossSize, cx, cy + crossSize, crossPen);

            // Edge ring
            DrawEllipse(dc, cx, cy, r * 0.95, MakePen(25, 1));
        }

        // ── Style 5: Crosshair (十字准星) ───────────────────────────

        private void DrawCrosshair(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 0.8;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.05 * Math.Sin(_phase * 3) : 1.0;
            var colorPen = MakePen(200, 2);
            var dimPen = MakePen(80, 1.5);
            var thinPen = MakePen(80, 1);
            double gap = 8;
            double len = size * pulse;
            double rot = _phase * 0.3;

            // Outer circle
            DrawEllipse(dc, cx, cy, len, dimPen);

            // Inner circle
            DrawEllipse(dc, cx, cy, len * 0.15, MakePen(200, 1));

            // Crosshair lines (with gap in center)
            DrawRotatedLine(dc, cx, cy - gap - len * 0.3, cx, cy - gap, colorPen, cx, cy, rot);
            DrawRotatedLine(dc, cx, cy + gap, cx, cy + gap + len * 0.3, colorPen, cx, cy, rot);
            DrawRotatedLine(dc, cx - gap - len * 0.3, cy, cx - gap, cy, colorPen, cx, cy, rot);
            DrawRotatedLine(dc, cx + gap, cy, cx + gap + len * 0.3, cy, colorPen, cx, cy, rot);

            // Tick marks at cardinal points
            double tickLen = 4;
            for (int i = 0; i < 12; i++)
            {
                double a = rot + Math.PI * 2 * i / 12;
                double inner = len * 0.85;
                double outer = len * 0.85 + tickLen;
                DrawLine(dc,
                    cx + inner * Math.Cos(a), cy + inner * Math.Sin(a),
                    cx + outer * Math.Cos(a), cy + outer * Math.Sin(a),
                    thinPen);
            }

            // Center dot
            DrawDot(dc, cx, cy, 2.5, MakeBrush(200));
        }

        // ── Style 6: Beacon (脉冲信标) ──────────────────────────────

        private void DrawBeacon(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double maxR = _settings!.IndicatorSize * 1.2;

            double sweepAngle = _phase * 1.5;
            double sweepLen = maxR * 0.9;

            // Sweep trail (fade)
            for (int i = 0; i < 8; i++)
            {
                double a = sweepAngle - i * 0.08;
                byte alpha = (byte)(80 - i * 9);
                if (alpha <= 0) continue;
                DrawLine(dc, cx, cy,
                    cx + sweepLen * Math.Cos(a), cy + sweepLen * Math.Sin(a),
                    MakePen(alpha, 2 - i * 0.2));
            }

            // Concentric rings
            int ringCount = 3;
            var ringPen = MakePen(40, 1);
            for (int i = 1; i <= ringCount; i++)
            {
                double r = maxR * i / ringCount;
                DrawEllipse(dc, cx, cy, r, ringPen);
            }

            // Cross axes
            var axisPen = MakePen(25, 0.5);
            DrawLine(dc, cx - maxR, cy, cx + maxR, cy, axisPen);
            DrawLine(dc, cx, cy - maxR, cx, cy + maxR, axisPen);

            // "Blip" dots on the sweep
            for (int i = 0; i < 3; i++)
            {
                double blipAngle = sweepAngle - (i + 1) * 0.5;
                double blipDist = maxR * (0.3 + i * 0.2);
                double bx = cx + blipDist * Math.Cos(blipAngle);
                double by = cy + blipDist * Math.Sin(blipAngle);
                double fade = Math.Max(0, 1 - i * 0.35);
                DrawDot(dc, bx, by, 3 * fade,
                    new SolidColorBrush(Color.FromArgb((byte)(200 * fade), _baseColor.R, _baseColor.G, _baseColor.B)));
            }

            // Center dot
            double dotPulse = _settings.PulseAnimation ? 1.0 + 0.15 * Math.Sin(_phase * 4) : 1.0;
            DrawDot(dc, cx, cy, 4 * dotPulse, MakeBrush(220));
        }

        // ── Style 7: Big Arrow (醒目大箭头) ────────────────────────
        // A large arrow hovering ~200px away, pointing at the cursor.
        // Slowly orbits around the cursor; the arrow body pulses.

        private void DrawBigArrow(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double baseSize = _settings!.IndicatorSize * 1.2;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.08 * Math.Sin(_phase * 2) : 1.0;
            double arrowSize = baseSize * pulse;

            // Arrow orbits around the cursor at a distance
            double orbitR = arrowSize * 2.5;
            double orbitAngle = _phase * 0.6; // slow orbit

            // Arrow tip is at the cursor, body extends outward
            // So the arrow "points at" the cursor
            double tipX = cx;
            double tipY = cy;
            double bodyAngle = orbitAngle + Math.PI; // arrow body goes away from cursor

            // Body center is at orbitR distance from cursor
            double bodyX = cx + orbitR * Math.Cos(orbitAngle);
            double bodyY = cy + orbitR * Math.Sin(orbitAngle);

            // Arrow geometry: tip at cursor, wings at body center
            double wingHalf = arrowSize * 0.45;
            double bodyLen = arrowSize * 1.6;

            // Direction from body to tip
            double dirX = tipX - bodyX;
            double dirY = tipY - bodyY;
            double dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (dirLen < 1) return;
            double nx = dirX / dirLen, ny = dirY / dirLen; // unit vector toward cursor
            double px = -ny, py = nx;                       // perpendicular

            // Wing points (perpendicular to arrow direction at the body)
            double w1x = bodyX + px * wingHalf, w1y = bodyY + py * wingHalf;
            double w2x = bodyX - px * wingHalf, w2y = bodyY - py * wingHalf;

            // Tail end (extends further out from body)
            double tailX = bodyX - nx * bodyLen;
            double tailY = bodyY - ny * bodyLen;

            // Tail notch (V-shape at the back)
            double notchDepth = arrowSize * 0.35;
            double n1x = w1x - nx * notchDepth + px * notchDepth * 0.3;
            double n1y = w1y - ny * notchDepth + py * notchDepth * 0.3;
            double n2x = w2x - nx * notchDepth - px * notchDepth * 0.3;
            double n2y = w2y - ny * notchDepth - py * notchDepth * 0.3;

            // Glow (drawn first, behind the arrow)
            var glowBrush = MakeBrush(40);
            dc.DrawEllipse(glowBrush, null, new Point(cx, cy), arrowSize * 0.5, arrowSize * 0.5);

            // Arrow shape with V-notch tail
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(tipX, tipY), true, true);
                ctx.LineTo(new Point(w1x, w1y), true, false);
                ctx.LineTo(new Point(n1x, n1y), true, false);
                ctx.LineTo(new Point(tailX, tailY), true, false);
                ctx.LineTo(new Point(n2x, n2y), true, false);
                ctx.LineTo(new Point(w2x, w2y), true, false);
            }
            geo.Freeze();

            // Fill + stroke
            var fillBrush = MakeBrush(200);
            var strokePen = MakePen(Color.FromArgb(255, 255, 255, 255), 2);
            dc.DrawGeometry(fillBrush, strokePen, geo);

            // Center dot at cursor
            DrawDot(dc, cx, cy, 5, MakeBrush(255));

            // Trail dots along the arrow axis (motion feel)
            var trailPen = MakePen(80, 2);
            for (int i = 1; i <= 3; i++)
            {
                double t = i * 0.15;
                double tx = tipX + dirX * t * 0.3;
                double ty = tipY + dirY * t * 0.3;
                DrawDot(dc, tx, ty, 2.0 / i, MakeBrush((byte)(120 / i)));
            }
        }

        // ── Style 8: Target (HUD 靶心) ─────────────────────────────
        // Military HUD look: rotating corner brackets, dual circles,
        // crosshairs with gaps, distance ticks, and data readout feel.

        private void DrawTarget(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 1.0;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.04 * Math.Sin(_phase * 3) : 1.0;
            double r = size * pulse;
            double rot = _phase * 0.4; // slow rotation

            var mainPen = MakePen(220, 2);
            var dimPen = MakePen(100, 1);
            var thinPen = MakePen(60, 1);

            // Outer circle
            DrawEllipse(dc, cx, cy, r, dimPen);

            // Inner circle
            DrawEllipse(dc, cx, cy, r * 0.35, MakePen(150, 1.5));

            // Crosshairs with center gap
            double gap = r * 0.12;
            double armLen = r * 0.9;
            DrawRotatedLine(dc, cx, cy - gap, cx, cy - armLen, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx, cy + gap, cx, cy + armLen, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx - gap, cy, cx - armLen, cy, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx + gap, cy, cx + armLen, cy, mainPen, cx, cy, rot);

            // Corner brackets (HUD-style L-shapes at 4 corners)
            double bracketLen = r * 0.3;
            double bracketOffset = r * 0.85;
            double bracketAngle = rot + Math.PI * 0.25; // 45° offset so brackets sit at corners

            for (int i = 0; i < 4; i++)
            {
                double a = bracketAngle + Math.PI * 0.5 * i;
                double cos = Math.Cos(a), sin = Math.Sin(a);

                // Corner point
                double cornerX = cx + bracketOffset * cos;
                double cornerY = cy + bracketOffset * sin;

                // Two arms of the bracket (perpendicular)
                double arm1X = cornerX - sin * bracketLen;
                double arm1Y = cornerY + cos * bracketLen;
                double arm2X = cornerX + sin * bracketLen;
                double arm2Y = cornerY - cos * bracketLen;

                // Bracket arm going "inward" along radius
                double inwardX = cornerX - cos * bracketLen * 0.6;
                double inwardY = cornerY - sin * bracketLen * 0.6;

                DrawLine(dc, cornerX, cornerY, arm1X, arm1Y, mainPen);
                DrawLine(dc, cornerX, cornerY, arm2X, arm2Y, mainPen);
            }

            // Tick marks on outer circle
            double tickLen = r * 0.08;
            for (int i = 0; i < 16; i++)
            {
                double a = rot + Math.PI * 2 * i / 16;
                double inner = r - tickLen;
                double outer = r;
                var pen = (i % 4 == 0) ? mainPen : thinPen;
                DrawLine(dc,
                    cx + inner * Math.Cos(a), cy + inner * Math.Sin(a),
                    cx + outer * Math.Cos(a), cy + outer * Math.Sin(a),
                    pen);
            }

            // Small diamond at center
            double dr = 3;
            var diamondPen = MakePen(200, 1.5);
            var diamondGeo = new StreamGeometry();
            using (var ctx = diamondGeo.Open())
            {
                ctx.BeginFigure(new Point(cx, cy - dr), true, true);
                ctx.LineTo(new Point(cx + dr, cy), true, false);
                ctx.LineTo(new Point(cx, cy + dr), true, false);
                ctx.LineTo(new Point(cx - dr, cy), true, false);
            }
            diamondGeo.Freeze();
            dc.DrawGeometry(MakeBrush(220), diamondPen, diamondGeo);
        }

        // ── Style 9: Spiral (螺旋汇聚) ─────────────────────────────
        // Spiral arms converging on the cursor, creating a hypnotic
        // pull-attention effect.  Arms rotate and pulse inward.

        private void DrawSpiral(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double maxR = _settings!.IndicatorSize * 1.5;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.06 * Math.Sin(_phase * 2) : 1.0;
            int arms = 3;
            int segments = 60; // points per spiral arm
            double rotations = 2.5; // how many full turns per arm

            // Draw each spiral arm as a polyline
            for (int arm = 0; arm < arms; arm++)
            {
                double armOffset = Math.PI * 2 * arm / arms + _phase * 0.8;
                var points = new Point[segments];

                for (int i = 0; i < segments; i++)
                {
                    double t = (double)i / (segments - 1); // 0 → 1 (outside → center)
                    double r = maxR * (1 - t) * pulse;      // radius shrinks toward center
                    double angle = armOffset + rotations * Math.PI * 2 * t;
                    points[i] = new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
                }

                // Build polyline geometry
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(points[0], false, false);
                    for (int i = 1; i < segments; i++)
                        ctx.LineTo(points[i], true, false);
                }
                geo.Freeze();

                // Gradient alpha: bright at outside, dim at center
                // Since we can't do per-segment gradient on a single path,
                // we draw each arm in 2 passes: outer half bright, inner half dim
                var outerPen = MakePen(180, 2.5);
                var innerPen = MakePen(80, 1.5);
                dc.DrawGeometry(null, outerPen, geo);
                dc.DrawGeometry(null, innerPen, geo);
            }

            // Concentric pulse rings
            for (int i = 1; i <= 3; i++)
            {
                double ringR = maxR * i / 3 * pulse * (0.3 + 0.1 * Math.Sin(_phase * 3 + i));
                byte alpha = (byte)(40 + 20 * i);
                DrawEllipse(dc, cx, cy, ringR, MakePen(alpha, 1));
            }

            // Center dot with strong pulse
            double dotPulse = _settings.PulseAnimation ? 1.0 + 0.3 * Math.Sin(_phase * 4) : 1.0;
            DrawDot(dc, cx, cy, 6 * dotPulse, MakeBrush(220));
            DrawDot(dc, cx, cy, 3 * dotPulse, MakeBrush(255));
        }
    }
}
