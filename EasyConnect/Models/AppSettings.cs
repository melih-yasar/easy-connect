namespace EasyConnect.Models;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool StartWithWindows { get; set; }
    public bool CompactMode { get; set; }
    public bool CompactConnectedOnly { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool ShowConnectionPopup { get; set; } = true;
    public bool ShowDisconnectPopup { get; set; }
    public bool LowBatteryNotifications { get; set; } = true;
    public bool AutomaticRefresh { get; set; } = true;
    public int PopupDurationSeconds { get; set; } = 5;
    public int RefreshIntervalSeconds { get; set; } = 20;
    public int LowBatteryThreshold { get; set; } = 20;
}
