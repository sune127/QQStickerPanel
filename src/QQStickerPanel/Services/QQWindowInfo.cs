namespace QQStickerPanel.Services;

public sealed class QQWindowInfo
{
    public required IntPtr Handle { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int MatchScore { get; init; }
    public string RejectReason { get; init; } = string.Empty;
}
