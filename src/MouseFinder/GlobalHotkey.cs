using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MouseFinder;

/// <summary>
/// Registers and handles global hotkeys.
/// Shows a tray notification if registration fails (e.g. hotkey already in use).
/// </summary>
public class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    // Modifiers
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;

    // Keys
    private const uint VK_F = 0x46;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event EventHandler? OnHotkeyPressed;

    private HwndSource? _source;

    public void Register()
    {
        // Create a hidden message-only window to receive hotkey messages
        var hwndSourceParams = new HwndSourceParameters("MouseFinder_HotkeyReceiver")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0, // hidden
        };
        _source = new HwndSource(hwndSourceParams);
        _source.AddHook(WndProc);

        // Register Ctrl+Alt+F
        if (!RegisterHotKey(_source.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_F))
        {
            System.Diagnostics.Debug.WriteLine("Failed to register global hotkey Ctrl+Alt+F");
            // Notify the user so they know the hotkey won't work
            MessageBox.Show(
                "快捷键 Ctrl+Alt+F 注册失败，可能已被其他程序占用。\n" +
                "MouseFinder 仍可通过托盘图标操作。",
                "MouseFinder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            OnHotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source != null)
        {
            try { UnregisterHotKey(_source.Handle, HOTKEY_ID); }
            catch { /* HwndSource may already be disposed during WPF shutdown */ }
            try { _source.Dispose(); }
            catch { }
            _source = null;
        }
    }
}
