using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace QQStickerPanel;

public partial class App : Application
{
    private bool _isReportingFatalException;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        try
        {
            var startHidden = e.Args.Any(arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-silent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "/silent", StringComparison.OrdinalIgnoreCase));
            var mainWindow = new MainWindow(startHidden);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            ReportFatalException(ex);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ReportFatalException(e.Exception);
        Shutdown(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ReportFatalException(ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        ReportFatalException(e.Exception);
        Dispatcher.Invoke(() => Shutdown(1));
    }

    private void ReportFatalException(Exception exception)
    {
        if (_isReportingFatalException)
        {
            return;
        }

        _isReportingFatalException = true;
        var logPath = WriteCrashLog(exception);
        MessageBox.Show(
            $"程序启动或运行时发生错误，已写入日志：\n{logPath}",
            "QQ 表情包面板",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static string WriteCrashLog(Exception exception)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QQStickerPanel",
            "logs");
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, "crash.log");
        var content = new StringBuilder()
            .AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}]")
            .AppendLine(exception.ToString())
            .AppendLine()
            .ToString();
        File.AppendAllText(logPath, content);
        return logPath;
    }
}
