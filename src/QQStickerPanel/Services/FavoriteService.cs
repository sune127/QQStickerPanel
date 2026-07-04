using System.IO;
using System.Text.Json;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class FavoriteService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _favoritesPath;
    private HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);

    public FavoriteService(string dataDirectory)
    {
        _favoritesPath = Path.Combine(dataDirectory, "favorites.json");
        Load();
    }

    public IReadOnlyList<StickerItem> GetFavoriteStickers(IEnumerable<StickerItem> stickers)
    {
        return stickers
            .Where(sticker => _favoritePaths.Contains(sticker.FilePath))
            .ToList();
    }

    public bool IsFavorite(StickerItem sticker)
    {
        return _favoritePaths.Contains(sticker.FilePath);
    }

    public bool ToggleFavorite(StickerItem sticker)
    {
        var isFavorite = !_favoritePaths.Remove(sticker.FilePath);
        if (isFavorite)
        {
            _favoritePaths.Add(sticker.FilePath);
        }

        Save();
        return isFavorite;
    }

    public void Add(StickerItem sticker)
    {
        if (_favoritePaths.Add(sticker.FilePath))
        {
            Save();
        }
    }

    public void AddRange(IEnumerable<StickerItem> stickers)
    {
        var changed = false;
        foreach (var sticker in stickers)
        {
            changed |= _favoritePaths.Add(sticker.FilePath);
        }

        if (changed)
        {
            Save();
        }
    }

    public void Remove(StickerItem sticker)
    {
        if (_favoritePaths.Remove(sticker.FilePath))
        {
            Save();
        }
    }

    public void RemoveRange(IEnumerable<StickerItem> stickers)
    {
        var changed = false;
        foreach (var sticker in stickers)
        {
            changed |= _favoritePaths.Remove(sticker.FilePath);
        }

        if (changed)
        {
            Save();
        }
    }

    public void UpdatePath(string oldPath, string newPath)
    {
        if (!_favoritePaths.Remove(oldPath))
        {
            return;
        }

        _favoritePaths.Add(newPath);
        Save();
    }

    public void UpdateDirectory(string oldDirectory, string newDirectory)
    {
        var changedPaths = _favoritePaths
            .Where(path => IsInDirectory(path, oldDirectory))
            .ToList();
        if (changedPaths.Count == 0)
        {
            return;
        }

        foreach (var oldPath in changedPaths)
        {
            _favoritePaths.Remove(oldPath);
            var relativePath = Path.GetRelativePath(oldDirectory, oldPath);
            _favoritePaths.Add(Path.Combine(newDirectory, relativePath));
        }

        Save();
    }

    public int PruneMissingFiles()
    {
        var missingPaths = _favoritePaths
            .Where(path => !File.Exists(path))
            .ToList();
        foreach (var path in missingPaths)
        {
            _favoritePaths.Remove(path);
        }

        if (missingPaths.Count > 0)
        {
            Save();
        }

        return missingPaths.Count;
    }

    private void Load()
    {
        if (!File.Exists(_favoritesPath))
        {
            _favoritePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = File.ReadAllText(_favoritesPath);
            var favoritePaths = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
            _favoritePaths = favoritePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            _favoritePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            _favoritePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_favoritesPath)!);
        var favoritePaths = _favoritePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        File.WriteAllText(_favoritesPath, JsonSerializer.Serialize(favoritePaths, JsonOptions));
    }

    private static bool IsInDirectory(string filePath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(directoryPath, filePath);
        return relativePath != "." && !relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath);
    }
}
