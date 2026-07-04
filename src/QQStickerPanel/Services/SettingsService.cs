using System.IO;
using System.Text.Json;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QQStickerPanel");

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public AppSettings Load()
    {
        Directory.CreateDirectory(DataDirectory);

        if (!File.Exists(SettingsPath))
        {
            var defaultSettings = CreateDefaultSettings();
            Save(defaultSettings);
            return defaultSettings;
        }

        var json = File.ReadAllText(SettingsPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefaultSettings();
        EnsureDefaults(settings);
        Directory.CreateDirectory(settings.StickerRoot);
        Save(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(settings.StickerRoot);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static AppSettings CreateDefaultSettings()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var stickerRoot = Path.Combine(pictures, "QQStickers");

        return new AppSettings
        {
            StickerRoot = stickerRoot,
            SupportedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".avif"],
            CategoryOrder = [],
            RecentLimit = 100,
            QQ = new QQSettings
            {
                ProcessNames = ["QQ.exe", "QQNT.exe"],
                HideWhenQQNotRunning = true,
                HideWhenQQNotForeground = true,
                ShowWhenQQForeground = true,
                WindowMatch = new QQWindowMatchSettings()
            },
            Startup = new StartupSettings(),
            Dock = new DockSettings
            {
                Gap = 8,
                Width = 320,
                Side = DockSide.Right,
                FollowTimerIntervalMs = 33,
                TopmostWhenQQForeground = true,
                FreeDockEnabled = false,
                FreeWidth = 320,
                FreeHeight = 640
            }
        };
    }

    private static void EnsureDefaults(AppSettings settings)
    {
        settings.SupportedExtensions ??= [];
        settings.CategoryOrder ??= [];
        settings.QQ ??= new QQSettings();
        settings.QQ.ProcessNames ??= [];
        settings.QQ.WindowMatch ??= new QQWindowMatchSettings();
        settings.QQ.WindowMatch.TitleExcludeKeywords ??= [];
        settings.QQ.WindowMatch.ClassExcludeKeywords ??= [];
        settings.Dock ??= new DockSettings();
        settings.Startup ??= new StartupSettings();

        if (string.IsNullOrWhiteSpace(settings.StickerRoot))
        {
            settings.StickerRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "QQStickers");
        }

        if (settings.SupportedExtensions.Count == 0)
        {
            settings.SupportedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".avif"];
        }

        if (settings.RecentLimit <= 0)
        {
            settings.RecentLimit = 100;
        }

        if (settings.QQ.ProcessNames.Count == 0)
        {
            settings.QQ.ProcessNames = ["QQ.exe", "QQNT.exe"];
        }

        if (settings.QQ.WindowMatch.MinScore <= 0)
        {
            settings.QQ.WindowMatch.MinScore = 70;
        }

        if (settings.QQ.WindowMatch.TitleExcludeKeywords.Count == 0)
        {
            settings.QQ.WindowMatch.TitleExcludeKeywords =
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
        }

        if (settings.Dock.Width <= 0)
        {
            settings.Dock.Width = 320;
        }

        if (settings.Dock.FollowTimerIntervalMs <= 0)
        {
            settings.Dock.FollowTimerIntervalMs = 33;
        }

        if (!double.IsFinite(settings.Dock.FreeWidth) || settings.Dock.FreeWidth <= 0)
        {
            settings.Dock.FreeWidth = 320;
        }

        if (!double.IsFinite(settings.Dock.FreeHeight) || settings.Dock.FreeHeight <= 0)
        {
            settings.Dock.FreeHeight = 640;
        }

        if (settings.Dock.FreeLeft.HasValue && !double.IsFinite(settings.Dock.FreeLeft.Value))
        {
            settings.Dock.FreeLeft = null;
        }

        if (settings.Dock.FreeTop.HasValue && !double.IsFinite(settings.Dock.FreeTop.Value))
        {
            settings.Dock.FreeTop = null;
        }
    }
}
