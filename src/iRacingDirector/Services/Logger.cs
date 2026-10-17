using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace IRacingDirector.Services;

public enum LogLevel { Info, Success, Warning, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>In-memory activity feed for the UI plus a rolling log file.</summary>
public sealed class Logger
{
    private const int MaxEntries = 500;
    private const long MaxFileBytes = 1_000_000;
    private readonly object _fileLock = new();

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public event Action<LogEntry>? Logged;

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Success(string message) => Write(LogLevel.Success, message);
    public void Warn(string message) => Write(LogLevel.Warning, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        WriteFile(entry);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Add(entry);
        else dispatcher.BeginInvoke(() => Add(entry));
    }

    private void Add(LogEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
        Logged?.Invoke(entry);
    }

    private void WriteFile(LogEntry entry)
    {
        try
        {
            lock (_fileLock)
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxFileBytes)
                    File.Move(file.FullName, file.FullName + ".old", overwrite: true);
                File.AppendAllText(AppPaths.LogFile,
                    $"{entry.Time:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
