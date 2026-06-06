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

            if (_settings.Style == IndicatorStyle.EdgeArrow && _edgeDirection != EdgeDirection.None)
            {
                DrawEdgeArrow(dc);
                return;
            }

            switch (_settings.Style)
            {
                case IndicatorStyle.MinimalPulse: DrawMinimalPulse(dc); break;
                case IndicatorStyle.GlassOrb: DrawGlassOrb(dc); break;
                case IndicatorStyle.NeonRing: DrawNeonRing(dc); break;
                case IndicatorStyle.ParticleField: DrawParticleField(dc); break;
                case IndicatorStyle.Aurora: DrawAurora(dc); break;
                case IndicatorStyle.FocusSpot: DrawFocusSpot(dc); break;
                case IndicatorStyle.MagneticDot: DrawMagneticDot(dc); break;
                case IndicatorStyle.EdgeArrow: DrawMinimalPulse(dc); break;
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

        // ══════════════════════════════════════════════════════════════
        //  Style 1: Minimal Pulse (极简脉冲)
        //  Clean, modern, minimal. Soft breathing ring with subtle glow.
        // ══════════════════════════════════════════════════════════════

        private void DrawMinimalPulse(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 0.6;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.08 * Math.Sin(_phase * 2) : 1.0;
            double radius = r * pulse;

            // Soft outer glow (very subtle)
            var glowBrush = MakeRadialBrush(cx, cy, radius * 1.5,
                (20, 0), (10, 0.3), (3, 0.6), (0, 1));
            dc.DrawEllipse(glowBrush, null, new Point(cx, cy), radius * 1.5, radius * 1.5);

            // Main ring (thin, elegant)
            DrawEllipse(dc, cx, cy, radius, MakePen(180, 1.5));

            // Inner subtle fill
            var innerFill = MakeRadialBrush(cx, cy, radius,
                (15, 0), (5, 0.5), (0, 1));
            dc.DrawEllipse(innerFill, null, new Point(cx, cy), radius, radius);

            // Center dot (small, precise)
            double dotR = 2.5 * pulse;
            DrawDot(dc, cx, cy, dotR, MakeBrush(220));
        }

        // ══════════════════════════════════════════════════════════════
        //  Style 2: Glass Orb (玻璃光球)
        //  Frosted glass effect with layered transparency.
        //  Premium, modern UI feel.
        // ══════════════════════════════════════════════════════════════

        private void DrawGlassOrb(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 0.8;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.06 * Math.Sin(_phase * 2) : 1.0;
            double radius = r * pulse;

            // Outer diffuse glow
            var outerGlow = MakeRadialBrush(cx, cy, radius * 2,
                (25, 0), (12, 0.2), (5, 0.4), (1, 0.7), (0, 1));
            dc.DrawEllipse(outerGlow, null, new Point(cx, cy), radius * 2, radius * 2);

            // Glass body (layered transparency)
            var glassBody = MakeRadialBrush(cx, cy, radius,
                (40, 0), (25, 0.3), (15, 0.5), (8, 0.7), (2, 0.9), (0, 1));
            dc.DrawEllipse(glassBody, null, new Point(cx, cy), radius, radius);

            // Highlight (top-left reflection)
            double highlightOffset = radius * 0.25;
            var highlight = MakeRadialBrush(cx - highlightOffset, cy - highlightOffset, radius * 0.4,
                (50, 0), (20, 0.3), (0, 1));
            dc.DrawEllipse(highlight, null, new Point(cx - highlightOffset, cy - highlightOffset),
                radius * 0.4, radius * 0.4);

            // Subtle ring
            DrawEllipse(dc, cx, cy, radius * 0.95, MakePen(30, 1));

            // Center bright point
            double dotR = 3 * pulse;
            DrawDot(dc, cx, cy, dotR, MakeBrush(200));
        }

        // ══════════════════════════════════════════════════════════════
        //  Style 3: Neon Ring (霓虹光环)
        //  Vibrant neon glow with multiple layers.
        //  Cyberpunk aesthetic, high contrast.
        // ══════════════════════════════════════════════════════════════

        private void DrawNeonRing(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 0.7;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.1 * Math.Sin(_phase * 2.5) : 1.0;
            double radius = r * pulse;

            // Outer neon glow (multiple soft layers)
            for (int i = 4; i >= 1; i--)
            {
                double glowR = radius + i * 10;
                byte alpha = (byte)(12 / i);
                DrawEllipse(dc, cx, cy, glowR, MakePen(alpha, 8));
            }

            // Main neon ring (bright)
            DrawEllipse(dc, cx, cy, radius, MakePen(220, 2.5));

            // Inner neon ring (dimmer)
            DrawEllipse(dc, cx, cy, radius * 0.7, MakePen(120, 1.5));

            // Rotating bright spots
            int spotCount = 4;
            for (int i = 0; i < spotCount; i++)
            {
                double angle = _phase * 1.5 + Math.PI * 2 * i / spotCount;
                double sx = cx + radius * Math.Cos(angle);
                double sy = cy + radius * Math.Sin(angle);
                double spotR = 3 * pulse;
                DrawDot(dc, sx, sy, spotR, MakeBrush(255));
            }

            // Center dot with glow
            double dotR = 4 * pulse;
            var dotGlow = MakeRadialBrush(cx, cy, dotR * 3,
                (60, 0), (20, 0.4), (0, 1));
            dc.DrawEllipse(dotGlow, null, new Point(cx, cy), dotR * 3, dotR * 3);
            DrawDot(dc, cx, cy, dotR, MakeBrush(255));
        }

        // ══════════════════════════════════════════════════════════════
        //  Style 4: Particle Field (粒子汇聚)
        //  Small particles orbiting and converging on cursor.
        //  Dynamic, organic feel.
        // ══════════════════════════════════════════════════════════════

        private void DrawParticleField(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 1.2;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.05 * Math.Sin(_phase * 2) : 1.0;

            // Outer particle ring
            int particleCount = 12;
            for (int i = 0; i < particleCount; i++)
            {
                double angle = _phase * 0.8 + Math.PI * 2 * i / particleCount;
                double orbitR = r * (0.8 + 0.2 * Math.Sin(_phase * 2 + i));
                double px = cx + orbitR * Math.Cos(angle);
                double py = cy + orbitR * Math.Sin(angle);

                // Particle with trail
                double particleR = 2 * pulse;
                byte alpha = (byte)(180 - i * 10);
                DrawDot(dc, px, py, particleR, MakeBrush(alpha));

                // Trail line to center
                double trailLen = r * 0.3;
                double trailAngle = Math.Atan2(cy - py, cx - px);
                DrawLine(dc, px, py,
                    px + trailLen * Math.Cos(trailAngle),
                    py + trailLen * Math.Sin(trailAngle),
                    MakePen((byte)(alpha / 3), 1));
            }

            // Inner particle ring (slower, smaller)
            int innerCount = 8;
            for (int i = 0; i < innerCount; i++)
            {
                double angle = -_phase * 0.5 + Math.PI * 2 * i / innerCount;
                double orbitR = r * 0.4;
                double px = cx + orbitR * Math.Cos(angle);
                double py = cy + orbitR * Math.Sin(angle);
                DrawDot(dc, px, py, 1.5 * pulse, MakeBrush(120));
            }

            // Center soft glow
            var centerGlow = MakeRadialBrush(cx, cy, r * 0.3,
                (30, 0), (10, 0.4), (0, 1));
            dc.DrawEllipse(centerGlow, null, new Point(cx, cy), r * 0.3, r * 0.3);

            // Center dot
            DrawDot(dc, cx, cy, 3 * pulse, MakeBrush(200));
        }

        // ══════════════════════════════════════════════════════════════
        //  Style 5: Aurora (极光)
        //  Flowing, organic gradients like northern lights.
        //  Ethereal, mesmerizing.
        // ══════════════════════════════════════════════════════════════

        private void DrawAurora(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 1.5;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.04 * Math.Sin(_phase * 1.5) : 1.0;

            // Multiple aurora layers (flowing gradients)
            for (int layer = 0; layer < 3; layer++)
            {
                double layerOffset = _phase * 0.3 + layer * 0.5;
                double layerR = r * (0.6 + layer * 0.2) * pulse;

                // Create flowing shape using multiple ellipses
                int segmentCount = 8;
                for (int i = 0; i < segmentCount; i++)
                {
                    double t = (double)i / segmentCount;
                    double angle = layerOffset + Math.PI * 2 * t;
                    double wobble = 0.2 * Math.Sin(_phase * 2 + t * Math.PI * 4 + layer);
                    double segR = layerR * (1 + wobble);

                    double sx = cx + segR * Math.Cos(angle);
                    double sy = cy + segR * Math.Sin(angle);

                    byte alpha = (byte)(15 - layer * 3);
                    double dotR = (8 - layer * 2) * pulse;
                    DrawDot(dc, sx, sy, dotR, MakeBrush(alpha));
                }
            }

            // Central aurora glow
            var auroraGlow = MakeRadialBrush(cx, cy, r * 0.8,
                (20, 0), (10, 0.3), (4, 0.5), (1, 0.7), (0, 1));
            dc.DrawEllipse(auroraGlow, null, new Point(cx, cy), r * 0.8, r * 0.8);

            // Subtle ring
            DrawEllipse(dc, cx, cy, r * 0.5, MakePen(15, 1));

            // Center dot
            double dotPulse = _settings.PulseAnimation ? 1.0 + 0.15 * Math.Sin(_phase * 3) : 1.0;
            DrawDot(dc, cx, cy, 3 * dotPulse, MakeBrush(180));
        }

        // ══════════════════════════════════════════════════════════════
        //  Style 6: Focus Spot (聚焦光斑)
        //  Theater spotlight effect. Dramatic, clear.
        //  Directs attention with high contrast.
        // ══════════════════════════════════════════════════════════════

        private void DrawFocusSpot(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 1.0;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.06 * Math.Sin(_phase * 2) : 1.0;
            double radius = r * pulse;

            // Outer shadow/vignette
            var vignette = MakeRadialBrush(cx, cy, radius * 1.8,
                (0, 0), (0, 0.5), (5, 0.7), (15, 0.85), (25, 1));
            dc.DrawEllipse(vignette, null, new Point(cx, cy), radius * 1.8, radius * 1.8);

            // Main spotlight (bright center, sharp falloff)
            var spotlight = MakeRadialBrush(cx, cy, radius,
                (80, 0), (50, 0.2), (25, 0.4), (8, 0.6), (2, 0.8), (0, 1));
            dc.DrawEllipse(spotlight, null, new Point(cx, cy), radius, radius);

            // Bright core
            var core = MakeRadialBrush(cx, cy, radius * 0.3,
                (120, 0), (40, 0.4), (0, 1));
            dc.DrawEllipse(core, null, new Point(cx, cy), radius * 0.3, radius * 0.3);

            // Crosshair (thin, elegant)
            double crossLen = radius * 0.6;
            double gap = radius * 0.08;
            var crossPen = MakePen(100, 0.8);
            DrawLine(dc, cx, cy - gap - crossLen * 0.3, cx, cy - gap, crossPen);
            DrawLine(dc, cx, cy + gap, cx, cy + gap + crossLen * 0.3, crossPen);
            DrawLine(dc, cx - gap - crossLen * 0.3, cy, cx - gap, cy, crossPen);
            DrawLine(dc, cx + gap, cy, cx + gap + crossLen * 0.3, cy, crossPen);

            // Center dot
            DrawDot(dc, cx, cy, 2, MakeBrush(200));
        }

        // ══════════════════════════════════════════════════════════════
        //  Style 7: Magnetic Dot (磁力点)
        //  Smooth, physics-based. Dot follows cursor with spring motion.
        //  Minimal, Apple-like aesthetic.
        // ══════════════════════════════════════════════════════════════

        private void DrawMagneticDot(DrawingContext dc)
        {
            double cx = _pos.X, cy = _pos.Y;
            double r = _settings!.IndicatorSize * 0.4;
            double pulse = _settings.PulseAnimation ? 1.0 + 0.12 * Math.Sin(_phase * 3) : 1.0;
            double radius = r * pulse;

            // Outer magnetic field (soft gradient)
            var field = MakeRadialBrush(cx, cy, radius * 4,
                (10, 0), (5, 0.3), (2, 0.5), (0, 0.8), (0, 1));
            dc.DrawEllipse(field, null, new Point(cx, cy), radius * 4, radius * 4);

            // Orbiting dots (magnetic field lines)
            int orbitCount = 3;
            for (int orbit = 0; orbit < orbitCount; orbit++)
            {
                double orbitR = radius * (2 + orbit * 0.8);
                int dotCount = 4 + orbit * 2;
                double speed = 1.0 + orbit * 0.3;

                for (int i = 0; i < dotCount; i++)
                {
                    double angle = _phase * speed + Math.PI * 2 * i / dotCount + orbit * 0.5;
                    double dx = cx + orbitR * Math.Cos(angle);
                    double dy = cy + orbitR * Math.Sin(angle);
                    double dotR = 1.5 - orbit * 0.3;
                    byte alpha = (byte)(100 - orbit * 25);
                    DrawDot(dc, dx, dy, dotR, MakeBrush(alpha));
                }
            }

            // Inner glow
            var innerGlow = MakeRadialBrush(cx, cy, radius * 2,
                (25, 0), (10, 0.3), (3, 0.6), (0, 1));
            dc.DrawEllipse(innerGlow, null, new Point(cx, cy), radius * 2, radius * 2);

            // Main dot (bright, clean)
            DrawDot(dc, cx, cy, radius, MakeBrush(220));

            // Bright center
            DrawDot(dc, cx, cy, radius * 0.5, MakeBrush(255));
        }

        // ── Edge Arrow (边缘箭头) ──────────────────────────────────

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

            var glowBrush = MakeBrush(40);
            dc.DrawEllipse(glowBrush, null, new Point(tipX, tipY), arrowSize * 0.3, arrowSize * 0.3);

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

            var fillBrush = MakeBrush(200);
            var strokePen = MakePen(Color.FromArgb(255, 255, 255, 255), 3);
            dc.DrawGeometry(fillBrush, strokePen, geo);

            DrawDot(dc, tipX, tipY, 6, MakeBrush(255));
        }
    }
}
