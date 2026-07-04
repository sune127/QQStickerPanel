using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class StickerManagementService
{
    private readonly AppSettings _settings;

    public StickerManagementService(AppSettings settings)
    {
        _settings = settings;
    }

    public void OpenStickerFolder(StickerItem sticker)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{sticker.FilePath}\"",
            UseShellExecute = true
        });
    }

    public void DeleteSticker(StickerItem sticker)
    {
        FileSystem.DeleteFile(sticker.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    public string CreateCategory(string name)
    {
        var categoryPath = SanitizeCategoryPath(name);
        if (string.IsNullOrWhiteSpace(categoryPath))
        {
            throw new InvalidOperationException("分类名不能为空");
        }

        var directoryPath = Path.Combine(_settings.StickerRoot, categoryPath);
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    public string RenameSticker(StickerItem sticker, string newName)
    {
        var sanitizedName = SanitizeFileName(newName);
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            throw new InvalidOperationException("文件名不能为空");
        }

        var extension = Path.GetExtension(sanitizedName);
        if (string.IsNullOrEmpty(extension))
        {
            sanitizedName += sticker.Extension;
        }

        var targetDirectory = Path.GetDirectoryName(sticker.FilePath) ?? _settings.StickerRoot;
        var targetPath = CreateAvailablePath(targetDirectory, sanitizedName, sticker.FilePath);
        File.Move(sticker.FilePath, targetPath);
        return targetPath;
    }

    public string MoveStickerToCategory(StickerItem sticker, StickerCategory category)
    {
        var targetDirectory = GetCategoryDirectory(category);
        Directory.CreateDirectory(targetDirectory);

        var targetPath = CreateAvailablePath(targetDirectory, sticker.FileName, sticker.FilePath);
        if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(sticker.FilePath), StringComparison.OrdinalIgnoreCase))
        {
            return targetPath;
        }

        File.Move(sticker.FilePath, targetPath);
        return targetPath;
    }

    public string MoveFileToCategory(string sourcePath, StickerCategory category)
    {
        var targetDirectory = GetCategoryDirectory(category);
        Directory.CreateDirectory(targetDirectory);

        var targetPath = CreateAvailablePath(targetDirectory, Path.GetFileName(sourcePath), sourcePath);
        if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            return targetPath;
        }

        File.Move(sourcePath, targetPath);
        return targetPath;
    }

    public void OpenCategoryFolder(StickerCategory category)
    {
        var directoryPath = GetCategoryDirectory(category);
        Directory.CreateDirectory(directoryPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath,
            UseShellExecute = true
        });
    }

    public string RenameCategory(StickerCategory category, string newName)
    {
        if (category.Kind != StickerCategoryKind.Directory || category.DirectoryPath is null)
        {
            throw new InvalidOperationException("只能重命名目录分类");
        }

        var sanitizedName = SanitizeCategoryPath(newName);
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            throw new InvalidOperationException("分类名不能为空");
        }

        var targetPath = Path.Combine(_settings.StickerRoot, sanitizedName);
        if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(category.DirectoryPath), StringComparison.OrdinalIgnoreCase))
        {
            return targetPath;
        }

        if (Directory.Exists(targetPath))
        {
            throw new InvalidOperationException("同名分类已存在");
        }

        Directory.Move(category.DirectoryPath, targetPath);
        return targetPath;
    }

    public void DeleteEmptyCategory(StickerCategory category)
    {
        if (category.Kind != StickerCategoryKind.Directory || category.DirectoryPath is null)
        {
            throw new InvalidOperationException("只能删除目录分类");
        }

        if (Directory.EnumerateFileSystemEntries(category.DirectoryPath).Any())
        {
            throw new InvalidOperationException("分类不是空目录，不能删除");
        }

        Directory.Delete(category.DirectoryPath);
    }

    private string GetCategoryDirectory(StickerCategory category)
    {
        return category.Kind == StickerCategoryKind.Directory && category.DirectoryPath is not null
            ? category.DirectoryPath
            : _settings.StickerRoot;
    }

    private static string SanitizeCategoryName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Trim().Where(character => !invalidChars.Contains(character)).ToArray());
        return sanitized.Trim();
    }

    private static string SanitizeCategoryPath(string path)
    {
        return string.Join(Path.DirectorySeparatorChar, path
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeCategoryName)
            .Where(segment => segment.Length > 0));
    }

    private static string SanitizeFileName(string name)
    {
        return SanitizeCategoryName(name);
    }

    private static string CreateAvailablePath(string targetDirectory, string fileName, string sourcePath)
    {
        var targetPath = Path.Combine(targetDirectory, fileName);
        if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            return targetPath;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 1;
        while (File.Exists(targetPath))
        {
            targetPath = Path.Combine(targetDirectory, $"{baseName}_{suffix}{extension}");
            suffix++;
        }

        return targetPath;
    }
}
