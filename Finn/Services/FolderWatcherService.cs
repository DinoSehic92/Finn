using Finn.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Finn.Services
{
    /// <summary>
    /// Watches project sync folders via <see cref="FileSystemWatcher"/> and
    /// raises <see cref="FolderChanged"/> when files are created, renamed,
    /// or deleted. Uses a debounce timer so rapid bursts of filesystem events
    /// collapse into a single callback.
    /// </summary>
    public sealed class FolderWatcherService : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private Timer? _debounceTimer;
        private readonly HashSet<string> _changedFolders = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Raised (on a thread-pool thread) after filesystem changes settle.
        /// The argument is the set of folder paths that changed.
        /// </summary>
        public event Action<IReadOnlySet<string>>? FolderChanged;

        /// <summary>Debounce interval for coalescing rapid filesystem events.</summary>
        public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Rebuilds the set of watched folders from the current project list.
        /// Adds watchers for new folders and removes watchers for folders
        /// that are no longer referenced.
        /// </summary>
        public void Refresh(IEnumerable<ProjectData> projects)
        {
            var activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in projects)
            {
                foreach (var folder in project.Folders)
                {
                    if (folder.IsValid() && !string.IsNullOrEmpty(folder.Path))
                        activePaths.Add(folder.Path);
                }
            }

            lock (_lock)
            {
                // Remove watchers for folders no longer in the project
                var toRemove = new List<string>();
                foreach (var kvp in _watchers)
                {
                    if (!activePaths.Contains(kvp.Key))
                    {
                        kvp.Value.EnableRaisingEvents = false;
                        kvp.Value.Dispose();
                        toRemove.Add(kvp.Key);
                    }
                }
                foreach (var key in toRemove)
                    _watchers.Remove(key);

                // Add watchers for new folders
                foreach (var path in activePaths)
                {
                    if (_watchers.ContainsKey(path))
                        continue;

                    try
                    {
                        var watcher = new FileSystemWatcher(path)
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                            IncludeSubdirectories = true,
                            EnableRaisingEvents = true
                        };

                        watcher.Created += OnFileEvent;
                        watcher.Deleted += OnFileEvent;
                        watcher.Renamed += OnFileEvent;
                        watcher.Error += OnWatcherError;

                        _watchers[path] = watcher;
                    }
                    catch
                    {
                        // Path may have become invalid between check and watcher creation
                    }
                }
            }
        }

        /// <summary>Stops all watchers and clears state.</summary>
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
            if (sender is FileSystemWatcher watcher)
            {
                lock (_lock)
                {
                    _changedFolders.Add(watcher.Path);
                }
                ResetDebounce();
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            // Watcher buffer overflowed or folder was removed — treat as a change
            if (sender is FileSystemWatcher watcher)
            {
                lock (_lock)
                {
                    _changedFolders.Add(watcher.Path);
                }
                ResetDebounce();
            }
        }

        private void ResetDebounce()
        {
            var newTimer = new Timer(_ => FireChanged(), null, DebounceInterval, Timeout.InfiniteTimeSpan);
            var old = Interlocked.Exchange(ref _debounceTimer, newTimer);
            old?.Dispose();
        }

        private void FireChanged()
        {
            HashSet<string> snapshot;
            lock (_lock)
            {
                if (_changedFolders.Count == 0) return;
                snapshot = new HashSet<string>(_changedFolders, StringComparer.OrdinalIgnoreCase);
                _changedFolders.Clear();
            }
            FolderChanged?.Invoke(snapshot);
        }

        public void Dispose()
        {
            var timer = Interlocked.Exchange(ref _debounceTimer, null);
            timer?.Dispose();
            StopAll();
        }
    }
}
