using System;
using System.IO;
using System.Text.Json;

namespace MouseFinder;

public enum FinderMode
{
    Global,
    EdgeOnly
}

public enum IndicatorStyle
{
    MinimalPulse,     // 极简脉冲
    GlassOrb,         // 玻璃光球
    NeonRing,         // 霓虹光环
    ParticleField,    // 粒子汇聚
    Aurora,           // 极光
    FocusSpot,        // 聚焦光斑
    MagneticDot,      // 磁力点
    EdgeArrow         // 边缘箭头
}

public class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MouseFinder");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");
    private static readonly string LogPath = Path.Combine(SettingsDir, "error.log");

    public int IdleTimeoutMs { get; set; } = 3000;
    public int EdgeTimeoutMs { get; set; } = 5000;
    public FinderMode Mode { get; set; } = FinderMode.Global;
    public IndicatorStyle Style { get; set; } = IndicatorStyle.MinimalPulse;
    public string IndicatorColor { get; set; } = "#009944";
    public double IndicatorSize { get; set; } = 60.0;
    public bool PulseAnimation { get; set; } = true;
    public bool IsPaused { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to load settings", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            LogError("Failed to save settings", ex);
        }
    }

    /// <summary>
    /// Append an error entry to the log file with timestamp.
    /// Silently fails if logging itself errors (never crash the app for logging).
    /// </summary>
    public static void LogError(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            if (ex != null)
                entry += $"\n  {ex.GetType().Name}: {ex.Message}\n  {ex.StackTrace}";
            entry += "\n";
            File.AppendAllText(LogPath, entry);
        }
        catch
        {
            // Logging must never throw
        }
    }
}
