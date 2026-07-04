using System.IO;
using QQStickerPanel.Models;

namespace QQStickerPanel.Services;

public sealed class QQWindowMatcher
{
    private readonly QQSettings _settings;
    private readonly string _logPath;

    public QQWindowMatcher(QQSettings settings, string dataDirectory)
    {
        _settings = settings;
        _logPath = Path.Combine(dataDirectory, "logs", "qq-window-match.log");
    }

    public bool TryMatch(QQWindowInfo candidate, bool isForeground, out QQWindowInfo matchedWindow)
    {
        var score = 0;
        string rejectReason = string.Empty;

        if (_settings.ProcessNames.Contains(candidate.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            score += 50;
        }
        else
        {
            rejectReason = "process-name-not-matched";
        }

        if (candidate.Width >= 320 && candidate.Height >= 240)
        {
            score += 10;
        }
        else if (string.IsNullOrEmpty(rejectReason))
        {
            rejectReason = "window-too-small";
        }

        if (!string.IsNullOrWhiteSpace(candidate.Title))
        {
            score += 10;
        }
        else if (string.IsNullOrEmpty(rejectReason))
        {
            rejectReason = "empty-title";
        }

        if (isForeground)
        {
            score += 20;
        }

        if (ContainsAny(candidate.Title, _settings.WindowMatch.TitleExcludeKeywords, out var titleKeyword))
        {
            score -= 80;
            rejectReason = $"title-excluded:{titleKeyword}";
        }

        if (ContainsAny(candidate.ClassName, _settings.WindowMatch.ClassExcludeKeywords, out var classKeyword))
        {
            score -= 80;
            rejectReason = $"class-excluded:{classKeyword}";
        }

        var minScore = Math.Max(1, _settings.WindowMatch.MinScore);
        if (score < minScore && string.IsNullOrEmpty(rejectReason))
        {
            rejectReason = $"score-below-threshold:{score}/{minScore}";
        }

        matchedWindow = new QQWindowInfo
        {
            Handle = candidate.Handle,
            ProcessId = candidate.ProcessId,
            ProcessName = candidate.ProcessName,
            Title = candidate.Title,
            ClassName = candidate.ClassName,
            Left = candidate.Left,
            Top = candidate.Top,
            Width = candidate.Width,
            Height = candidate.Height,
            MatchScore = score,
            RejectReason = score >= minScore && string.IsNullOrEmpty(rejectReason) ? string.Empty : rejectReason
        };

        var accepted = matchedWindow.RejectReason.Length == 0;
        WriteDiagnostics(matchedWindow, accepted);
        return accepted;
    }

    private static bool ContainsAny(string value, IEnumerable<string> keywords, out string keyword)
    {
        foreach (var candidate in keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword)))
        {
            if (value.Contains(candidate, StringComparison.CurrentCultureIgnoreCase))
            {
                keyword = candidate;
                return true;
            }
        }

        keyword = string.Empty;
        return false;
    }

    private void WriteDiagnostics(QQWindowInfo window, bool accepted)
    {
        if (!_settings.WindowMatch.EnableMatchDiagnostics)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(
                _logPath,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\t{(accepted ? "accept" : "reject")}\t" +
                $"hwnd=0x{window.Handle.ToInt64():X}\tpid={window.ProcessId}\tprocess={window.ProcessName}\t" +
                $"score={window.MatchScore}\treason={window.RejectReason}\tclass={window.ClassName}\t" +
                $"rect={window.Left},{window.Top},{window.Width},{window.Height}\ttitle={window.Title}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
