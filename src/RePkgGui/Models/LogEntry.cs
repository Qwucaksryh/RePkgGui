namespace RePkgGui.Models;

/// <summary>日志级别，用于在日志区域着色。</summary>
public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>一条日志。</summary>
public sealed class LogEntry
{
    public LogEntry(string text, LogLevel level)
    {
        Text = text;
        Level = level;
        Time = DateTime.Now;
    }

    public DateTime Time { get; }
    public string Text { get; }
    public LogLevel Level { get; }

    public string TimeText => Time.ToString("HH:mm:ss");

    public static LogEntry Info(string text) => new(text, LogLevel.Info);
    public static LogEntry Success(string text) => new(text, LogLevel.Success);
    public static LogEntry Warning(string text) => new(text, LogLevel.Warning);
    public static LogEntry Error(string text) => new(text, LogLevel.Error);
}
