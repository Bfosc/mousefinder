using System;
using System.Linq;
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
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

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
        GetCursorPos(out var pt);
        double scale = GetScaleForPoint(pt);
        // Convert screen coordinates to virtual-screen-relative coordinates
        double x = pt.X / scale - SystemParameters.VirtualScreenLeft;
        double y = pt.Y / scale - SystemParameters.VirtualScreenTop;
        ShowIndicator(new Point(x, y));
    }

    private static double GetScaleForPoint(POINT pt)
    {
        try
        {
            IntPtr hmon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hmon != IntPtr.Zero)
            {
                int hr = GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
                if (hr == 0 && dpiX > 0)
                    return dpiX / 96.0;
            }
        }
        catch (System.DllNotFoundException) { }
        catch (System.EntryPointNotFoundException) { }

        return GetDpiForSystem() / 96.0;
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
                case IndicatorStyle.EdgeArrow: DrawBigArrow(dc); break;
                case IndicatorStyle.MinimalPulse: DrawMinimalPulse(dc); break;
                case IndicatorStyle.GlassOrb: DrawGlassOrb(dc); break;
                case IndicatorStyle.NeonRing: DrawNeonRing(dc); break;
                case IndicatorStyle.ParticleField: DrawParticleField(dc); break;
                case IndicatorStyle.Aurora: DrawAurora(dc); break;
                case IndicatorStyle.FocusSpot: DrawFocusSpot(dc); break;
                case IndicatorStyle.MagneticDot: DrawMagneticDot(dc); break;
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

        private RadialGradientBrush MakeRadialBrush(double cx, double cy, double r, params (byte alpha, double offset)[] stops)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
                GradientStops = new GradientStopCollection(stops.Select(s =>
                    new GradientStop(Color.FromArgb(s.alpha, _baseColor.R, _baseColor.G, _baseColor.B), s.offset)))
            };
            brush.Freeze();
            return brush;
        }

        private void DrawEllipse(DrawingContext dc, double cx, double cy, double radius, Pen stroke, Brush? fill = null)
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

        private void DrawDash(DrawingContext dc, double dx, double dy, double angle, double len, Pen pen)
        {
            dc.DrawLine(pen,
                new Point(dx - len * Math.Cos(angle), dy - len * Math.Sin(angle)),
                new Point(dx + len * Math.Cos(angle), dy + len * Math.Sin(angle)));
        }

        // ══════════════════════════════════════════════════════════════
        //  Original Styles (经典样式) — Enhanced
        // ══════════════════════════════════════════════════════════════

        // ── Glow Ring (脉冲光环) ─────────────────────────────────────
        // 6-layer soft glow, smooth breathing pulse, orbiting dashes with trails.

        private void DrawGlowRing(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize / 2;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.15 * Math.Sin(_phase * 2.5) : 1.0;
            double radius = r * pulse;

            // Outer bloom (6 layers, soft falloff)
            for (int i = 6; i >= 1; i--)
            {
                double gr = radius + i * 10;
                byte a = (byte)(18 / (i * i) + 2);
                double w = 10.0 - i;
                DrawEllipse(dc, cx, cy, gr, MakePen(a, w));
            }

            // Soft inner fill
            var innerGlow = MakeRadialBrush(cx, cy, radius,
                (35, 0), (15, 0.4), (5, 0.7), (0, 1));
            dc.DrawEllipse(innerGlow, null, new Point(cx, cy), radius, radius);

            // Main ring (bright, crisp)
            DrawEllipse(dc, cx, cy, radius, MakePen(220, 2.5));

            // Orbiting dashes with trailing fade (6 dashes)
            double dashR = radius * 0.65;
            for (int i = 0; i < 6; i++)
            {
                double a = _phase * 1.8 + Math.PI * 2 * i / 6;
                double dx = cx + dashR * Math.Cos(a);
                double dy = cy + dashR * Math.Sin(a);
                double len = (5 + 3 * Math.Sin(_phase + i)) * pulse;
                byte alpha = (byte)(160 - i * 15);
                DrawDash(dc, dx, dy, a + Math.PI / 2, len, MakePen(alpha, 2));
            }

            // Center glow + dot
            var centerGlow = MakeRadialBrush(cx, cy, 10 * pulse,
                (80, 0), (30, 0.4), (0, 1));
            dc.DrawEllipse(centerGlow, null, new Point(cx, cy), 10 * pulse, 10 * pulse);
            DrawDot(dc, cx, cy, 4 * pulse, MakeBrush(240));
        }

        // ── Arrow (旋转箭头) ────────────────────────────────────────
        // 3 arrows orbiting with glow trails, center pulse, white stroke.

        private void DrawArrow(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 0.5;
            double orbitR = size * 1.8;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.1 * Math.Sin(_phase * 2) : 1.0;
            var brush = MakeBrush(220);
            var strokePen = MakePen(Color.FromArgb(200, 255, 255, 255), 1.5);

            // Center glow (multi-layer)
            var outerGlow = MakeRadialBrush(cx, cy, 25 * pulse,
                (40, 0), (15, 0.3), (5, 0.6), (0, 1));
            dc.DrawEllipse(outerGlow, null, new Point(cx, cy), 25 * pulse, 25 * pulse);
            DrawDot(dc, cx, cy, 6 * pulse, MakeBrush(180));

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

                // Arrow glow (soft halo)
                var arrowGlow = MakeRadialBrush(ax, ay, arrowLen,
                    (20, 0), (8, 0.4), (0, 1));
                dc.DrawEllipse(arrowGlow, null, new Point(ax, ay), arrowLen, arrowLen);

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

                // Tail with fade
                double tailLen = size * 0.8;
                var tailPen = MakePen(80, 1.5);
                DrawLine(dc, ax, ay,
                    ax - tailLen * Math.Cos(toCenter),
                    ay - tailLen * Math.Sin(toCenter), tailPen);
            }

            DrawDot(dc, cx, cy, 5 * pulse, MakeBrush(255));
        }

        // ── Ripple (涟漪水波) ───────────────────────────────────────
        // 6 expanding rings with smooth alpha falloff, highlight on each ring.

        private void DrawRipple(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double maxR = _settings!.IndicatorSize * 1.8;
            int ringCount = 6;

            for (int i = 0; i < ringCount; i++)
            {
                double t = (_phase * 0.7 + i * 0.7) % (ringCount * 0.7);
                double progress = t / (ringCount * 0.7);
                double radius = progress * maxR;
                // Smooth cubic falloff
                double fade = 1.0 - progress;
                byte alpha = (byte)(200 * fade * fade);

                if (radius > 2)
                {
                    double strokeW = 3.0 * fade;
                    DrawEllipse(dc, cx, cy, radius, MakePen(alpha, strokeW));

                    // Highlight arc on leading edge
                    if (fade > 0.3)
                    {
                        byte hlAlpha = (byte)(100 * fade);
                        double hlAngle = _phase * 2 + i * 0.5;
                        double hlR = radius;
                        double hlLen = Math.PI * 0.3;
                        for (int s = 0; s < 5; s++)
                        {
                            double sa = hlAngle - hlLen + hlLen * 2 * s / 5;
                            double sx = cx + hlR * Math.Cos(sa);
                            double sy = cy + hlR * Math.Sin(sa);
                            DrawDot(dc, sx, sy, 1.5 * fade, MakeBrush(hlAlpha));
                        }
                    }
                }
            }

            // Center glow + dot
            var centerGlow = MakeRadialBrush(cx, cy, 15,
                (60, 0), (20, 0.4), (0, 1));
            dc.DrawEllipse(centerGlow, null, new Point(cx, cy), 15, 15);
            double dotPulse = _settings.PulseAnimation ? 1.0 + 0.2 * Math.Sin(_phase * 3) : 1.0;
            DrawDot(dc, cx, cy, 5 * dotPulse, MakeBrush(220));
        }

        // ── Spotlight (聚光灯) ──────────────────────────────────────
        //  Layered radial glow with subtle light rays and dust particles.

        private void DrawSpotlight(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 2.2;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.08 * Math.Sin(_phase * 2) : 1.0;
            double r = size * pulse;

            // Outer vignette (dark edge)
            var vignette = MakeRadialBrush(cx, cy, r * 1.2,
                (0, 0), (0, 0.6), (5, 0.8), (15, 0.95), (25, 1));
            dc.DrawEllipse(vignette, null, new Point(cx, cy), r * 1.2, r * 1.2);

            // Main spotlight cone (bright center, smooth falloff)
            var grad = MakeRadialBrush(cx, cy, r,
                (90, 0), (55, 0.2), (30, 0.4), (12, 0.6), (4, 0.8), (0, 1));
            dc.DrawEllipse(grad, null, new Point(cx, cy), r, r);

            // Bright core
            var core = MakeRadialBrush(cx, cy, r * 0.25,
                (120, 0), (50, 0.3), (0, 1));
            dc.DrawEllipse(core, null, new Point(cx, cy), r * 0.25, r * 0.25);

            // Light rays (subtle, rotating)
            var rayPen = MakePen(15, 1);
            for (int i = 0; i < 8; i++)
            {
                double a = _phase * 0.3 + Math.PI * 2 * i / 8;
                double inner = r * 0.3;
                double outer = r * 0.85;
                DrawLine(dc,
                    cx + inner * Math.Cos(a), cy + inner * Math.Sin(a),
                    cx + outer * Math.Cos(a), cy + outer * Math.Sin(a),
                    rayPen);
            }

            // Inner ring + crosshair
            DrawEllipse(dc, cx, cy, r * 0.25, MakePen(60, 1.5));
            double crossSize = 8;
            var crossPen = MakePen(180, 1.5);
            DrawLine(dc, cx - crossSize, cy, cx + crossSize, cy, crossPen);
            DrawLine(dc, cx, cy - crossSize, cx, cy + crossSize, crossPen);

            // Dust particles (tiny floating dots)
            for (int i = 0; i < 6; i++)
            {
                double a = _phase * 0.5 + Math.PI * 2 * i / 6 + i * 0.7;
                double dr = r * (0.3 + 0.4 * Math.Sin(_phase + i * 1.3));
                double dx = cx + dr * Math.Cos(a);
                double dy = cy + dr * Math.Sin(a);
                DrawDot(dc, dx, dy, 1.2, MakeBrush((byte)(30 + 10 * i)));
            }

            DrawEllipse(dc, cx, cy, r * 0.95, MakePen(20, 1));
        }

        // ── Crosshair (十字准星) ────────────────────────────────────
        //  Tactical scope with outer ring, tick marks, distance indicators.

        private void DrawCrosshair(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 0.9;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.05 * Math.Sin(_phase * 3) : 1.0;
            var mainPen = MakePen(220, 2);
            var dimPen = MakePen(80, 1.5);
            var thinPen = MakePen(60, 1);
            double gap = 10;
            double len = size * pulse;
            double rot = _phase * 0.3;

            // Outer scope ring
            DrawEllipse(dc, cx, cy, len, dimPen);
            DrawEllipse(dc, cx, cy, len * 0.7, MakePen(40, 0.8));
            DrawEllipse(dc, cx, cy, len * 0.15, MakePen(200, 1));

            // Crosshair arms (gap from center)
            DrawRotatedLine(dc, cx, cy - gap - len * 0.35, cx, cy - gap, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx, cy + gap, cx, cy + gap + len * 0.35, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx - gap - len * 0.35, cy, cx - gap, cy, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx + gap, cy, cx + gap + len * 0.35, cy, mainPen, cx, cy, rot);

            // Tick marks on outer ring (major + minor)
            for (int i = 0; i < 16; i++)
            {
                double a = rot + Math.PI * 2 * i / 16;
                double inner = len * 0.88;
                double outer = len;
                double tickW = (i % 4 == 0) ? 4 : 2;
                var pen = (i % 4 == 0) ? mainPen : thinPen;
                DrawLine(dc,
                    cx + inner * Math.Cos(a), cy + inner * Math.Sin(a),
                    cx + outer * Math.Cos(a), cy + outer * Math.Sin(a),
                    pen);
            }

            // Diagonal lines (subtle)
            var diagPen = MakePen(25, 0.5);
            for (int i = 0; i < 4; i++)
            {
                double a = rot + Math.PI * 0.25 + Math.PI * 0.5 * i;
                DrawLine(dc,
                    cx + len * 0.2 * Math.Cos(a), cy + len * 0.2 * Math.Sin(a),
                    cx + len * 0.65 * Math.Cos(a), cy + len * 0.65 * Math.Sin(a),
                    diagPen);
            }

            DrawDot(dc, cx, cy, 2.5, MakeBrush(220));
        }

        // ── Beacon (脉冲信标) ───────────────────────────────────────
        //  Radar sweep with gradient fade, glowing blips, scan lines.

        private void DrawBeacon(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double maxR = _settings!.IndicatorSize * 1.3;

            double sweepAngle = _phase * 1.5;

            // Radar sweep (gradient fade tail)
            for (int i = 0; i < 12; i++)
            {
                double a = sweepAngle - i * 0.06;
                byte alpha = (byte)(70 - i * 5);
                if (alpha <= 0) continue;
                double w = 3.0 - i * 0.2;
                DrawLine(dc, cx, cy,
                    cx + maxR * Math.Cos(a), cy + maxR * Math.Sin(a),
                    MakePen(alpha, w));
            }

            // Sweep glow at leading edge
            double sweepTipX = cx + maxR * Math.Cos(sweepAngle);
            double sweepTipY = cy + maxR * Math.Sin(sweepAngle);
            var sweepGlow = MakeRadialBrush(sweepTipX, sweepTipY, maxR * 0.15,
                (40, 0), (15, 0.4), (0, 1));
            dc.DrawEllipse(sweepGlow, null, new Point(sweepTipX, sweepTipY), maxR * 0.15, maxR * 0.15);

            // Concentric rings (4 rings, varying opacity)
            for (int i = 1; i <= 4; i++)
            {
                double r = maxR * i / 4;
                byte a = (byte)(30 + 10 * i);
                DrawEllipse(dc, cx, cy, r, MakePen(a, 0.8));
            }

            // Axis lines (subtle cross)
            var axisPen = MakePen(20, 0.5);
            DrawLine(dc, cx - maxR, cy, cx + maxR, cy, axisPen);
            DrawLine(dc, cx, cy - maxR, cx, cy + maxR, axisPen);

            // Glowing blips with halos
            for (int i = 0; i < 4; i++)
            {
                double blipAngle = sweepAngle - (i + 1) * 0.4;
                double blipDist = maxR * (0.25 + i * 0.18);
                double bx = cx + blipDist * Math.Cos(blipAngle);
                double by = cy + blipDist * Math.Sin(blipAngle);
                double fade = Math.Max(0, 1 - i * 0.25);

                // Blip halo
                var blipGlow = MakeRadialBrush(bx, by, 8 * fade,
                    (60, 0), (20, 0.4), (0, 1));
                dc.DrawEllipse(blipGlow, null, new Point(bx, by), 8 * fade, 8 * fade);

                var blipBrush = new SolidColorBrush(Color.FromArgb((byte)(220 * fade), _baseColor.R, _baseColor.G, _baseColor.B));
                blipBrush.Freeze();
                DrawDot(dc, bx, by, 3 * fade, blipBrush);
            }

            // Center dot with pulse
            double dotPulse = _settings.PulseAnimation ? 1.0 + 0.15 * Math.Sin(_phase * 4) : 1.0;
            var centerGlow = MakeRadialBrush(cx, cy, 12 * dotPulse,
                (80, 0), (30, 0.4), (0, 1));
            dc.DrawEllipse(centerGlow, null, new Point(cx, cy), 12 * dotPulse, 12 * dotPulse);
            DrawDot(dc, cx, cy, 4 * dotPulse, MakeBrush(240));
        }

        // ── Big Arrow (醒目大箭头) ──────────────────────────────────
        //  Orbiting arrow with motion blur trail, glow halo, center marker.

        private void DrawBigArrow(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double baseSize = _settings!.IndicatorSize * 1.2;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.08 * Math.Sin(_phase * 2) : 1.0;
            double arrowSize = baseSize * pulse;

            double orbitR = arrowSize * 2.5;
            double orbitAngle = _phase * 0.6;

            double tipX = cx;
            double tipY = cy;

            double bodyX = cx + orbitR * Math.Cos(orbitAngle);
            double bodyY = cy + orbitR * Math.Sin(orbitAngle);

            double wingHalf = arrowSize * 0.45;
            double bodyLen = arrowSize * 1.6;

            double dirX = tipX - bodyX;
            double dirY = tipY - bodyY;
            double dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (dirLen < 1) return;
            double nx = dirX / dirLen, ny = dirY / dirLen;
            double px = -ny, py = nx;

            double w1x = bodyX + px * wingHalf, w1y = bodyY + py * wingHalf;
            double w2x = bodyX - px * wingHalf, w2y = bodyY - py * wingHalf;

            double tailX = bodyX - nx * bodyLen;
            double tailY = bodyY - ny * bodyLen;

            double notchDepth = arrowSize * 0.35;
            double n1x = w1x - nx * notchDepth + px * notchDepth * 0.3;
            double n1y = w1y - ny * notchDepth + py * notchDepth * 0.3;
            double n2x = w2x - nx * notchDepth - px * notchDepth * 0.3;
            double n2y = w2y - ny * notchDepth - py * notchDepth * 0.3;

            // Center glow (multi-layer)
            var outerGlow = MakeRadialBrush(cx, cy, arrowSize * 0.6,
                (30, 0), (12, 0.3), (4, 0.6), (0, 1));
            dc.DrawEllipse(outerGlow, null, new Point(cx, cy), arrowSize * 0.6, arrowSize * 0.6);

            // Motion blur trail (3 faded ghosts)
            for (int t = 3; t >= 1; t--)
            {
                double trailAngle = orbitAngle - t * 0.15;
                double tx = cx + orbitR * Math.Cos(trailAngle);
                double ty = cy + orbitR * Math.Sin(trailAngle);
                double tDirX = cx - tx, tDirY = cy - ty;
                double tDirLen = Math.Sqrt(tDirX * tDirX + tDirY * tDirY);
                if (tDirLen < 1) continue;
                double tnx = tDirX / tDirLen, tny = tDirY / tDirLen;
                double tpx = -tny, tpy = tnx;

                double tw1x = tx + tpx * wingHalf, tw1y = ty + tpy * wingHalf;
                double tw2x = tx - tpx * wingHalf, tw2y = ty - tpy * wingHalf;
                double tTailX = tx - tnx * bodyLen, tTailY = ty - tny * bodyLen;

                var trailGeo = new StreamGeometry();
                using (var ctx = trailGeo.Open())
                {
                    ctx.BeginFigure(new Point(cx, cy), true, true);
                    ctx.LineTo(new Point(tw1x, tw1y), true, false);
                    ctx.LineTo(new Point(tTailX, tTailY), true, false);
                    ctx.LineTo(new Point(tw2x, tw2y), true, false);
                }
                trailGeo.Freeze();
                byte trailAlpha = (byte)(40 / t);
                dc.DrawGeometry(MakeBrush(trailAlpha), null, trailGeo);
            }

            // Main arrow
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

            var fillBrush = MakeBrush(210);
            var strokePen = MakePen(Color.FromArgb(240, 255, 255, 255), 2.5);
            dc.DrawGeometry(fillBrush, strokePen, geo);

            // Arrow tip glow
            var tipGlow = MakeRadialBrush(tipX, tipY, 12,
                (60, 0), (20, 0.4), (0, 1));
            dc.DrawEllipse(tipGlow, null, new Point(tipX, tipY), 12, 12);

            DrawDot(dc, cx, cy, 5, MakeBrush(255));
        }

        // ── Target (HUD 靶心) ──────────────────────────────────────
        //  HUD-style reticle with corner brackets, tick marks, diamond center.

        private void DrawTarget(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 1.0;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.04 * Math.Sin(_phase * 3) : 1.0;
            double r = size * pulse;
            double rot = _phase * 0.4;

            var mainPen = MakePen(220, 2);
            var dimPen = MakePen(100, 1);
            var thinPen = MakePen(50, 0.8);

            // Outer ring + inner ring
            DrawEllipse(dc, cx, cy, r, dimPen);
            DrawEllipse(dc, cx, cy, r * 0.6, MakePen(60, 0.8));
            DrawEllipse(dc, cx, cy, r * 0.35, MakePen(150, 1.5));

            // Center crosshair arms (gap from center)
            double gap = r * 0.12;
            double armLen = r * 0.9;
            DrawRotatedLine(dc, cx, cy - gap, cx, cy - armLen, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx, cy + gap, cx, cy + armLen, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx - gap, cy, cx - armLen, cy, mainPen, cx, cy, rot);
            DrawRotatedLine(dc, cx + gap, cy, cx + armLen, cy, mainPen, cx, cy, rot);

            // Corner brackets (HUD style)
            double bracketLen = r * 0.3;
            double bracketOffset = r * 0.85;
            double bracketAngle = rot + Math.PI * 0.25;

            for (int i = 0; i < 4; i++)
            {
                double a = bracketAngle + Math.PI * 0.5 * i;
                double cos = Math.Cos(a), sin = Math.Sin(a);

                double cornerX = cx + bracketOffset * cos;
                double cornerY = cy + bracketOffset * sin;

                double arm1X = cornerX - sin * bracketLen;
                double arm1Y = cornerY + cos * bracketLen;
                double arm2X = cornerX + sin * bracketLen;
                double arm2Y = cornerY - cos * bracketLen;

                DrawLine(dc, cornerX, cornerY, arm1X, arm1Y, mainPen);
                DrawLine(dc, cornerX, cornerY, arm2X, arm2Y, mainPen);
            }

            // Tick marks on outer ring (major every 45°, minor every 22.5°)
            for (int i = 0; i < 16; i++)
            {
                double a = rot + Math.PI * 2 * i / 16;
                double inner = r - (i % 4 == 0 ? r * 0.1 : r * 0.05);
                double outer = r;
                var pen = (i % 4 == 0) ? mainPen : thinPen;
                DrawLine(dc,
                    cx + inner * Math.Cos(a), cy + inner * Math.Sin(a),
                    cx + outer * Math.Cos(a), cy + outer * Math.Sin(a),
                    pen);
            }

            // Diamond center marker
            double dr = 3.5;
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
            dc.DrawGeometry(MakeBrush(230), diamondPen, diamondGeo);
        }

        // ── Spiral (螺旋汇聚) ──────────────────────────────────────
        //  4-arm spiral with particle dots along path, converging on center.

        private void DrawSpiral(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double maxR = _settings!.IndicatorSize * 1.5;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.06 * Math.Sin(_phase * 2) : 1.0;
            int arms = 4;
            int segments = 60;
            double rotations = 2.5;

            // Spiral arms with glow
            for (int arm = 0; arm < arms; arm++)
            {
                double armOffset = Math.PI * 2 * arm / arms + _phase * 0.8;
                var points = new Point[segments];

                for (int i = 0; i < segments; i++)
                {
                    double t = (double)i / (segments - 1);
                    double r = maxR * (1 - t) * pulse;
                    double angle = armOffset + rotations * Math.PI * 2 * t;
                    points[i] = new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
                }

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(points[0], false, false);
                    for (int i = 1; i < segments; i++)
                        ctx.LineTo(points[i], true, false);
                }
                geo.Freeze();

                // Glow layer (wider, dimmer)
                dc.DrawGeometry(null, MakePen(30, 5), geo);
                // Main layer
                dc.DrawGeometry(null, MakePen(180, 2), geo);

                // Particle dots along spiral path
                for (int i = 0; i < segments; i += 8)
                {
                    double t = (double)i / (segments - 1);
                    byte alpha = (byte)(160 * (1 - t));
                    double dotR = 2.5 * (1 - t * 0.5) * pulse;
                    DrawDot(dc, points[i].X, points[i].Y, dotR, MakeBrush(alpha));
                }
            }

            // Pulsing rings
            for (int i = 1; i <= 3; i++)
            {
                double ringR = maxR * i / 3 * pulse * (0.3 + 0.1 * Math.Sin(_phase * 3 + i));
                byte alpha = (byte)(35 + 15 * i);
                DrawEllipse(dc, cx, cy, ringR, MakePen(alpha, 0.8));
            }

            // Center glow + dot
            var centerGlow = MakeRadialBrush(cx, cy, 15 * pulse,
                (70, 0), (25, 0.4), (0, 1));
            dc.DrawEllipse(centerGlow, null, new Point(cx, cy), 15 * pulse, 15 * pulse);
            double dotPulse = _settings.PulseAnimation ? 1.0 + 0.3 * Math.Sin(_phase * 4) : 1.0;
            DrawDot(dc, cx, cy, 6 * dotPulse, MakeBrush(220));
            DrawDot(dc, cx, cy, 3 * dotPulse, MakeBrush(255));
        }

        // ══════════════════════════════════════════════════════════════
        //  New Styles (新样式) — Enhanced
        // ══════════════════════════════════════════════════════════════

        // ── Minimal Pulse (极简脉冲) ────────────────────────────────
        //  Elegant breathing ring with multi-layer glow, subtle particles.

        private void DrawMinimalPulse(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 0.6;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.1 * Math.Sin(_phase * 2) : 1.0;
            double radius = r * pulse;

            // Outer bloom (3 layers)
            for (int i = 3; i >= 1; i--)
            {
                double glowR = radius + i * 12;
                byte a = (byte)(8 / i);
                DrawEllipse(dc, cx, cy, glowR, MakePen(a, 6));
            }

            // Soft outer glow
            var glowBrush = MakeRadialBrush(cx, cy, radius * 2,
                (18, 0), (8, 0.3), (3, 0.6), (0, 1));
            dc.DrawEllipse(glowBrush, null, new Point(cx, cy), radius * 2, radius * 2);

            // Main ring (thin, elegant)
            DrawEllipse(dc, cx, cy, radius, MakePen(200, 1.5));

            // Inner subtle fill
            var innerFill = MakeRadialBrush(cx, cy, radius,
                (12, 0), (4, 0.5), (0, 1));
            dc.DrawEllipse(innerFill, null, new Point(cx, cy), radius, radius);

            // Subtle orbiting particles (8 dots)
            for (int i = 0; i < 8; i++)
            {
                double a = _phase * 0.6 + Math.PI * 2 * i / 8;
                double dr = radius * 0.7;
                double dx = cx + dr * Math.Cos(a);
                double dy = cy + dr * Math.Sin(a);
                DrawDot(dc, dx, dy, 1.2, MakeBrush((byte)(60 + 10 * i)));
            }

            // Center dot (small, precise)
            double dotR = 2.5 * pulse;
            DrawDot(dc, cx, cy, dotR, MakeBrush(230));
        }

        // ── Glass Orb (玻璃光球) ───────────────────────────────────
        //  Multi-layer glass body with specular highlights, rim light, depth.

        private void DrawGlassOrb(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 0.8;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.06 * Math.Sin(_phase * 2) : 1.0;
            double radius = r * pulse;

            // Outer diffuse glow (wide, soft)
            var outerGlow = MakeRadialBrush(cx, cy, radius * 2.5,
                (20, 0), (10, 0.15), (5, 0.3), (2, 0.5), (0, 0.8), (0, 1));
            dc.DrawEllipse(outerGlow, null, new Point(cx, cy), radius * 2.5, radius * 2.5);

            // Glass body (layered transparency with depth)
            var glassBody = MakeRadialBrush(cx, cy, radius,
                (45, 0), (30, 0.2), (18, 0.4), (10, 0.6), (4, 0.8), (1, 0.95), (0, 1));
            dc.DrawEllipse(glassBody, null, new Point(cx, cy), radius, radius);

            // Rim light (edge highlight)
            DrawEllipse(dc, cx, cy, radius * 0.97, MakePen(40, 1.5));

            // Primary highlight (top-left, large)
            double hlX = cx - radius * 0.25, hlY = cy - radius * 0.25;
            var highlight = MakeRadialBrush(hlX, hlY, radius * 0.45,
                (55, 0), (25, 0.3), (8, 0.6), (0, 1));
            dc.DrawEllipse(highlight, null, new Point(hlX, hlY), radius * 0.45, radius * 0.45);

            // Secondary highlight (small, bright specular)
            double hl2X = cx - radius * 0.15, hl2Y = cy - radius * 0.35;
            var highlight2 = MakeRadialBrush(hl2X, hl2Y, radius * 0.15,
                (80, 0), (30, 0.4), (0, 1));
            dc.DrawEllipse(highlight2, null, new Point(hl2X, hl2Y), radius * 0.15, radius * 0.15);

            // Bottom reflection (subtle)
            double refY = cy + radius * 0.3;
            var bottomRef = MakeRadialBrush(cx, refY, radius * 0.3,
                (15, 0), (5, 0.4), (0, 1));
            dc.DrawEllipse(bottomRef, null, new Point(cx, refY), radius * 0.3, radius * 0.3);

            // Center bright point
            double dotR = 3 * pulse;
            DrawDot(dc, cx, cy, dotR, MakeBrush(210));
        }

        // ── Neon Ring (霓虹光环) ───────────────────────────────────
        //  Multi-layer neon glow with flicker, rotating bright spots, center bloom.

        private void DrawNeonRing(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 0.7;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.12 * Math.Sin(_phase * 2.5) : 1.0;
            double radius = r * pulse;

            // Flicker effect (subtle brightness variation)
            double flicker = 1.0 + 0.05 * Math.Sin(_phase * 8) * Math.Sin(_phase * 3.7);

            // Outer neon bloom (6 layers, wider spread)
            for (int i = 6; i >= 1; i--)
            {
                double glowR = radius + i * 12;
                byte a = (byte)(10 * flicker / i);
                double w = 10.0 - i;
                DrawEllipse(dc, cx, cy, glowR, MakePen(a, w));
            }

            // Main neon ring (bright)
            DrawEllipse(dc, cx, cy, radius, MakePen((byte)(230 * flicker), 2.5));

            // Inner neon ring (dimmer, smaller)
            DrawEllipse(dc, cx, cy, radius * 0.65, MakePen((byte)(100 * flicker), 1.5));

            // Rotating bright spots with glow halos
            int spotCount = 5;
            for (int i = 0; i < spotCount; i++)
            {
                double angle = _phase * 1.5 + Math.PI * 2 * i / spotCount;
                double sx = cx + radius * Math.Cos(angle);
                double sy = cy + radius * Math.Sin(angle);

                // Spot halo
                var spotGlow = MakeRadialBrush(sx, sy, 8 * pulse,
                    (40, 0), (15, 0.4), (0, 1));
                dc.DrawEllipse(spotGlow, null, new Point(sx, sy), 8 * pulse, 8 * pulse);

                DrawDot(dc, sx, sy, 3 * pulse, MakeBrush(255));
            }

            // Center dot with glow
            double dotR = 4 * pulse;
            var dotGlow = MakeRadialBrush(cx, cy, dotR * 4,
                (50, 0), (20, 0.3), (5, 0.6), (0, 1));
            dc.DrawEllipse(dotGlow, null, new Point(cx, cy), dotR * 4, dotR * 4);
            DrawDot(dc, cx, cy, dotR, MakeBrush(255));
        }

        // ── Particle Field (粒子汇聚) ──────────────────────────────
        //  Dense particle orbits with glowing trails, multi-ring structure.

        private void DrawParticleField(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 1.3;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.05 * Math.Sin(_phase * 2) : 1.0;

            // Outer particle ring (16 particles)
            int particleCount = 16;
            for (int i = 0; i < particleCount; i++)
            {
                double angle = _phase * 0.8 + Math.PI * 2 * i / particleCount;
                double orbitR = r * (0.8 + 0.2 * Math.Sin(_phase * 2 + i * 0.7));
                double px = cx + orbitR * Math.Cos(angle);
                double py = cy + orbitR * Math.Sin(angle);

                double particleR = 2.5 * pulse;
                byte alpha = (byte)(200 - i * 8);

                // Particle glow
                var pGlow = MakeRadialBrush(px, py, 6,
                    (30, 0), (10, 0.4), (0, 1));
                dc.DrawEllipse(pGlow, null, new Point(px, py), 6, 6);

                DrawDot(dc, px, py, particleR, MakeBrush(alpha));

                // Trail line (fading)
                double trailLen = r * 0.25;
                double trailAngle = Math.Atan2(cy - py, cx - px);
                DrawLine(dc, px, py,
                    px + trailLen * Math.Cos(trailAngle),
                    py + trailLen * Math.Sin(trailAngle),
                    MakePen((byte)(alpha / 4), 1));
            }

            // Middle particle ring (10 particles, counter-rotating)
            int midCount = 10;
            for (int i = 0; i < midCount; i++)
            {
                double angle = -_phase * 0.6 + Math.PI * 2 * i / midCount;
                double orbitR = r * 0.55;
                double px = cx + orbitR * Math.Cos(angle);
                double py = cy + orbitR * Math.Sin(angle);
                DrawDot(dc, px, py, 2 * pulse, MakeBrush(140));
            }

            // Inner particle ring (6 particles, slow)
            int innerCount = 6;
            for (int i = 0; i < innerCount; i++)
            {
                double angle = _phase * 0.3 + Math.PI * 2 * i / innerCount;
                double orbitR = r * 0.3;
                double px = cx + orbitR * Math.Cos(angle);
                double py = cy + orbitR * Math.Sin(angle);
                DrawDot(dc, px, py, 1.5 * pulse, MakeBrush(100));
            }

            // Orbital guide rings (subtle)
            DrawEllipse(dc, cx, cy, r * 0.8, MakePen(12, 0.5));
            DrawEllipse(dc, cx, cy, r * 0.55, MakePen(10, 0.5));
            DrawEllipse(dc, cx, cy, r * 0.3, MakePen(8, 0.5));

            // Center soft glow
            var centerGlow = MakeRadialBrush(cx, cy, r * 0.25,
                (35, 0), (12, 0.4), (3, 0.7), (0, 1));
            dc.DrawEllipse(centerGlow, null, new Point(cx, cy), r * 0.25, r * 0.25);

            // Center dot
            DrawDot(dc, cx, cy, 3.5 * pulse, MakeBrush(220));
        }

        // ── Aurora (极光) ──────────────────────────────────────────
        //  5 flowing layers with wobble, shimmer particles, central glow.

        private void DrawAurora(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 1.6;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.04 * Math.Sin(_phase * 1.5) : 1.0;

            // Aurora layers (5 flowing layers)
            for (int layer = 0; layer < 5; layer++)
            {
                double layerOffset = _phase * 0.25 + layer * 0.4;
                double layerR = r * (0.4 + layer * 0.15) * pulse;
                int segmentCount = 10;

                for (int i = 0; i < segmentCount; i++)
                {
                    double t = (double)i / segmentCount;
                    double angle = layerOffset + Math.PI * 2 * t;
                    double wobble = 0.25 * Math.Sin(_phase * 1.8 + t * Math.PI * 5 + layer * 0.7);
                    double wobble2 = 0.1 * Math.Sin(_phase * 3 + t * Math.PI * 3 + layer * 1.3);
                    double segR = layerR * (1 + wobble + wobble2);

                    double sx = cx + segR * Math.Cos(angle);
                    double sy = cy + segR * Math.Sin(angle);

                    byte alpha = (byte)(12 - layer * 2);
                    double dotR = (10 - layer * 1.5) * pulse;
                    DrawDot(dc, sx, sy, dotR, MakeBrush(alpha));
                }
            }

            // Shimmer particles (floating bright dots)
            for (int i = 0; i < 8; i++)
            {
                double a = _phase * 0.4 + Math.PI * 2 * i / 8 + i * 0.9;
                double dr = r * (0.3 + 0.3 * Math.Sin(_phase * 1.5 + i * 2));
                double dx = cx + dr * Math.Cos(a);
                double dy = cy + dr * Math.Sin(a);
                byte alpha = (byte)(40 + 20 * Math.Sin(_phase * 2 + i));
                DrawDot(dc, dx, dy, 1.5, MakeBrush(alpha));
            }

            // Central aurora glow
            var auroraGlow = MakeRadialBrush(cx, cy, r * 0.7,
                (22, 0), (12, 0.2), (5, 0.4), (2, 0.6), (0, 1));
            dc.DrawEllipse(auroraGlow, null, new Point(cx, cy), r * 0.7, r * 0.7);

            // Subtle ring
            DrawEllipse(dc, cx, cy, r * 0.4, MakePen(12, 0.8));

            // Center dot
            double dotPulse = _settings.PulseAnimation ? 1.0 + 0.15 * Math.Sin(_phase * 3) : 1.0;
            DrawDot(dc, cx, cy, 3.5 * dotPulse, MakeBrush(190));
        }

        // ── Focus Spot (聚焦光斑) ──────────────────────────────────
        //  Theater spotlight with light rays, vignette, dust particles, crosshair.

        private void DrawFocusSpot(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 1.0;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.06 * Math.Sin(_phase * 2) : 1.0;
            double radius = r * pulse;

            // Outer shadow/vignette (deeper)
            var vignette = MakeRadialBrush(cx, cy, radius * 2.0,
                (0, 0), (0, 0.4), (3, 0.6), (10, 0.8), (20, 0.95), (30, 1));
            dc.DrawEllipse(vignette, null, new Point(cx, cy), radius * 2.0, radius * 2.0);

            // Main spotlight (bright center, smooth falloff)
            var spotlight = MakeRadialBrush(cx, cy, radius,
                (90, 0), (60, 0.15), (35, 0.3), (15, 0.5), (5, 0.7), (1, 0.9), (0, 1));
            dc.DrawEllipse(spotlight, null, new Point(cx, cy), radius, radius);

            // Bright core
            var core = MakeRadialBrush(cx, cy, radius * 0.25,
                (130, 0), (50, 0.3), (0, 1));
            dc.DrawEllipse(core, null, new Point(cx, cy), radius * 0.25, radius * 0.25);

            // Light rays (subtle, rotating)
            for (int i = 0; i < 12; i++)
            {
                double a = _phase * 0.2 + Math.PI * 2 * i / 12;
                double inner = radius * 0.2;
                double outer = radius * 0.9;
                byte alpha = (byte)(12 + 8 * Math.Sin(_phase + i));
                DrawLine(dc,
                    cx + inner * Math.Cos(a), cy + inner * Math.Sin(a),
                    cx + outer * Math.Cos(a), cy + outer * Math.Sin(a),
                    MakePen(alpha, 0.8));
            }

            // Crosshair (thin, elegant)
            double crossLen = radius * 0.6;
            double gap = radius * 0.08;
            var crossPen = MakePen(110, 0.8);
            DrawLine(dc, cx, cy - gap - crossLen * 0.3, cx, cy - gap, crossPen);
            DrawLine(dc, cx, cy + gap, cx, cy + gap + crossLen * 0.3, crossPen);
            DrawLine(dc, cx - gap - crossLen * 0.3, cy, cx - gap, cy, crossPen);
            DrawLine(dc, cx + gap, cy, cx + gap + crossLen * 0.3, cy, crossPen);

            // Dust particles
            for (int i = 0; i < 5; i++)
            {
                double a = _phase * 0.4 + Math.PI * 2 * i / 5 + i * 1.1;
                double dr = radius * (0.2 + 0.5 * Math.Sin(_phase + i * 1.7));
                double dx = cx + dr * Math.Cos(a);
                double dy = cy + dr * Math.Sin(a);
                DrawDot(dc, dx, dy, 1.0, MakeBrush((byte)(25 + 10 * i)));
            }

            // Center dot
            DrawDot(dc, cx, cy, 2.5, MakeBrush(220));
        }

        // ── Magnetic Dot (磁力点) ──────────────────────────────────
        //  Apple-like minimal dot with multi-orbit field lines, pulse wave.

        private void DrawMagneticDot(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 0.4;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.12 * Math.Sin(_phase * 3) : 1.0;
            double radius = r * pulse;

            // Outer magnetic field (wide, soft)
            var field = MakeRadialBrush(cx, cy, radius * 5,
                (8, 0), (4, 0.2), (2, 0.4), (0, 0.7), (0, 1));
            dc.DrawEllipse(field, null, new Point(cx, cy), radius * 5, radius * 5);

            // Pulse wave (expanding ring from center)
            double waveR = radius * (2 + 2 * ((_phase * 0.5) % 1.0));
            byte waveAlpha = (byte)(30 * (1 - ((_phase * 0.5) % 1.0)));
            DrawEllipse(dc, cx, cy, waveR, MakePen(waveAlpha, 1));

            // Orbiting dots (4 orbits, varying speeds)
            int orbitCount = 4;
            for (int orbit = 0; orbit < orbitCount; orbit++)
            {
                double orbitR = radius * (1.8 + orbit * 0.7);
                int dotCount = 5 + orbit * 2;
                double speed = 1.2 - orbit * 0.2;
                double dir = (orbit % 2 == 0) ? 1 : -1;

                // Orbit guide ring (very subtle)
                DrawEllipse(dc, cx, cy, orbitR, MakePen((byte)(8 + orbit * 2), 0.5));

                for (int i = 0; i < dotCount; i++)
                {
                    double angle = _phase * speed * dir + Math.PI * 2 * i / dotCount + orbit * 0.4;
                    double dx = cx + orbitR * Math.Cos(angle);
                    double dy = cy + orbitR * Math.Sin(angle);
                    double dotR = 2.0 - orbit * 0.3;
                    byte alpha = (byte)(120 - orbit * 20);
                    DrawDot(dc, dx, dy, dotR, MakeBrush(alpha));
                }
            }

            // Inner glow (multi-layer)
            var innerGlow = MakeRadialBrush(cx, cy, radius * 2.5,
                (20, 0), (10, 0.2), (5, 0.4), (2, 0.6), (0, 1));
            dc.DrawEllipse(innerGlow, null, new Point(cx, cy), radius * 2.5, radius * 2.5);

            // Main dot (bright, clean)
            DrawDot(dc, cx, cy, radius, MakeBrush(220));

            // Bright center
            DrawDot(dc, cx, cy, radius * 0.5, MakeBrush(255));
        }

        // ── Edge Arrow (边缘箭头) ──────────────────────────────────
        //  Directional arrow from screen edge with glow trail and pulse.

        private void DrawEdgeArrow(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double size = _settings!.IndicatorSize * 2.0;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.08 * Math.Sin(_phase * 2) : 1.0;
            double arrowSize = size * pulse;

            double bodyLen = arrowSize * 2.0;
            double wingHalf = arrowSize * 0.6;

            double tipX, tipY, bodyX, bodyY;
            double dirX = 0, dirY = 0;

            switch (_edgeDirection)
            {
                case EdgeDirection.Top:
                    tipX = cx; tipY = Math.Max(cy, SystemParameters.VirtualScreenTop + arrowSize);
                    bodyX = cx; bodyY = tipY + bodyLen; dirX = 0; dirY = -1; break;
                case EdgeDirection.Bottom:
                    tipX = cx; tipY = Math.Min(cy, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - arrowSize);
                    bodyX = cx; bodyY = tipY - bodyLen; dirX = 0; dirY = 1; break;
                case EdgeDirection.Left:
                    tipX = Math.Max(cx, SystemParameters.VirtualScreenLeft + arrowSize); tipY = cy;
                    bodyX = tipX + bodyLen; bodyY = cy; dirX = -1; dirY = 0; break;
                case EdgeDirection.Right:
                    tipX = Math.Min(cx, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - arrowSize); tipY = cy;
                    bodyX = tipX - bodyLen; bodyY = cy; dirX = 1; dirY = 0; break;
                default: return;
            }

            double px = -dirY, py = dirX;
            double w1x = bodyX + px * wingHalf, w1y = bodyY + py * wingHalf;
            double w2x = bodyX - px * wingHalf, w2y = bodyY - py * wingHalf;
            double tailX = bodyX - dirX * bodyLen * 0.5;
            double tailY = bodyY - dirY * bodyLen * 0.5;
            double notchDepth = arrowSize * 0.4;
            double n1x = w1x - dirX * notchDepth + px * notchDepth * 0.3;
            double n1y = w1y - dirY * notchDepth + py * notchDepth * 0.3;
            double n2x = w2x - dirX * notchDepth - px * notchDepth * 0.3;
            double n2y = w2y - dirY * notchDepth - py * notchDepth * 0.3;

            // Tip glow (multi-layer)
            var tipGlow = MakeRadialBrush(tipX, tipY, arrowSize * 0.5,
                (40, 0), (15, 0.3), (5, 0.6), (0, 1));
            dc.DrawEllipse(tipGlow, null, new Point(tipX, tipY), arrowSize * 0.5, arrowSize * 0.5);

            // Motion trail (3 faded ghosts)
            for (int t = 3; t >= 1; t--)
            {
                double trailOffset = t * bodyLen * 0.08;
                double ttx = tipX + dirX * trailOffset;
                double tty = tipY + dirY * trailOffset;
                double tw1x = ttx + px * wingHalf * (1 - t * 0.15), tw1y = tty + py * wingHalf * (1 - t * 0.15);
                double tw2x = ttx - px * wingHalf * (1 - t * 0.15), tw2y = tty - py * wingHalf * (1 - t * 0.15);
                double tTailX = ttx - dirX * bodyLen * 0.5, tTailY = tty - dirY * bodyLen * 0.5;

                var trailGeo = new StreamGeometry();
                using (var ctx = trailGeo.Open())
                {
                    ctx.BeginFigure(new Point(ttx, tty), true, true);
                    ctx.LineTo(new Point(tw1x, tw1y), true, false);
                    ctx.LineTo(new Point(tTailX, tTailY), true, false);
                    ctx.LineTo(new Point(tw2x, tw2y), true, false);
                }
                trailGeo.Freeze();
                dc.DrawGeometry(MakeBrush((byte)(30 / t)), null, trailGeo);
            }

            // Main arrow body
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

            var fillBrush = MakeBrush(210);
            var strokePen = MakePen(Color.FromArgb(240, 255, 255, 255), 3);
            dc.DrawGeometry(fillBrush, strokePen, geo);

            // Tip bright dot
            DrawDot(dc, tipX, tipY, 6, MakeBrush(255));
        }
    }
}
