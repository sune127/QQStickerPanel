using System.Windows;
using System.Windows.Controls;
using QQStickerPanel.Models;

namespace QQStickerPanel;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        StickerRootBox.Text = settings.StickerRoot;
        ExtensionsBox.Text = string.Join(", ", settings.SupportedExtensions);
        ProcessNamesBox.Text = string.Join(", ", settings.QQ.ProcessNames);
        RecentLimitBox.Text = settings.RecentLimit.ToString();
        DockSideBox.SelectedValue = settings.Dock.Side.ToString();
        DockWidthBox.Text = settings.Dock.Width.ToString();
        DockGapBox.Text = settings.Dock.Gap.ToString();
        FollowIntervalBox.Text = settings.Dock.FollowTimerIntervalMs.ToString();
        SendAfterPasteBox.IsChecked = settings.QQ.SendAfterPaste;
        SendShortcutBox.SelectedValue = settings.QQ.SendShortcut.ToString();
        HideWhenQQNotRunningBox.IsChecked = settings.QQ.HideWhenQQNotRunning;
        HideWhenQQNotForegroundBox.IsChecked = settings.QQ.HideWhenQQNotForeground;
        ShowWhenQQForegroundBox.IsChecked = settings.QQ.ShowWhenQQForeground;
        WindowMatchScoreBox.Text = settings.QQ.WindowMatch.MinScore.ToString();
        MatchDiagnosticsBox.IsChecked = settings.QQ.WindowMatch.EnableMatchDiagnostics;
        FreeDockBox.IsChecked = settings.Dock.FreeDockEnabled;
        TopmostBox.IsChecked = settings.Dock.TopmostWhenQQForeground;
        StartupBox.IsChecked = settings.Startup.Enabled;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var stickerRoot = StickerRootBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(stickerRoot))
        {
            MessageBox.Show(this, "表情目录不能为空。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(RecentLimitBox.Text, out var recentLimit) || recentLimit <= 0)
        {
            MessageBox.Show(this, "最近数量必须是正整数。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DockGapBox.Text, out var dockGap) || dockGap < 0)
        {
            MessageBox.Show(this, "吸附间距不能小于 0。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DockWidthBox.Text, out var dockWidth) || dockWidth is < 240 or > 640)
        {
            MessageBox.Show(this, "面板宽度必须在 240 到 640 之间。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(FollowIntervalBox.Text, out var followInterval) || followInterval is < 16 or > 500)
        {
            MessageBox.Show(this, "跟随间隔必须在 16 到 500 毫秒之间。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(WindowMatchScoreBox.Text, out var matchScore) || matchScore is < 1 or > 200)
        {
            MessageBox.Show(this, "匹配分必须在 1 到 200 之间。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DockSideBox.SelectedValue is not string dockSideValue || !Enum.TryParse<DockSide>(dockSideValue, out var dockSide))
        {
            MessageBox.Show(this, "请选择吸附位置。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SendShortcutBox.SelectedValue is not string sendShortcutValue || !Enum.TryParse<QQSendShortcut>(sendShortcutValue, out var sendShortcut))
        {
            MessageBox.Show(this, "请选择发送快捷键。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var extensions = SplitValues(ExtensionsBox.Text)
            .Select(extension => extension.StartsWith('.') ? extension : $".{extension}")
            .ToList();
        if (extensions.Count == 0)
        {
            MessageBox.Show(this, "至少保留一个图片扩展名。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var processNames = SplitValues(ProcessNamesBox.Text).ToList();
        if (processNames.Count == 0)
        {
            MessageBox.Show(this, "至少保留一个 QQ 进程名。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.StickerRoot = stickerRoot;
        _settings.SupportedExtensions = extensions;
        _settings.QQ.ProcessNames = processNames;
        _settings.QQ.SendAfterPaste = SendAfterPasteBox.IsChecked == true;
        _settings.QQ.SendShortcut = sendShortcut;
        _settings.QQ.HideWhenQQNotRunning = HideWhenQQNotRunningBox.IsChecked == true;
        _settings.QQ.HideWhenQQNotForeground = HideWhenQQNotForegroundBox.IsChecked == true;
        _settings.QQ.ShowWhenQQForeground = ShowWhenQQForegroundBox.IsChecked == true;
        _settings.QQ.WindowMatch.MinScore = matchScore;
        _settings.QQ.WindowMatch.EnableMatchDiagnostics = MatchDiagnosticsBox.IsChecked == true;
        _settings.RecentLimit = recentLimit;
        _settings.Dock.Side = dockSide;
        _settings.Dock.Width = dockWidth;
        _settings.Dock.Gap = dockGap;
        _settings.Dock.FollowTimerIntervalMs = followInterval;
        _settings.Dock.FreeDockEnabled = FreeDockBox.IsChecked == true;
        _settings.Dock.TopmostWhenQQForeground = TopmostBox.IsChecked == true;
        _settings.Startup.Enabled = StartupBox.IsChecked == true;
        DialogResult = true;
    }

    private static IEnumerable<string> SplitValues(string text)
    {
        return text.Split([',', '，', ';', '；', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
