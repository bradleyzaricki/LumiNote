using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LumikitApp;

/// <summary>Severity of an <see cref="AppLogEntry"/>. Error is the only level that
/// triggers the overlay console's notification pill; Info/Warning just accumulate.</summary>
public enum AppLogLevel { Info, Warning, Error }

/// <summary>One immutable log line.</summary>
public sealed record AppLogEntry(DateTime Timestamp, AppLogLevel Level, string Source, string Message);

/// <summary>
/// Central in-app log rendered by the overlay console. Layering convention: services
/// log diagnostics at Info/Warning under their own source tag (e.g. "Cloud", "Serial");
/// user-facing failures are logged as Error by the UI layer, so one failure produces
/// exactly one Error entry. EntryAdded is raised on the calling thread — UI subscribers
/// marshal to the dispatcher themselves.
/// </summary>
public interface IAppLog
{
    event Action<AppLogEntry>? EntryAdded;

    /// <summary>Bounded history (oldest first) for late subscribers, e.g. a console opened mid-session.</summary>
    IReadOnlyList<AppLogEntry> Snapshot();

    void Clear();

    void Info(string message, string source = "App");
    void Warn(string message, string source = "App");
    void Error(string message, string source = "App");
}

public sealed class AppLog : IAppLog
{
    /// <summary>Ring-buffer bound so a long session can't grow memory without limit.</summary>
    public const int MaxEntries = 500;

    private readonly object _gate = new();
    private readonly List<AppLogEntry> _entries = new();

    public event Action<AppLogEntry>? EntryAdded;

    public IReadOnlyList<AppLogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    public void Info(string message, string source = "App") => Add(AppLogLevel.Info, message, source);
    public void Warn(string message, string source = "App") => Add(AppLogLevel.Warning, message, source);
    public void Error(string message, string source = "App") => Add(AppLogLevel.Error, message, source);

    private void Add(AppLogLevel level, string message, string source)
    {
        var entry = new AppLogEntry(DateTime.Now, level, source, message);
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        }
        // Mirror to the debugger output so diagnostics survive even with the console closed.
        Trace.WriteLine($"[{entry.Timestamp:HH:mm:ss} {level}] [{source}] {message}");
        EntryAdded?.Invoke(entry);
    }
}
