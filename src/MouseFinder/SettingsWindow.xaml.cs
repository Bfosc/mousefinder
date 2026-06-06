using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MouseFinder;

public partial class SettingsWindow : System.Windows.Window
{
    public event EventHandler<AppSettings>? SettingsChanged;
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadCurrentSettings();

        // Set window icon to match tray icon
        var icon = TrayIcon.CreateDefaultIcon();
        using var bmp = icon.ToBitmap();
        var hBitmap = bmp.GetHbitmap();
        Icon = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap, IntPtr.Zero, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        DeleteObject(hBitmap);
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private void LoadCurrentSettings()
    {
        // Mode
        ModeGlobal.IsChecked = _settings.Mode == FinderMode.Global;
        ModeEdge.IsChecked = _settings.Mode == FinderMode.EdgeOnly;
        UpdateTimeoutVisibility();

        // Style
        for (int i = 0; i < StyleCombo.Items.Count; i++)
        {
            if (StyleCombo.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == _settings.Style.ToString())
            {
                StyleCombo.SelectedIndex = i;
                break;
            }
        }
        if (StyleCombo.SelectedIndex < 0) StyleCombo.SelectedIndex = 0;

        // Timeout
        TimeoutSlider.Value = _settings.IdleTimeoutMs / 1000.0;
        TimeoutLabel.Text = $"{(int)TimeoutSlider.Value} 秒";

        // Edge Timeout
        EdgeTimeoutSlider.Value = _settings.EdgeTimeoutMs / 1000.0;
        EdgeTimeoutLabel.Text = $"{(int)EdgeTimeoutSlider.Value} 秒";

        // Size
        SizeSlider.Value = _settings.IndicatorSize;
        SizeLabel.Text = $"{(int)SizeSlider.Value} px";

        // Color - 设置常用颜色按钮状态
        switch (_settings.IndicatorColor.ToUpper())
        {
            case "#00BFFF": ColorCyan.IsChecked = true; break;
            case "#009944": ColorGreen.IsChecked = true; break;
            case "#FF3333": ColorRed.IsChecked = true; break;
            case "#FFD700": ColorYellow.IsChecked = true; break;
            case "#9933FF": ColorPurple.IsChecked = true; break;
            case "#FFFFFF": ColorWhite.IsChecked = true; break;
            default:
                // 自定义颜色，取消所有按钮选中
                ColorCyan.IsChecked = false;
                ColorGreen.IsChecked = false;
                ColorRed.IsChecked = false;
                ColorYellow.IsChecked = false;
                ColorPurple.IsChecked = false;
                ColorWhite.IsChecked = false;
                break;
        }

        // 设置自定义颜色滑块值
        LoadColorSliders();

        PulseCheckBox.IsChecked = _settings.PulseAnimation;
        StartupCheckBox.IsChecked = _settings.StartWithWindows;
    }

    private void LoadColorSliders()
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.IndicatorColor);
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            UpdateColorPreview();
        }
        catch
        {
            // 默认颜色
            RedSlider.Value = 0;
            GreenSlider.Value = 153;
            BlueSlider.Value = 68;
            UpdateColorPreview();
        }
    }

    private void UpdateColorPreview()
    {
        if (ColorPreview == null || ColorHexLabel == null) return;

        int r = (int)RedSlider.Value;
        int g = (int)GreenSlider.Value;
        int b = (int)BlueSlider.Value;

        string hex = $"#{r:X2}{g:X2}{b:X2}";
        ColorPreview.Fill = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb((byte)r, (byte)g, (byte)b));
        ColorHexLabel.Text = hex;
    }

    private void UpdateTimeoutVisibility()
    {
        if (TimeoutPanel != null)
            TimeoutPanel.Visibility = _settings.Mode == FinderMode.Global
                ? Visibility.Visible : Visibility.Collapsed;
        if (EdgeTimeoutPanel != null)
            EdgeTimeoutPanel.Visibility = _settings.Mode == FinderMode.EdgeOnly
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            _settings.Mode = tag == "EdgeOnly" ? FinderMode.EdgeOnly : FinderMode.Global;
            UpdateTimeoutVisibility();
        }
    }

    private void StyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StyleCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (Enum.TryParse<IndicatorStyle>(tag, out var style))
                _settings.Style = style;
        }
    }

    private void TimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TimeoutLabel != null)
            TimeoutLabel.Text = $"{(int)e.NewValue} 秒";
    }

    private void EdgeTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EdgeTimeoutLabel != null)
            EdgeTimeoutLabel.Text = $"{(int)e.NewValue} 秒";
    }

    private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ColorPreview == null || ColorHexLabel == null) return;

        int r = (int)RedSlider.Value;
        int g = (int)GreenSlider.Value;
        int b = (int)BlueSlider.Value;

        string hex = $"#{r:X2}{g:X2}{b:X2}";
        ColorPreview.Fill = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb((byte)r, (byte)g, (byte)b));
        ColorHexLabel.Text = hex;
        _settings.IndicatorColor = hex;

        // 取消常用颜色按钮选中状态
        ColorCyan.IsChecked = false;
        ColorGreen.IsChecked = false;
        ColorRed.IsChecked = false;
        ColorYellow.IsChecked = false;
        ColorPurple.IsChecked = false;
        ColorWhite.IsChecked = false;
    }

    private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SizeLabel != null)
            SizeLabel.Text = $"{(int)e.NewValue} px";
    }

    private void ColorRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string color)
        {
            _settings.IndicatorColor = color;
            LoadColorSliders();
        }
    }

    private void PulseCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _settings.PulseAnimation = PulseCheckBox.IsChecked ?? true;
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _settings.StartWithWindows = StartupCheckBox.IsChecked ?? false;
        SetAutoStart(_settings.StartWithWindows);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.IdleTimeoutMs = (int)(TimeoutSlider.Value * 1000);
        _settings.EdgeTimeoutMs = (int)(EdgeTimeoutSlider.Value * 1000);
        _settings.IndicatorSize = SizeSlider.Value;
        SettingsChanged?.Invoke(this, _settings);
        System.Windows.MessageBox.Show("设置已保存！", "MouseFinder",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (enable)
                key.SetValue("MouseFinder", $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue("MouseFinder", false);
        }
        catch (Exception ex)
        {
            AppSettings.LogError("Failed to set auto-start", ex);
            System.Windows.MessageBox.Show(
                $"设置开机启动失败: {ex.Message}",
                "MouseFinder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
