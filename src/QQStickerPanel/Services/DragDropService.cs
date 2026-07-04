using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class DragDropService
{
    private const string InternalStickerDragFormat = "QQStickerPanel.StickerDrag";
    private const string FileGroupDescriptorWFormat = "FileGroupDescriptorW";
    private const string FileGroupDescriptorFormat = "FileGroupDescriptor";
    private const string FileContentsFormat = "FileContents";
    private static readonly string[] BitmapFormats = [DataFormats.Bitmap, "PNG", "image/png", "DeviceIndependentBitmap", DataFormats.Dib];
    private static readonly Regex HtmlPathRegex = new("(?:src|href)\\s*=\\s*[\"'](?<value>[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly AppSettings _settings;

    public DragDropService(AppSettings settings)
    {
        _settings = settings;
    }

    public DataObject CreateStickerDataObject(StickerItem sticker)
    {
        return CreateStickerDataObject([sticker]);
    }

    public DataObject CreateStickerDataObject(IEnumerable<StickerItem> stickers)
    {
        var dataObject = new DataObject();
        var fileList = new StringCollection();
        foreach (var sticker in stickers)
        {
            fileList.Add(sticker.FilePath);
        }

        dataObject.SetFileDropList(fileList);
        dataObject.SetData(InternalStickerDragFormat, true);
        return dataObject;
    }

    public bool HasInternalStickerFiles(IDataObject dataObject)
    {
        return GetInternalStickerFiles(dataObject).Count > 0;
    }

    public IReadOnlyList<string> GetInternalStickerFiles(IDataObject dataObject)
    {
        if (!dataObject.GetDataPresent(InternalStickerDragFormat) || !dataObject.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }

        return ExpandFileDropPaths(GetFileDropPaths(dataObject))
            .Where(IsSupportedFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool HasSupportedFiles(IDataObject dataObject)
    {
        return GetSupportedFiles(dataObject).Count > 0;
    }

    public IReadOnlyList<string> GetSupportedFiles(IDataObject dataObject)
    {
        if (dataObject.GetDataPresent(InternalStickerDragFormat) || !dataObject.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }

        return ExpandFileDropPaths(GetFileDropPaths(dataObject))
            .Where(IsSupportedFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool HasSupportedVirtualFiles(IDataObject dataObject)
    {
        return GetSupportedVirtualFiles(dataObject).Count > 0;
    }

    public IReadOnlyList<string> GetSupportedVirtualFileNames(IDataObject dataObject)
    {
        return GetSupportedVirtualFiles(dataObject)
            .Select(descriptor => descriptor.FileName)
            .ToList();
    }

    public bool HasSupportedTextFiles(IDataObject dataObject)
    {
        return GetSupportedTextFiles(dataObject).Count > 0;
    }

    public IReadOnlyList<string> GetSupportedTextFiles(IDataObject dataObject)
    {
        if (dataObject.GetDataPresent(InternalStickerDragFormat))
        {
            return [];
        }

        return ExpandFileDropPaths(EnumerateTextFileCandidates(dataObject))
            .Where(IsSupportedFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ImportResult ImportVirtualFiles(IDataObject dataObject, string targetDirectory, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash)
    {
        return ImportVirtualFiles(dataObject, targetDirectory, existingStickers, getHash, true);
    }

    public ImportResult ImportVirtualFiles(IDataObject dataObject, string targetDirectory, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash, bool deduplicate)
    {
        Directory.CreateDirectory(targetDirectory);

        var descriptors = GetSupportedVirtualFiles(dataObject);
        if (descriptors.Count == 0)
        {
            return new ImportResult([], 0);
        }

        var existingHashes = deduplicate
            ? existingStickers
                .Where(sticker => File.Exists(sticker.FilePath))
                .Select(sticker => getHash(sticker.FilePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var importedFiles = new List<string>();
        var skippedDuplicateCount = 0;
        foreach (var descriptor in descriptors)
        {
            if (!TryGetFileContentStream(dataObject, descriptor.Index, out var contentStream))
            {
                continue;
            }

            using (contentStream)
            {
                var targetPath = CreateAvailablePath(targetDirectory, descriptor.FileName);
                using (var targetStream = File.Create(targetPath))
                {
                    contentStream.CopyTo(targetStream);
                }

                if (deduplicate)
                {
                    var hash = ComputeHash(targetPath);
                    if (!existingHashes.Add(hash))
                    {
                        File.Delete(targetPath);
                        skippedDuplicateCount++;
                        continue;
                    }
                }

                importedFiles.Add(targetPath);
            }
        }

        return new ImportResult(importedFiles, skippedDuplicateCount);
    }

    public int CountDuplicateVirtualFiles(IDataObject dataObject, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash)
    {
        var existingHashes = existingStickers
            .Where(sticker => File.Exists(sticker.FilePath))
            .Select(sticker => getHash(sticker.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;
        foreach (var descriptor in GetSupportedVirtualFiles(dataObject))
        {
            if (!TryGetFileContentStream(dataObject, descriptor.Index, out var contentStream))
            {
                continue;
            }

            using (contentStream)
            {
                var hash = ComputeHash(contentStream);
                if (!existingHashes.Add(hash))
                {
                    duplicateCount++;
                }
            }
        }

        return duplicateCount;
    }

    public bool HasSupportedImageData(IDataObject dataObject)
    {
        return TryGetBitmapSource(dataObject, out _);
    }

    public bool TryGetBitmapSource(IDataObject dataObject, out BitmapSource bitmapSource)
    {
        bitmapSource = null!;
        foreach (var format in BitmapFormats)
        {
            if (!dataObject.GetDataPresent(format))
            {
                continue;
            }

            if (TryCreateBitmapSource(dataObject.GetData(format), out bitmapSource))
            {
                return true;
            }
        }

        return false;
    }

    public ImportResult ImportFiles(IEnumerable<string> sourceFiles, string targetDirectory, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash)
    {
        return ImportFiles(sourceFiles, targetDirectory, existingStickers, getHash, true);
    }

    public ImportResult ImportFiles(IEnumerable<string> sourceFiles, string targetDirectory, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash, bool deduplicate)
    {
        Directory.CreateDirectory(targetDirectory);

        var existingHashes = deduplicate
            ? existingStickers
                .Where(sticker => File.Exists(sticker.FilePath))
                .Select(sticker => getHash(sticker.FilePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var importedFiles = new List<string>();
        var skippedDuplicateCount = 0;
        foreach (var sourceFile in ExpandFileDropPaths(sourceFiles).Where(IsSupportedFile))
        {
            if (deduplicate)
            {
                var sourceHash = getHash(sourceFile);
                if (!existingHashes.Add(sourceHash))
                {
                    skippedDuplicateCount++;
                    continue;
                }
            }

            var targetPath = CreateAvailablePath(targetDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, targetPath);
            importedFiles.Add(targetPath);
        }

        return new ImportResult(importedFiles, skippedDuplicateCount);
    }

    public int CountDuplicateFiles(IEnumerable<string> sourceFiles, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash)
    {
        var existingHashes = existingStickers
            .Where(sticker => File.Exists(sticker.FilePath))
            .Select(sticker => getHash(sticker.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;
        foreach (var sourceFile in ExpandFileDropPaths(sourceFiles).Where(IsSupportedFile))
        {
            if (!existingHashes.Add(getHash(sourceFile)))
            {
                duplicateCount++;
            }
        }

        return duplicateCount;
    }

    public ImportResult ImportClipboardImage(BitmapSource image, string targetDirectory, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash)
    {
        return ImportClipboardImage(image, targetDirectory, existingStickers, getHash, true);
    }

    public ImportResult ImportClipboardImage(BitmapSource image, string targetDirectory, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash, bool deduplicate)
    {
        Directory.CreateDirectory(targetDirectory);

        var targetPath = CreateAvailablePath(targetDirectory, $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        using (var stream = File.Create(targetPath))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
        }

        if (!deduplicate)
        {
            return new ImportResult([targetPath], 0);
        }

        var imageHash = ComputeHash(targetPath);
        var isDuplicate = existingStickers
            .Where(sticker => File.Exists(sticker.FilePath))
            .Select(sticker => getHash(sticker.FilePath))
            .Any(hash => string.Equals(hash, imageHash, StringComparison.OrdinalIgnoreCase));
        if (!isDuplicate)
        {
            return new ImportResult([targetPath], 0);
        }

        File.Delete(targetPath);
        return new ImportResult([], 1);
    }

    public int CountDuplicateClipboardImage(BitmapSource image, IEnumerable<StickerItem> existingStickers, Func<string, string> getHash)
    {
        var imageHash = ComputeHash(image);
        return existingStickers
            .Where(sticker => File.Exists(sticker.FilePath))
            .Select(sticker => getHash(sticker.FilePath))
            .Any(hash => string.Equals(hash, imageHash, StringComparison.OrdinalIgnoreCase))
            ? 1
            : 0;
    }

    private IEnumerable<string> ExpandFileDropPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                yield return path;
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(IsSupportedFile))
            {
                yield return filePath;
            }
        }
    }

    private IEnumerable<string> EnumerateTextFileCandidates(IDataObject dataObject)
    {
        if (TryGetText(dataObject, DataFormats.Html, out var html))
        {
            foreach (var candidate in HtmlPathRegex.Matches(html).Select(match => match.Groups["value"].Value))
            {
                if (TryNormalizeLocalPath(candidate, out var path))
                {
                    yield return path;
                }
            }
        }

        foreach (var format in new[] { DataFormats.Text, DataFormats.UnicodeText, "UniformResourceLocator", "FileName", "FileNameW" })
        {
            if (!TryGetText(dataObject, format, out var text))
            {
                continue;
            }

            foreach (var candidate in SplitTextCandidates(text))
            {
                if (TryNormalizeLocalPath(candidate, out var path))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> SplitTextCandidates(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(candidate => candidate.Trim().Trim('"', '\'', '<', '>'));
    }

    private static bool TryGetText(IDataObject dataObject, string format, out string text)
    {
        text = string.Empty;
        try
        {
            if (!dataObject.GetDataPresent(format))
            {
                return false;
            }

            switch (dataObject.GetData(format))
            {
                case string value:
                    text = value;
                    return !string.IsNullOrWhiteSpace(text);
                case MemoryStream stream:
                    text = DecodeTextStream(stream);
                    return !string.IsNullOrWhiteSpace(text);
                case Stream stream:
                    text = DecodeTextStream(stream);
                    return !string.IsNullOrWhiteSpace(text);
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static string DecodeTextStream(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static bool TryNormalizeLocalPath(string candidate, out string path)
    {
        path = string.Empty;
        candidate = WebUtility.HtmlDecode(candidate).Trim();
        if (candidate.Length == 0)
        {
            return false;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
            {
                return false;
            }

            path = uri.LocalPath;
            return true;
        }

        path = candidate;
        return true;
    }

    private static IReadOnlyList<string> GetFileDropPaths(IDataObject dataObject)
    {
        return dataObject.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];
    }

    private IReadOnlyList<VirtualFileDescriptor> GetSupportedVirtualFiles(IDataObject dataObject)
    {
        if (!dataObject.GetDataPresent(FileContentsFormat))
        {
            return [];
        }

        var descriptors = TryGetVirtualFiles(dataObject, FileGroupDescriptorWFormat, Encoding.Unicode)
            ?? TryGetVirtualFiles(dataObject, FileGroupDescriptorFormat, Encoding.Default)
            ?? [];
        return descriptors
            .Where(descriptor => IsSupportedFileName(descriptor.FileName))
            .DistinctBy(descriptor => descriptor.Index)
            .ToList();
    }

    private static IReadOnlyList<VirtualFileDescriptor>? TryGetVirtualFiles(IDataObject dataObject, string descriptorFormat, Encoding encoding)
    {
        if (!dataObject.GetDataPresent(descriptorFormat) || dataObject.GetData(descriptorFormat) is not MemoryStream stream)
        {
            return null;
        }

        var bytes = stream.ToArray();
        if (bytes.Length < sizeof(uint))
        {
            return [];
        }

        var count = BitConverter.ToUInt32(bytes, 0);
        var offset = sizeof(uint);
        var descriptors = new List<VirtualFileDescriptor>();
        for (var index = 0; index < count && offset < bytes.Length; index++)
        {
            const int descriptorSize = 592;
            const int fileNameOffset = 76;
            const int maxFileNameBytes = 520;
            if (offset + descriptorSize > bytes.Length)
            {
                break;
            }

            var rawFileName = encoding.GetString(bytes, offset + fileNameOffset, maxFileNameBytes);
            var nullIndex = rawFileName.IndexOf('\0');
            var fileName = (nullIndex >= 0 ? rawFileName[..nullIndex] : rawFileName).Trim();
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                descriptors.Add(new VirtualFileDescriptor(index, Path.GetFileName(fileName)));
            }

            offset += descriptorSize;
        }

        return descriptors;
    }

    private static bool TryGetFileContentStream(IDataObject dataObject, int index, out Stream contentStream)
    {
        contentStream = null!;
        try
        {
            var content = dataObject.GetData(FileContentsFormat, true);
            switch (content)
            {
                case Stream stream:
                    contentStream = CopyToMemoryStream(stream);
                    return true;
                case MemoryStream[] streams when index < streams.Length:
                    contentStream = CopyToMemoryStream(streams[index]);
                    return true;
                case Stream[] streams when index < streams.Length:
                    contentStream = CopyToMemoryStream(streams[index]);
                    return true;
                case object[] objects when index < objects.Length && objects[index] is Stream stream:
                    contentStream = CopyToMemoryStream(stream);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private static MemoryStream CopyToMemoryStream(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    private bool IsSupportedFile(string filePath)
    {
        return File.Exists(filePath) && _settings.SupportedExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);
    }

    private bool IsSupportedFileName(string fileName)
    {
        return _settings.SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryCreateBitmapSource(object? data, out BitmapSource bitmapSource)
    {
        bitmapSource = null!;
        switch (data)
        {
            case BitmapSource source:
                bitmapSource = source;
                return true;
            case Stream stream:
                return TryDecodeBitmapStream(stream, out bitmapSource);
            default:
                return false;
        }
    }

    private static bool TryDecodeBitmapStream(Stream stream, out BitmapSource bitmapSource)
    {
        bitmapSource = null!;
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            bitmapSource = decoder.Frames[0];
            bitmapSource.Freeze();
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or COMException or ArgumentException)
        {
            return false;
        }
    }

    private static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ComputeHash(stream);
    }

    private static string ComputeHash(BitmapSource image)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        stream.Position = 0;
        return ComputeHash(stream);
    }

    private static string ComputeHash(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var hash = SHA256.HashData(stream);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Convert.ToHexString(hash);
    }

    private static string CreateAvailablePath(string targetDirectory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var targetPath = Path.Combine(targetDirectory, fileName);
        var suffix = 1;

        while (File.Exists(targetPath))
        {
            targetPath = Path.Combine(targetDirectory, $"{baseName}_{suffix}{extension}");
            suffix++;
        }

        return targetPath;
    }
}

public sealed record ImportResult(IReadOnlyList<string> ImportedFiles, int SkippedDuplicateCount);

internal sealed record VirtualFileDescriptor(int Index, string FileName);
