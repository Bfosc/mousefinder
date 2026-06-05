using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace MouseFinder;

/// <summary>
/// Edge direction for edge mode indicator
/// </summary>
public enum EdgeDirection
{
    Top,
    Bottom,
    Left,
    Right,
    None
}

/// <summary>
/// Tracks mouse position. Supports Global (idle timeout) and EdgeOnly (mouse leaves screen) modes.
///
/// Uses per-monitor DPI via MonitorFromPoint + GetDpiForMonitor for correct
/// coordinate conversion on mixed-DPI multi-monitor setups.
/// </summary>
public class MouseTracker
{
    // ── Win32 ────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // ── Events ───────────────────────────────────────────────────────

    /// <summary>Fires when the indicator should show. Point = mouse position, EdgeDirection = which edge (for edge mode).</summary>
    public event EventHandler<(Point pos, EdgeDirection direction)>? OnShowIndicator;

    /// <summary>Fires when the indicator should hide.</summary>
    public event EventHandler? OnHideIndicator;

    public bool IsPaused { get; set; }

    // ── State ────────────────────────────────────────────────────────

    private readonly DispatcherTimer _timer;
    private Point _lastPosition;
    private DateTime _lastMoveTime;
    private bool _isShowing;
    private int _idleTimeoutMs;
    private int _edgeTimeoutMs;
    private FinderMode _mode = FinderMode.Global;

    // Screen bounds (updated on display change)
    private double _screenLeft, _screenTop, _screenRight, _screenBottom;

    // Edge mode: dwell time before showing indicator (prevents false triggers)
    private DateTime _edgeEnterTime;
    private bool _edgeInside; // true while cursor is in the edge zone

    private const double EdgeMargin = 8;           // pixels — generous enough to avoid false triggers
    private const double EdgeDwellMs = 5000;        // ms cursor must stay in edge zone before triggering

    // ── Constructor ──────────────────────────────────────────────────

    public MouseTracker(int idleTimeoutMs, int edgeTimeoutMs = 5000)
    {
        _idleTimeoutMs = idleTimeoutMs;
        _edgeTimeoutMs = edgeTimeoutMs;
        _lastMoveTime = DateTime.Now;
        _lastPosition = GetMousePosition();

        UpdateScreenBounds();

        // Re-cache screen bounds when monitors change
        SystemEvents.DisplaySettingsChanged += (_, _) => UpdateScreenBounds();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += Timer_Tick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    public void UpdateTimeout(int timeoutMs) => _idleTimeoutMs = timeoutMs;
    public void UpdateEdgeTimeout(int timeoutMs) => _edgeTimeoutMs = timeoutMs;
    public void UpdateMode(FinderMode mode) => _mode = mode;

    // ── Screen bounds ────────────────────────────────────────────────

    private void UpdateScreenBounds()
    {
        _screenLeft = SystemParameters.VirtualScreenLeft;
        _screenTop = SystemParameters.VirtualScreenTop;
        _screenRight = _screenLeft + SystemParameters.VirtualScreenWidth;
        _screenBottom = _screenTop + SystemParameters.VirtualScreenHeight;
    }

    // ── Timer tick ───────────────────────────────────────────────────

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (IsPaused) return;

        var pos = GetMousePosition();

        if (_mode == FinderMode.Global)
            HandleGlobalMode(pos);
        else
            HandleEdgeMode(pos);

        _lastPosition = pos;
    }

    // ── Global mode ──────────────────────────────────────────────────

    private void HandleGlobalMode(Point pos)
    {
        if (pos != _lastPosition)
        {
            _lastMoveTime = DateTime.Now;
            if (_isShowing)
            {
                _isShowing = false;
                OnHideIndicator?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            var idleMs = (DateTime.Now - _lastMoveTime).TotalMilliseconds;
            if (idleMs >= _idleTimeoutMs && !_isShowing)
            {
                _isShowing = true;
                OnShowIndicator?.Invoke(this, (pos, EdgeDirection.None));
            }
        }
    }

    // ── Edge mode ────────────────────────────────────────────────────

    private void HandleEdgeMode(Point pos)
    {
        bool inEdgeZone = pos.X <= _screenLeft + EdgeMargin
                       || pos.X >= _screenRight - EdgeMargin
                       || pos.Y <= _screenTop + EdgeMargin
                       || pos.Y >= _screenBottom - EdgeMargin;

        if (inEdgeZone)
        {
            if (!_edgeInside)
            {
                // Just entered the edge zone — start dwell timer
                _edgeInside = true;
                _edgeEnterTime = DateTime.Now;
            }
            else if (!_isShowing && (DateTime.Now - _edgeEnterTime).TotalMilliseconds >= _edgeTimeoutMs)
            {
                // Dwelled long enough — show indicator
                _isShowing = true;
                double ix = Math.Max(_screenLeft, Math.Min(pos.X, _screenRight - 1));
                double iy = Math.Max(_screenTop, Math.Min(pos.Y, _screenBottom - 1));

                // Determine edge direction
                EdgeDirection direction = GetEdgeDirection(pos);

                OnShowIndicator?.Invoke(this, (new Point(ix, iy), direction));
            }
        }
        else
        {
            _edgeInside = false;
            if (_isShowing)
            {
                _isShowing = false;
                OnHideIndicator?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Determine which edge the mouse is near
    /// </summary>
    private EdgeDirection GetEdgeDirection(Point pos)
    {
        // Check which edge is closest
        double distToLeft = pos.X - _screenLeft;
        double distToRight = _screenRight - pos.X;
        double distToTop = pos.Y - _screenTop;
        double distToBottom = _screenBottom - pos.Y;

        double minDist = Math.Min(Math.Min(distToLeft, distToRight), Math.Min(distToTop, distToBottom));

        if (minDist == distToTop) return EdgeDirection.Top;
        if (minDist == distToBottom) return EdgeDirection.Bottom;
        if (minDist == distToLeft) return EdgeDirection.Left;
        return EdgeDirection.Right;
    }

    // ── Mouse position (per-monitor DPI) ─────────────────────────────

    /// <summary>
    /// Get mouse position in WPF logical coordinates.
    /// Uses per-monitor DPI for correct conversion on mixed-DPI setups.
    /// </summary>
    private static Point GetMousePosition()
    {
        GetCursorPos(out var pt);
        double scale = GetScaleForPoint(pt);
        return new Point(pt.X / scale, pt.Y / scale);
    }

    /// <summary>
    /// Get the DPI scale factor for the monitor containing the given point.
    /// Falls back to system DPI if per-monitor API is unavailable.
    /// </summary>
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
        catch
        {
            // shcore.dll not available (very old Windows) — fall through to system DPI
        }

        // Fallback: system-wide DPI
        return GetDpiForSystem() / 96.0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
