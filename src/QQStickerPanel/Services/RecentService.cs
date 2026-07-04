using System.IO;
using System.Text.Json;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class RecentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _recentPath;
    private int _limit;
    private List<RecentStickerRecord> _records = [];

    public RecentService(string dataDirectory, int limit)
    {
        _recentPath = Path.Combine(dataDirectory, "recent.json");
        _limit = limit;
        Load();
    }

    public void UpdateLimit(int limit)
    {
        _limit = Math.Max(1, limit);
        _records = _records.Take(_limit).ToList();
        Save();
    }

    public IReadOnlyList<StickerItem> GetRecentStickers(IEnumerable<StickerItem> stickers)
    {
        var stickerMap = stickers.ToDictionary(sticker => sticker.FilePath, StringComparer.OrdinalIgnoreCase);
        return _records
            .OrderByDescending(record => record.UsedAt)
            .Select(record => stickerMap.TryGetValue(record.FilePath, out var sticker) ? sticker : null)
            .Where(sticker => sticker is not null)
            .Cast<StickerItem>()
            .Take(_limit)
            .ToList();
    }

    public void RecordUse(StickerItem sticker)
    {
        _records.RemoveAll(record => string.Equals(record.FilePath, sticker.FilePath, StringComparison.OrdinalIgnoreCase));
        _records.Insert(0, new RecentStickerRecord
        {
            FilePath = sticker.FilePath,
            UsedAt = DateTimeOffset.Now
        });
        _records = _records.Take(_limit).ToList();
        Save();
    }

    public void Remove(StickerItem sticker)
    {
        if (_records.RemoveAll(record => string.Equals(record.FilePath, sticker.FilePath, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    public void UpdatePath(string oldPath, string newPath)
    {
        var record = _records.FirstOrDefault(item => string.Equals(item.FilePath, oldPath, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            return;
        }

        record.FilePath = newPath;
        Save();
    }

    public void UpdateDirectory(string oldDirectory, string newDirectory)
    {
        var changed = false;
        foreach (var record in _records.Where(record => IsInDirectory(record.FilePath, oldDirectory)))
        {
            var relativePath = Path.GetRelativePath(oldDirectory, record.FilePath);
            record.FilePath = Path.Combine(newDirectory, relativePath);
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    public int PruneMissingFiles()
    {
        var removedCount = _records.RemoveAll(record => !File.Exists(record.FilePath));
        if (removedCount > 0)
        {
            Save();
        }

        return removedCount;
    }

    private static bool IsInDirectory(string filePath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(directoryPath, filePath);
        return relativePath != "." && !relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath);
    }

    private void Load()
    {
        if (!File.Exists(_recentPath))
        {
            _records = [];
            return;
        }

        try
        {
            var json = File.ReadAllText(_recentPath);
            _records = JsonSerializer.Deserialize<List<RecentStickerRecord>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            _records = [];
        }
        catch (IOException)
        {
            _records = [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_recentPath)!);
        File.WriteAllText(_recentPath, JsonSerializer.Serialize(_records, JsonOptions));
    }

    private sealed class RecentStickerRecord
    {
        public required string FilePath { get; set; }
        public DateTimeOffset UsedAt { get; init; }
    }
}
