namespace QQStickerPanel.Models;

public sealed class AppSettings
{
    public string StickerRoot { get; set; } = string.Empty;
    public List<string> SupportedExtensions { get; set; } = [];
    public List<string> CategoryOrder { get; set; } = [];
    public int RecentLimit { get; set; } = 100;
    public QQSettings QQ { get; set; } = new();
    public DockSettings Dock { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();
}

public sealed class QQSettings
{
    public List<string> ProcessNames { get; set; } = ["QQ.exe", "QQNT.exe"];
    public bool SendAfterPaste { get; set; }
    public QQSendShortcut SendShortcut { get; set; } = QQSendShortcut.Enter;
    public bool HideWhenQQNotRunning { get; set; } = true;
    public bool HideWhenQQNotForeground { get; set; } = true;
    public bool ShowWhenQQForeground { get; set; } = true;
    public QQWindowMatchSettings WindowMatch { get; set; } = new();
}

public sealed class QQWindowMatchSettings
{
    public int MinScore { get; set; } = 70;
    public List<string> TitleExcludeKeywords { get; set; } =
    [
        "图片",
        "图片查看",
        "图片预览",
        "小程序",
        "文件预览",
        "设置",
        "收藏",
        "音视频通话"
    ];
    public List<string> ClassExcludeKeywords { get; set; } = [];
    public bool EnableMatchDiagnostics { get; set; }
}

public enum QQSendShortcut
{
    Enter,
    CtrlEnter
}

public sealed class StartupSettings
{
    public bool Enabled { get; set; }
}

public sealed class DockSettings
{
    public int Gap { get; set; } = 8;
    public int Width { get; set; } = 320;
    public DockSide Side { get; set; } = DockSide.Right;
    public int FollowTimerIntervalMs { get; set; } = 33;
    public bool TopmostWhenQQForeground { get; set; } = true;
    public bool FreeDockEnabled { get; set; }
    public double? FreeLeft { get; set; }
    public double? FreeTop { get; set; }
    public double FreeWidth { get; set; } = 320;
    public double FreeHeight { get; set; } = 640;
}

public enum DockSide
{
    Left,
    Right,
    Top,
    Bottom,
    InnerLeft,
    InnerRight
}
