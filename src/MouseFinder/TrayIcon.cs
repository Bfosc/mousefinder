using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseFinder;

/// <summary>
/// System tray icon with context menu
/// </summary>
public class TrayIcon : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public event EventHandler? OnShowSettings;
    public event EventHandler? OnTogglePause;
    public event EventHandler? OnExit;

    private readonly NotifyIcon _notifyIcon;
    private ToolStripMenuItem? _pauseMenuItem;

    public TrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "MouseFinder - 鼠标寻找器",
            Icon = CreateDefaultIcon(),
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => OnShowSettings?.Invoke(this, EventArgs.Empty);

        var contextMenu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("设置(&S)");
        settingsItem.Click += (_, _) => OnShowSettings?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(settingsItem);

        _pauseMenuItem = new ToolStripMenuItem("暂停(&P)");
        _pauseMenuItem.Click += (_, _) => OnTogglePause?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(_pauseMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出(&X)");
        exitItem.Click += (_, _) => OnExit?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public void UpdatePauseState(bool isPaused)
    {
        if (_pauseMenuItem != null)
        {
            _pauseMenuItem.Text = isPaused ? "继续(&P)" : "暂停(&P)";
            _notifyIcon.Text = isPaused ? "MouseFinder - 已暂停" : "MouseFinder - 鼠标寻找器";
        }
    }

    public static Icon CreateDefaultIcon()
    {
        // Create a simple mouse cursor icon programmatically
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Arrow shape points
            var points = new[]
            {
                new PointF(2, 1), new PointF(2, 13), new PointF(5, 10),
                new PointF(8, 14), new PointF(10, 13), new PointF(7, 9),
                new PointF(11, 9)
            };

            // Draw black border (thicker)
            using var borderPen = new Pen(Color.Black, 2);
            g.DrawPolygon(borderPen, points);

            // Fill with sky blue
            using var brush = new SolidBrush(Color.FromArgb(0, 191, 255));
            g.FillPolygon(brush, points);
        }

        // Convert Bitmap to Icon using GetHicon
        IntPtr hicon = bmp.GetHicon();
        Icon icon = Icon.FromHandle(hicon);
        // Clone to own the handle, then destroy original
        Icon result = (Icon)icon.Clone();
        DestroyIcon(hicon);
        return result;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }
}
