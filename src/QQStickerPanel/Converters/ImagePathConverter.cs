using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using QQStickerPanel.Services;

namespace QQStickerPanel.Converters;

public sealed class ImagePathConverter : IValueConverter
{
    private const int MaxCachedImages = 512;
    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> CacheKeys = new();
    private static readonly object CacheLock = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string filePath || !File.Exists(filePath))
        {
            return null;
        }

        var width = GetDecodePixelWidth(parameter);
        var fileInfo = new FileInfo(filePath);
        var cacheKey = $"{fileInfo.FullName}|{width}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        if (Cache.TryGetValue(cacheKey, out var cachedSource))
        {
            return cachedSource;
        }

        var source = CreateImageSource(fileInfo.FullName, width);
        AddToCache(cacheKey, source);
        return source;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static BitmapSource? CreateImageSource(string filePath, int width)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = width;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception) when (IsImageLoadException())
        {
            return ShellThumbnailService.TryCreateThumbnail(filePath, width);
        }
    }

    private static void AddToCache(string cacheKey, BitmapSource? source)
    {
        lock (CacheLock)
        {
            if (!Cache.TryAdd(cacheKey, source))
            {
                return;
            }

            CacheKeys.Enqueue(cacheKey);
            while (CacheKeys.Count > MaxCachedImages && CacheKeys.TryDequeue(out var oldKey))
            {
                Cache.TryRemove(oldKey, out _);
            }
        }
    }

    private static int GetDecodePixelWidth(object parameter)
    {
        return parameter is string value && int.TryParse(value, out var width) && width > 0 ? width : 128;
    }

    private static bool IsImageLoadException() => true;
}
