using System;
using System.Threading;
using System.Windows;

namespace MouseFinder;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Global_MouseFinder_SingleInstance";
    private static Mutex? _mutex;

    private TrayIcon? _trayIcon;
    private MouseTracker? _mouseTracker;
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private GlobalHotkey? _globalHotkey;
    private AppSettings _settings = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Multi-instance protection ────────────────────────────────
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("MouseFinder 已在运行中。", "MouseFinder",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // ── Initialize with error handling ───────────────────────────
        try
        {
            _settings = AppSettings.Load();

            // Create overlay window (hidden by default)
            _overlayWindow = new OverlayWindow(_settings);

            // Create mouse tracker
            _mouseTracker = new MouseTracker(_settings.IdleTimeoutMs, _settings.EdgeTimeoutMs);
            _mouseTracker.UpdateMode(_settings.Mode);
            _mouseTracker.OnShowIndicator += (_, data) => _overlayWindow?.ShowIndicator(data.pos, data.direction);
            _mouseTracker.OnHideIndicator += (_, _) => _overlayWindow?.HideIndicator();
            _mouseTracker.Start();

            // Create global hotkey (Ctrl+Alt+F to toggle)
            _globalHotkey = new GlobalHotkey();
            _globalHotkey.OnHotkeyPressed += (_, _) =>
            {
                if (_overlayWindow?.IsIndicatorVisible == true)
                    _overlayWindow.HideIndicator();
                else
                    _overlayWindow?.ForceShow();
            };
            _globalHotkey.Register();

            // Create tray icon
            _trayIcon = new TrayIcon();
            _trayIcon.OnShowSettings += (_, _) => ShowSettings();
            _trayIcon.OnTogglePause += (_, _) =>
            {
                _settings.IsPaused = !_settings.IsPaused;
                _mouseTracker.IsPaused = _settings.IsPaused;
                _trayIcon.UpdatePauseState(_settings.IsPaused);
                if (_settings.IsPaused)
                    _overlayWindow?.HideIndicator();
            };
            _trayIcon.OnExit += (_, _) => Shutdown();
            _trayIcon.Show();
        }
        catch (Exception ex)
        {
            AppSettings.LogError("Fatal error during startup", ex);
            MessageBox.Show(
                $"MouseFinder 启动失败:\n{ex.Message}\n\n详情已写入日志: %AppData%/MouseFinder/error.log",
                "MouseFinder - 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_settings);
            _settingsWindow.SettingsChanged += (_, newSettings) =>
            {
                _mouseTracker?.UpdateTimeout(newSettings.IdleTimeoutMs);
                _mouseTracker?.UpdateEdgeTimeout(newSettings.EdgeTimeoutMs);
                _mouseTracker?.UpdateMode(newSettings.Mode);
                _overlayWindow?.UpdateSettings(newSettings);
                newSettings.Save();
            };
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _globalHotkey?.Dispose();
        _mouseTracker?.Stop();
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
