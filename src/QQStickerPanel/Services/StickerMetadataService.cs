using System.IO;
using Microsoft.Data.Sqlite;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class StickerMetadataService
{
    private readonly string _databasePath;

    public StickerMetadataService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _databasePath = Path.Combine(dataDirectory, "metadata.db");
        Initialize();
    }

    public string GetHash(StickerItem sticker) => GetHash(sticker.FilePath);

    public string GetHash(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var lastWriteUtc = fileInfo.LastWriteTimeUtc.ToString("O");

        using var connection = OpenConnection();
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = """
                SELECT sha256
                FROM stickers
                WHERE file_path = $filePath AND file_size = $fileSize AND last_write_utc = $lastWriteUtc AND sha256 IS NOT NULL
                """;
            selectCommand.Parameters.AddWithValue("$filePath", fileInfo.FullName);
            selectCommand.Parameters.AddWithValue("$fileSize", fileInfo.Length);
            selectCommand.Parameters.AddWithValue("$lastWriteUtc", lastWriteUtc);
            var cachedHash = selectCommand.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(cachedHash))
            {
                return cachedHash;
            }
        }

        var hash = ComputeHash(fileInfo.FullName);
        UpsertSticker(connection, fileInfo.FullName, fileInfo.Length, lastWriteUtc, hash);
        return hash;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetTagsByPath(IEnumerable<StickerItem> stickers)
    {
        var paths = stickers.Select(sticker => sticker.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var tagsByPath = paths.ToDictionary(path => path, _ => (IReadOnlyList<string>)[], StringComparer.OrdinalIgnoreCase);
        using var connection = OpenConnection();
        foreach (var path in paths)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT tag_name
                FROM sticker_tags
                WHERE file_path = $filePath
                ORDER BY tag_name COLLATE NOCASE
                """;
            command.Parameters.AddWithValue("$filePath", path);
            var tags = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tags.Add(reader.GetString(0));
            }

            tagsByPath[path] = tags;
        }

        return tagsByPath;
    }

    public IReadOnlyList<string> GetAllTags()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM tags ORDER BY name COLLATE NOCASE";
        var tags = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    public void SetTags(IEnumerable<StickerItem> stickers, IEnumerable<string> tags)
    {
        var stickerList = stickers.ToList();
        var tagList = NormalizeTags(tags);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var sticker in stickerList)
        {
            EnsureStickerRow(connection, transaction, sticker.FilePath);
            DeleteStickerTags(connection, transaction, sticker.FilePath);
            foreach (var tag in tagList)
            {
                InsertTag(connection, transaction, tag);
                InsertStickerTag(connection, transaction, sticker.FilePath, tag);
            }
        }

        transaction.Commit();
    }

    public void AddTags(IEnumerable<StickerItem> stickers, IEnumerable<string> tags)
    {
        var stickerList = stickers.ToList();
        var tagList = NormalizeTags(tags);
        if (stickerList.Count == 0 || tagList.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var sticker in stickerList)
        {
            EnsureStickerRow(connection, transaction, sticker.FilePath);
            foreach (var tag in tagList)
            {
                InsertTag(connection, transaction, tag);
                InsertStickerTag(connection, transaction, sticker.FilePath, tag);
            }
        }

        transaction.Commit();
    }

    public void ClearTags(IEnumerable<StickerItem> stickers)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var sticker in stickers)
        {
            DeleteStickerTags(connection, transaction, sticker.FilePath);
        }

        transaction.Commit();
    }

    public void Remove(StickerItem sticker)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM stickers WHERE file_path = $filePath";
        command.Parameters.AddWithValue("$filePath", sticker.FilePath);
        command.ExecuteNonQuery();
    }

    public void UpdatePath(string oldPath, string newPath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OR REPLACE stickers
            SET file_path = $newPath
            WHERE file_path = $oldPath
            """;
        command.Parameters.AddWithValue("$oldPath", oldPath);
        command.Parameters.AddWithValue("$newPath", newPath);
        command.ExecuteNonQuery();
    }

    public void UpdateDirectory(string oldDirectory, string newDirectory)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText = "SELECT file_path FROM stickers";
        var changedPaths = new List<string>();
        using (var reader = selectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (IsInDirectory(path, oldDirectory))
                {
                    changedPaths.Add(path);
                }
            }
        }

        foreach (var oldPath in changedPaths)
        {
            var relativePath = Path.GetRelativePath(oldDirectory, oldPath);
            var newPath = Path.Combine(newDirectory, relativePath);
            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = "UPDATE OR REPLACE stickers SET file_path = $newPath WHERE file_path = $oldPath";
            updateCommand.Parameters.AddWithValue("$oldPath", oldPath);
            updateCommand.Parameters.AddWithValue("$newPath", newPath);
            updateCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public int PruneMissingFiles()
    {
        using var connection = OpenConnection();
        using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = "SELECT file_path FROM stickers";
        var missingPaths = new List<string>();
        using (var reader = selectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (!File.Exists(path))
                {
                    missingPaths.Add(path);
                }
            }
        }

        using var transaction = connection.BeginTransaction();
        foreach (var path in missingPaths)
        {
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM stickers WHERE file_path = $filePath";
            deleteCommand.Parameters.AddWithValue("$filePath", path);
            deleteCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return missingPaths.Count;
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags.SelectMany(tag => tag.Split(['#', ',', '，', ';', '；', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(tag => tag.Length > 0)
            .Select(tag => tag.Length > 32 ? tag[..32] : tag)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS stickers (
                file_path TEXT PRIMARY KEY,
                sha256 TEXT NULL,
                file_size INTEGER NOT NULL DEFAULT 0,
                last_write_utc TEXT NOT NULL DEFAULT '',
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tags (
                name TEXT PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS sticker_tags (
                file_path TEXT NOT NULL,
                tag_name TEXT NOT NULL,
                PRIMARY KEY (file_path, tag_name),
                FOREIGN KEY (file_path) REFERENCES stickers(file_path) ON DELETE CASCADE,
                FOREIGN KEY (tag_name) REFERENCES tags(name) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_sticker_tags_tag_name ON sticker_tags(tag_name);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void UpsertSticker(SqliteConnection connection, string filePath, long fileSize, string lastWriteUtc, string hash)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO stickers (file_path, sha256, file_size, last_write_utc, updated_at_utc)
            VALUES ($filePath, $sha256, $fileSize, $lastWriteUtc, $updatedAtUtc)
            ON CONFLICT(file_path) DO UPDATE SET
                sha256 = excluded.sha256,
                file_size = excluded.file_size,
                last_write_utc = excluded.last_write_utc,
                updated_at_utc = excluded.updated_at_utc
            """;
        command.Parameters.AddWithValue("$filePath", filePath);
        command.Parameters.AddWithValue("$sha256", hash);
        command.Parameters.AddWithValue("$fileSize", fileSize);
        command.Parameters.AddWithValue("$lastWriteUtc", lastWriteUtc);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void EnsureStickerRow(SqliteConnection connection, SqliteTransaction transaction, string filePath)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO stickers (file_path, updated_at_utc)
            VALUES ($filePath, $updatedAtUtc)
            ON CONFLICT(file_path) DO NOTHING
            """;
        command.Parameters.AddWithValue("$filePath", filePath);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertTag(SqliteConnection connection, SqliteTransaction transaction, string tag)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO tags (name) VALUES ($name) ON CONFLICT(name) DO NOTHING";
        command.Parameters.AddWithValue("$name", tag);
        command.ExecuteNonQuery();
    }

    private static void InsertStickerTag(SqliteConnection connection, SqliteTransaction transaction, string filePath, string tag)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sticker_tags (file_path, tag_name)
            VALUES ($filePath, $tagName)
            ON CONFLICT(file_path, tag_name) DO NOTHING
            """;
        command.Parameters.AddWithValue("$filePath", filePath);
        command.Parameters.AddWithValue("$tagName", tag);
        command.ExecuteNonQuery();
    }

    private static void DeleteStickerTags(SqliteConnection connection, SqliteTransaction transaction, string filePath)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sticker_tags WHERE file_path = $filePath";
        command.Parameters.AddWithValue("$filePath", filePath);
        command.ExecuteNonQuery();
    }

    private static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static bool IsInDirectory(string filePath, string directoryPath)
    {
        var relativePath = Path.GetRelativePath(directoryPath, filePath);
        return relativePath != "." && !relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath);
    }
}
