using Microsoft.Win32;

namespace QQStickerPanel.Services;

public sealed class StartupService
{
    private const string AppName = "QQStickerPanel";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(AppName) is string value
            && (string.Equals(value, CreateStartupCommand(silent: true), StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, CreateStartupCommand(silent: false), StringComparison.OrdinalIgnoreCase));
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enabled)
        {
            key.SetValue(AppName, CreateStartupCommand(silent: true), RegistryValueKind.String);
            return;
        }

        key.DeleteValue(AppName, false);
    }

    private static string CreateStartupCommand(bool silent)
    {
        var executablePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        return silent ? $"\"{executablePath}\" --silent" : $"\"{executablePath}\"";
    }
}
