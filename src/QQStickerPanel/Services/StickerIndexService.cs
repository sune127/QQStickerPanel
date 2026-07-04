using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class StickerIndexService
{
    private readonly AppSettings _settings;
    private readonly string _databasePath;

    public StickerIndexService(AppSettings settings, string dataDirectory)
    {
        _settings = settings;
        Directory.CreateDirectory(dataDirectory);
        _databasePath = Path.Combine(dataDirectory, "sticker-index.db");
        Initialize();
    }

    public StickerLibrarySnapshot LoadCachedSnapshot()
    {
        try
        {
            using var connection = OpenConnection();
            var rootPath = GetRootPath();
            var stickers = ReadStickerRecords(connection, rootPath)
                .Where(record => IsSupportedExtension(record.Extension))
                .Select(CreateStickerItem)
                .OrderByDescending(sticker => sticker.LastWriteTime)
                .ThenBy(sticker => sticker.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var categories = ReadCategoryRecords(connection, rootPath, stickers);
            return CreateSnapshot(stickers, categories);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException)
        {
            ResetDatabase();
            return CreateSnapshot([], []);
        }
    }

    public StickerLibrarySnapshot RefreshSnapshotIncrementally()
    {
        try
        {
            return RefreshSnapshotIncrementallyCore();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException)
        {
            ResetDatabase();
            Initialize();
            return RefreshSnapshotIncrementallyCore();
        }
    }

    private StickerLibrarySnapshot RefreshSnapshotIncrementallyCore()
    {
        Directory.CreateDirectory(_settings.StickerRoot);

        using var connection = OpenConnection();
        var rootPath = GetRootPath();
        var cachedRecords = ReadStickerRecords(connection, rootPath)
            .ToDictionary(record => record.FilePath, StringComparer.OrdinalIgnoreCase);
        var diskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stickers = new List<StickerItem>();
        var categoryRecords = EnumerateCategoryRecords(rootPath).ToList();

        using var transaction = connection.BeginTransaction();
        foreach (var fileInfo in EnumerateSupportedFiles(rootPath))
        {
            diskPaths.Add(fileInfo.FullName);

            var category = CreateCategoryRecord(rootPath, fileInfo.DirectoryName);
            var extension = fileInfo.Extension;
            var lastWriteUtc = fileInfo.LastWriteTimeUtc.ToString("O");
            var isUnchanged = cachedRecords.TryGetValue(fileInfo.FullName, out var cached)
                && cached.FileSize == fileInfo.Length
                && string.Equals(cached.LastWriteUtc, lastWriteUtc, StringComparison.Ordinal)
                && string.Equals(cached.CategoryKey, category?.CategoryKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cached.Extension, extension, StringComparison.OrdinalIgnoreCase);
            var isAnimated = isUnchanged ? cached!.IsAnimated : IsAnimatedGif(fileInfo.FullName);

            var record = new StickerIndexRecord(
                rootPath,
                fileInfo.FullName,
                fileInfo.Name,
                category?.CategoryKey,
                category?.CategoryName ?? "未分类",
                extension,
                fileInfo.Length,
                lastWriteUtc,
                isAnimated);

            if (!isUnchanged)
            {
                UpsertStickerRecord(connection, transaction, record);
            }

            stickers.Add(CreateStickerItem(record));
        }

        foreach (var stalePath in cachedRecords.Keys.Where(path => !diskPaths.Contains(path)).ToList())
        {
            DeleteStickerRecord(connection, transaction, rootPath, stalePath);
        }

        ReplaceCategoryRecords(connection, transaction, rootPath, categoryRecords);
        transaction.Commit();

        return CreateSnapshot(
            stickers
                .OrderByDescending(sticker => sticker.LastWriteTime)
                .ThenBy(sticker => sticker.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            categoryRecords);
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS sticker_index (
                root_path TEXT NOT NULL,
                file_path TEXT NOT NULL,
                file_name TEXT NOT NULL,
                category_key TEXT NULL,
                category_name TEXT NOT NULL,
                extension TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                last_write_utc TEXT NOT NULL,
                is_animated INTEGER NOT NULL DEFAULT 0,
                indexed_at_utc TEXT NOT NULL,
                PRIMARY KEY (root_path, file_path)
            );

            CREATE INDEX IF NOT EXISTS idx_sticker_index_category_key ON sticker_index(root_path, category_key);
            CREATE INDEX IF NOT EXISTS idx_sticker_index_last_write ON sticker_index(root_path, last_write_utc);

            CREATE TABLE IF NOT EXISTS category_index (
                root_path TEXT NOT NULL,
                category_key TEXT NOT NULL,
                category_name TEXT NOT NULL,
                directory_path TEXT NOT NULL,
                indexed_at_utc TEXT NOT NULL,
                PRIMARY KEY (root_path, category_key)
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        }.ToString());
        connection.Open();
        return connection;
    }

    private string GetRootPath()
    {
        return Path.GetFullPath(_settings.StickerRoot);
    }

    private IReadOnlyList<StickerIndexRecord> ReadStickerRecords(SqliteConnection connection, string rootPath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT root_path, file_path, file_name, category_key, category_name, extension, file_size, last_write_utc, is_animated
            FROM sticker_index
            WHERE root_path = $rootPath
            ORDER BY last_write_utc DESC, file_name COLLATE NOCASE
            """;
        command.Parameters.AddWithValue("$rootPath", rootPath);

        var records = new List<StickerIndexRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new StickerIndexRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetString(7),
                reader.GetInt32(8) != 0));
        }

        return records;
    }

    private IReadOnlyList<CategoryIndexRecord> ReadCategoryRecords(SqliteConnection connection, string rootPath, IReadOnlyList<StickerItem> stickers)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT root_path, category_key, category_name, directory_path
            FROM category_index
            WHERE root_path = $rootPath
            ORDER BY category_name COLLATE NOCASE
            """;
        command.Parameters.AddWithValue("$rootPath", rootPath);

        var records = new List<CategoryIndexRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new CategoryIndexRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        if (records.Count > 0)
        {
            return records;
        }

        return stickers
            .Where(sticker => !string.IsNullOrEmpty(sticker.CategoryKey) && sticker.CategoryKey is not null)
            .GroupBy(sticker => sticker.CategoryKey!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CategoryIndexRecord(rootPath, group.Key, group.First().CategoryName, group.Key))
            .ToList();
    }

    private IEnumerable<CategoryIndexRecord> EnumerateCategoryRecords(string rootPath)
    {
        foreach (var directoryPath in EnumerateDirectoriesSafe(rootPath).OrderBy(GetCategoryName, StringComparer.CurrentCultureIgnoreCase))
        {
            yield return new CategoryIndexRecord(rootPath, directoryPath, GetCategoryName(directoryPath), directoryPath);
        }
    }

    private IEnumerable<FileInfo> EnumerateSupportedFiles(string rootPath)
    {
        foreach (var directoryPath in new[] { rootPath }.Concat(EnumerateDirectoriesSafe(rootPath)))
        {
            foreach (var filePath in EnumerateFilesSafe(directoryPath).Where(IsSupportedFile))
            {
                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(filePath);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                yield return fileInfo;
            }
        }
    }

    private IEnumerable<string> EnumerateDirectoriesSafe(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                yield return directory;
                pending.Push(directory);
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private CategoryIndexRecord? CreateCategoryRecord(string rootPath, string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)
            || string.Equals(Path.GetFullPath(directoryPath), rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        return new CategoryIndexRecord(rootPath, fullDirectoryPath, GetCategoryName(fullDirectoryPath), fullDirectoryPath);
    }

    private string GetCategoryName(string directory)
    {
        return Path.GetRelativePath(_settings.StickerRoot, directory).Replace(Path.DirectorySeparatorChar, '/');
    }

    private bool IsSupportedFile(string filePath)
    {
        return IsSupportedExtension(Path.GetExtension(filePath));
    }

    private bool IsSupportedExtension(string extension)
    {
        return _settings.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static StickerItem CreateStickerItem(StickerIndexRecord record)
    {
        return new StickerItem
        {
            Id = record.FilePath,
            FilePath = record.FilePath,
            FileName = record.FileName,
            Extension = record.Extension,
            CategoryKey = record.CategoryKey,
            CategoryName = record.CategoryName,
            LastWriteTime = ParseLastWriteUtc(record.LastWriteUtc).ToLocalTime(),
            IsAnimated = record.IsAnimated
        };
    }

    private static DateTime ParseLastWriteUtc(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }

    private static StickerLibrarySnapshot CreateSnapshot(IReadOnlyList<StickerItem> stickers, IReadOnlyList<CategoryIndexRecord> categoryRecords)
    {
        var countsByCategory = stickers
            .Where(sticker => sticker.CategoryKey is not null)
            .GroupBy(sticker => sticker.CategoryKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new StickerLibrarySnapshot
        {
            Stickers = stickers,
            DirectoryCategories = categoryRecords
                .OrderBy(category => category.CategoryName, StringComparer.CurrentCultureIgnoreCase)
                .Select(category => new StickerCategory
                {
                    Key = category.CategoryKey,
                    Name = category.CategoryName,
                    Kind = StickerCategoryKind.Directory,
                    DirectoryPath = category.DirectoryPath,
                    Count = countsByCategory.TryGetValue(category.CategoryKey, out var count) ? count : 0
                })
                .ToList(),
            TagCategories = []
        };
    }

    private static void UpsertStickerRecord(SqliteConnection connection, SqliteTransaction transaction, StickerIndexRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sticker_index (
                root_path, file_path, file_name, category_key, category_name, extension, file_size, last_write_utc, is_animated, indexed_at_utc
            )
            VALUES (
                $rootPath, $filePath, $fileName, $categoryKey, $categoryName, $extension, $fileSize, $lastWriteUtc, $isAnimated, $indexedAtUtc
            )
            ON CONFLICT(root_path, file_path) DO UPDATE SET
                file_name = excluded.file_name,
                category_key = excluded.category_key,
                category_name = excluded.category_name,
                extension = excluded.extension,
                file_size = excluded.file_size,
                last_write_utc = excluded.last_write_utc,
                is_animated = excluded.is_animated,
                indexed_at_utc = excluded.indexed_at_utc
            """;
        command.Parameters.AddWithValue("$rootPath", record.RootPath);
        command.Parameters.AddWithValue("$filePath", record.FilePath);
        command.Parameters.AddWithValue("$fileName", record.FileName);
        command.Parameters.AddWithValue("$categoryKey", (object?)record.CategoryKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$categoryName", record.CategoryName);
        command.Parameters.AddWithValue("$extension", record.Extension);
        command.Parameters.AddWithValue("$fileSize", record.FileSize);
        command.Parameters.AddWithValue("$lastWriteUtc", record.LastWriteUtc);
        command.Parameters.AddWithValue("$isAnimated", record.IsAnimated ? 1 : 0);
        command.Parameters.AddWithValue("$indexedAtUtc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void DeleteStickerRecord(SqliteConnection connection, SqliteTransaction transaction, string rootPath, string filePath)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sticker_index WHERE root_path = $rootPath AND file_path = $filePath";
        command.Parameters.AddWithValue("$rootPath", rootPath);
        command.Parameters.AddWithValue("$filePath", filePath);
        command.ExecuteNonQuery();
    }

    private static void ReplaceCategoryRecords(SqliteConnection connection, SqliteTransaction transaction, string rootPath, IReadOnlyList<CategoryIndexRecord> records)
    {
        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM category_index WHERE root_path = $rootPath";
            deleteCommand.Parameters.AddWithValue("$rootPath", rootPath);
            deleteCommand.ExecuteNonQuery();
        }

        foreach (var record in records)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO category_index (root_path, category_key, category_name, directory_path, indexed_at_utc)
                VALUES ($rootPath, $categoryKey, $categoryName, $directoryPath, $indexedAtUtc)
                """;
            insertCommand.Parameters.AddWithValue("$rootPath", record.RootPath);
            insertCommand.Parameters.AddWithValue("$categoryKey", record.CategoryKey);
            insertCommand.Parameters.AddWithValue("$categoryName", record.CategoryName);
            insertCommand.Parameters.AddWithValue("$directoryPath", record.DirectoryPath);
            insertCommand.Parameters.AddWithValue("$indexedAtUtc", DateTime.UtcNow.ToString("O"));
            insertCommand.ExecuteNonQuery();
        }
    }

    private void ResetDatabase()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsAnimatedGif(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[6];
            if (stream.Read(header) != header.Length || header is not [0x47, 0x49, 0x46, 0x38, _, 0x61])
            {
                return false;
            }

            var imageSeparatorCount = 0;
            int current;
            while ((current = stream.ReadByte()) != -1)
            {
                if (current == 0x2C && ++imageSeparatorCount > 1)
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    private sealed record StickerIndexRecord(
        string RootPath,
        string FilePath,
        string FileName,
        string? CategoryKey,
        string CategoryName,
        string Extension,
        long FileSize,
        string LastWriteUtc,
        bool IsAnimated);

    private sealed record CategoryIndexRecord(
        string RootPath,
        string CategoryKey,
        string CategoryName,
        string DirectoryPath);
}
