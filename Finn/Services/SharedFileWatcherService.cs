using Finn.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Finn.Services;

/// <summary>
/// Watches shared project server files for changes using <see cref="FileSystemWatcher"/>
/// with a periodic polling fallback for network shares where FSW is unreliable.
/// Fires <see cref="ServerFileChanged"/> when a server JSON file is modified by another user.
/// Uses per-directory watchers with filename filters for efficiency.
/// </summary>
public sealed class SharedFileWatcherService : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private Timer? _pollTimer;
    private readonly HashSet<string> _changedFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracked file paths and their last-known write timestamps.
    /// Updated after each poll cycle so we only fire on actual changes.
    /// </summary>
    private readonly Dictionary<string, DateTime> _lastKnownWriteTimes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raised (on a thread-pool thread) after server file changes settle.
    /// The argument is the set of full file paths that changed.
    /// </summary>
    public event Action<IReadOnlySet<string>>? ServerFileChanged;

    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// How often the polling fallback checks server files for changes.
    /// Covers cases where <see cref="FileSystemWatcher"/> misses events
    /// on network/UNC paths.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Rebuilds watchers from the current set of shared projects.
    /// One watcher per directory, filtering for .json files.
    /// </summary>
    public void Refresh(IEnumerable<ProjectData> projects)
    {
        var activePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trackedFiles = new List<string>();
        foreach (var project in projects)
        {
            if (string.IsNullOrEmpty(project.SharedPath)) continue;
            trackedFiles.Add(project.SharedPath);
            string? dir = Path.GetDirectoryName(project.SharedPath);
            if (!string.IsNullOrEmpty(dir))
                activePaths.TryAdd(dir, dir);
        }
        System.Diagnostics.Debug.WriteLine($"[SharedWatcher] Refresh: {trackedFiles.Count} tracked files, {activePaths.Count} directories");
        foreach (var f in trackedFiles)
            System.Diagnostics.Debug.WriteLine($"[SharedWatcher]   tracked: {f}");

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
                    System.Diagnostics.Debug.WriteLine($"[SharedWatcher] Created FSW for: {dir}");
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SharedWatcher] FSW creation failed for {dir}: {ex.Message}"); }
            }

            // Seed baseline timestamps for the polling fallback
            var staleKeys = _lastKnownWriteTimes.Keys.Except(trackedFiles, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var key in staleKeys)
                _lastKnownWriteTimes.Remove(key);

            foreach (var file in trackedFiles)
            {
                if (!_lastKnownWriteTimes.ContainsKey(file))
                {
                    try { _lastKnownWriteTimes[file] = File.GetLastWriteTimeUtc(file); }
                    catch { _lastKnownWriteTimes[file] = DateTime.MinValue; }
                    System.Diagnostics.Debug.WriteLine($"[SharedWatcher] Baseline seeded: {file} = {_lastKnownWriteTimes[file]:O}");
                }
            }
        }

        // Start or restart the polling timer
        _pollTimer?.Dispose();
        if (trackedFiles.Count > 0)
        {
            _pollTimer = new Timer(_ => PollForChanges(), null, PollInterval, PollInterval);
            System.Diagnostics.Debug.WriteLine($"[SharedWatcher] Poll timer started ({PollInterval.TotalSeconds}s interval)");
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
            _lastKnownWriteTimes.Clear();
        }
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[SharedWatcher] FSW event: {e.ChangeType} {e.FullPath}");
        lock (_lock)
        {
            _changedFiles.Add(e.FullPath);
            // Update baseline so the next poll doesn't double-fire
            try { _lastKnownWriteTimes[e.FullPath] = File.GetLastWriteTimeUtc(e.FullPath); }
            catch { /* file may be mid-write */ }
        }
        ResetDebounce();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (sender is FileSystemWatcher watcher)
        {
            try
            {
                lock (_lock)
                {
                    // Flag the directory so all its files get re-checked
                    foreach (var file in Directory.EnumerateFiles(watcher.Path, "*.json"))
                        _changedFiles.Add(file);
                }
                ResetDebounce();
            }
            catch { /* directory may be unavailable — ignore gracefully */ }
        }
    }

    /// <summary>
    /// Periodic fallback: compares current write times against the last-known
    /// baselines. Catches changes that <see cref="FileSystemWatcher"/> missed
    /// (common on network/UNC paths).
    /// </summary>
    private void PollForChanges()
    {
        List<string>? changed = null;

        lock (_lock)
        {
            // Snapshot the keys so we don't modify the dictionary during enumeration
            var paths = _lastKnownWriteTimes.Keys.ToArray();
            foreach (var path in paths)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var current = File.GetLastWriteTimeUtc(path);
                    var baseline = _lastKnownWriteTimes[path];
                    System.Diagnostics.Debug.WriteLine($"[SharedWatcher] Poll: {Path.GetFileName(path)} current={current:O} baseline={baseline:O} delta={current - baseline}");
                    if (current > baseline.AddSeconds(1))
                    {
                        _lastKnownWriteTimes[path] = current;
                        changed ??= [];
                        changed.Add(path);
                        System.Diagnostics.Debug.WriteLine($"[SharedWatcher] Poll detected change: {path}");
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SharedWatcher] Poll error for {path}: {ex.Message}"); }
            }
        }

        if (changed is { Count: > 0 })
        {
            lock (_lock)
            {
                foreach (var path in changed)
                    _changedFiles.Add(path);
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
        System.Diagnostics.Debug.WriteLine($"[SharedWatcher] FireChanged: {snapshot.Count} files — {string.Join(", ", snapshot.Select(Path.GetFileName))}");
        System.Diagnostics.Debug.WriteLine($"[SharedWatcher] Subscribers: {ServerFileChanged?.GetInvocationList().Length ?? 0}");
        ServerFileChanged?.Invoke(snapshot);
    }

    public void Dispose()
    {
        var timer = Interlocked.Exchange(ref _debounceTimer, null);
        timer?.Dispose();
        _pollTimer?.Dispose();
        _pollTimer = null;
        StopAll();
    }
}
