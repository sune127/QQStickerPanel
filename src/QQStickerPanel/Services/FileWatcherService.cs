using System.IO;
using System.Windows.Threading;

namespace QQStickerPanel.Services;

public sealed class FileWatcherService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _onChanged;
    private readonly DispatcherTimer _debounceTimer;
    private FileSystemWatcher? _watcher;

    public FileWatcherService(Dispatcher dispatcher, Action onChanged)
    {
        _dispatcher = dispatcher;
        _onChanged = onChanged;
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            _onChanged();
        };
    }

    public void Start(string rootDirectory)
    {
        Stop();
        Directory.CreateDirectory(rootDirectory);

        _watcher = new FileSystemWatcher(rootDirectory)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
    }

    public void Stop()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileChanged;
        _watcher.Deleted -= OnFileChanged;
        _watcher.Changed -= OnFileChanged;
        _watcher.Renamed -= OnFileChanged;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        Stop();
        _debounceTimer.Stop();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }
}
