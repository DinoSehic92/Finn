using Finn.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Finn.Services;

/// <summary>
/// Watches shared project server files for changes using <see cref="FileSystemWatcher"/>.
/// Fires <see cref="ServerFileChanged"/> when a server JSON file is modified by another user.
/// Uses per-directory watchers with filename filters for efficiency.
/// </summary>
public sealed class SharedFileWatcherService : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private readonly HashSet<string> _changedFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raised (on a thread-pool thread) after server file changes settle.
    /// The argument is the set of full file paths that changed.
    /// </summary>
    public event Action<IReadOnlySet<string>>? ServerFileChanged;

    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// Rebuilds watchers from the current set of shared projects.
    /// One watcher per directory, filtering for .json files.
    /// </summary>
    public void Refresh(IEnumerable<ProjectData> projects)
    {
        var activePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            if (string.IsNullOrEmpty(project.SharedPath)) continue;
            string? dir = Path.GetDirectoryName(project.SharedPath);
            if (!string.IsNullOrEmpty(dir))
                activePaths.TryAdd(dir, dir);
        }

        lock (_lock)
        {
            var toRemove = new List<string>();
            foreach (var kvp in _watchers)
            {
                if (!activePaths.ContainsKey(kvp.Key))
                {
                    kvp.Value.EnableRaisingEvents = false;
                    kvp.Value.Dispose();
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var key in toRemove)
                _watchers.Remove(key);

            foreach (var dir in activePaths.Keys)
            {
                if (_watchers.ContainsKey(dir)) continue;

                try
                {
                    if (!Directory.Exists(dir)) continue;

                    var watcher = new FileSystemWatcher(dir, "*.json")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };

                    watcher.Changed += OnFileEvent;
                    watcher.Created += OnFileEvent;
                    watcher.Renamed += OnFileEvent;
                    watcher.Deleted += OnFileEvent;
                    watcher.Error += OnWatcherError;

                    _watchers[dir] = watcher;
                }
                catch { }
            }
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _changedFiles.Add(e.FullPath);
        }
        ResetDebounce();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (sender is FileSystemWatcher watcher)
        {
            lock (_lock)
            {
                // Flag the directory so all its files get re-checked
                foreach (var file in Directory.EnumerateFiles(watcher.Path, "*.json"))
                    _changedFiles.Add(file);
            }
            ResetDebounce();
        }
    }

    private void ResetDebounce()
    {
        var existing = _debounceTimer;
        if (existing != null)
        {
            try { existing.Change(DebounceInterval, Timeout.InfiniteTimeSpan); return; }
            catch (ObjectDisposedException) { }
        }
        var newTimer = new Timer(_ => FireChanged(), null, DebounceInterval, Timeout.InfiniteTimeSpan);
        var old = Interlocked.Exchange(ref _debounceTimer, newTimer);
        old?.Dispose();
    }

    private void FireChanged()
    {
        HashSet<string> snapshot;
        lock (_lock)
        {
            if (_changedFiles.Count == 0) return;
            snapshot = new HashSet<string>(_changedFiles, StringComparer.OrdinalIgnoreCase);
            _changedFiles.Clear();
        }
        ServerFileChanged?.Invoke(snapshot);
    }

    public void Dispose()
    {
        var timer = Interlocked.Exchange(ref _debounceTimer, null);
        timer?.Dispose();
        StopAll();
    }
}
