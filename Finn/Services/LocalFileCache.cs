using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Finn.Services
{
    /// <summary>Result of a cache lookup.</summary>
    /// <param name="Path">The resolved local (or original) file path.</param>
    /// <param name="WasCacheHit">True when an existing valid cache entry was returned without copying.</param>
    /// <param name="WasStale">True when the server file had changed and the cached copy was refreshed.</param>
    public readonly record struct CacheResult(string Path, bool WasCacheHit, bool WasStale = false);

    /// <summary>
    /// Persistent local file cache. Copies network/remote files to a local
    /// directory on first access; subsequent requests return the local copy
    /// when the server file has not changed (checked via last-write timestamp + size).
    /// Read-only — never modifies the original file.
    /// <para>
    /// The cache index is persisted to <c>cache_index.json</c> so cached files
    /// survive application restarts. On startup, entries whose local files are
    /// missing are pruned automatically.
    /// </para>
    /// <para>
    /// Thread-safe: concurrent calls for the same server path are serialized via
    /// per-key locks so only one copy runs at a time (no orphaned temp files).
    /// </para>
    /// <para>
    /// Eviction: when total cached bytes exceed <see cref="MaxCacheBytes"/> the
    /// least-recently-used entries are evicted until the cache fits.
    /// </para>
    /// <para>
    /// Open-handle safety: callers can pin/unpin paths to prevent deletion while
    /// native file handles are open (e.g. MuPDF file-based documents).
    /// </para>
    /// </summary>
    public sealed class LocalFileCache : IDisposable
    {
        /// <summary>Maximum total cached bytes before LRU eviction kicks in (default 2 GB).</summary>
        public long MaxCacheBytes { get; set; } = 2L * 1024 * 1024 * 1024;

        private readonly string _cacheDir;
        private readonly string _indexPath;
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pinnedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _pinLock = new();
        private bool _disposed;
        private volatile bool _indexDirty;
        private Timer? _saveTimer;

        private sealed record CacheEntry(string LocalPath, DateTime LastWriteUtc, long Size, DateTime LastAccessUtc);

        /// <summary>Number of files currently tracked in the cache.</summary>
        public int CachedFileCount => _entries.Count;

        /// <summary>Total size (bytes) of all tracked cached files.
        /// Takes a snapshot of the dictionary to avoid inconsistent reads during concurrent modifications.</summary>
        public long CachedTotalBytes => _entries.ToArray().Sum(kv => kv.Value.Size);

        public LocalFileCache()
            : this(Path.Combine(Path.GetTempPath(), "Finn_FileCache"))
        {
        }

        /// <summary>
        /// Creates a cache rooted at the specified <paramref name="cacheDir"/>.
        /// The directory is created if it does not exist. Any persisted index
        /// from a previous session is loaded and validated.
        /// </summary>
        public LocalFileCache(string cacheDir)
        {
            _cacheDir = cacheDir;
            _indexPath = Path.Combine(_cacheDir, "cache_index.json");
            Directory.CreateDirectory(_cacheDir);
            LoadIndex();
        }

        /// <summary>
        /// Returns a local path for the given file. If the file resides on a
        /// network drive and a valid cached copy exists, the cached local path
        /// is returned. Otherwise the file is copied to the cache first.
        /// Non-network files are returned as-is (no copy needed).
        /// </summary>
        /// <param name="serverPath">The original network file path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onProgress">Optional progress callback (0–100).</param>
        public async Task<CacheResult> GetLocalPathAsync(string serverPath, CancellationToken token = default, Action<int>? onProgress = null)
        {
            if (string.IsNullOrEmpty(serverPath))
                return new CacheResult(serverPath, false);

            if (!IsNetworkPath(serverPath))
                return new CacheResult(serverPath, false);

            // Per-key lock: serializes concurrent requests for the same file
            // to prevent double-copies and orphaned temp files.
            var keyLock = _keyLocks.GetOrAdd(serverPath, _ => new SemaphoreSlim(1, 1));
            await keyLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Probe the network file on a background thread so slow/unreachable
                // servers never block the calling (UI) thread.
                var (exists, lastWrite, size) = await Task.Run(() =>
                {
                    try
                    {
                        var fi = new FileInfo(serverPath);
                        if (!fi.Exists)
                            return (false, DateTime.MinValue, 0L);
                        return (true, fi.LastWriteTimeUtc, fi.Length);
                    }
                    catch
                    {
                        return (false, DateTime.MinValue, 0L);
                    }
                }, token).ConfigureAwait(false);

                if (!exists)
                {
                    // Server unreachable or file missing — return existing cache if available
                    if (_entries.TryGetValue(serverPath, out var fallbackEntry) && File.Exists(fallbackEntry.LocalPath))
                        return new CacheResult(fallbackEntry.LocalPath, true);
                    return new CacheResult(serverPath, false);
                }

                bool wasStale = false;

                // Re-check after acquiring lock — another caller may have just cached it.
                if (_entries.TryGetValue(serverPath, out var cached)
                    && cached.LastWriteUtc == lastWrite
                    && cached.Size == size
                    && File.Exists(cached.LocalPath))
                {
                    // Touch LRU timestamp
                    _entries[serverPath] = cached with { LastAccessUtc = DateTime.UtcNow };
                    return new CacheResult(cached.LocalPath, true);
                }

                // Stale if we had a previous entry with different timestamp/size
                if (cached != null)
                    wasStale = true;

                // Cache miss or stale — copy file locally with progress
                string localPath = Path.Combine(_cacheDir, Guid.NewGuid().ToString("N") + Path.GetExtension(serverPath));

                await Task.Run(() =>
                {
                    CopyFileWithProgress(serverPath, localPath, size, token, onProgress);
                }, token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    TryDeleteFile(localPath);
                    return new CacheResult(serverPath, false);
                }

                // Delete previous cached copy if key existed (safe — we hold the per-key lock)
                if (cached != null)
                    TryDeleteFile(cached.LocalPath);

                _entries[serverPath] = new CacheEntry(localPath, lastWrite, size, DateTime.UtcNow);

                // Evict LRU entries if total size exceeds the cap
                EvictIfNeeded();

                MarkIndexDirty();

                return new CacheResult(localPath, false, wasStale);
            }
            catch
            {
                // If anything fails (server unreachable, etc.), try to return
                // an existing cached copy; otherwise fall back to the server path.
                if (_entries.TryGetValue(serverPath, out var fallback) && File.Exists(fallback.LocalPath))
                    return new CacheResult(fallback.LocalPath, true);

                return new CacheResult(serverPath, false);
            }
            finally
            {
                keyLock.Release();
                // Trim the per-key lock if no one else is waiting, to prevent
                // unbounded dictionary growth over the session lifetime.
                if (keyLock.CurrentCount == 1)
                {
                    if (_keyLocks.TryRemove(serverPath, out var removed) && removed != keyLock)
                        _keyLocks.TryAdd(serverPath, removed); // race: another thread re-added, restore
                }
            }
        }

        /// <summary>
        /// Buffered file copy with progress reporting. For small files (&lt; 4 MB)
        /// a single <see cref="File.Copy"/> is used; larger files stream in 64 KB
        /// chunks so progress can be reported on slow network links.
        /// </summary>
        private static void CopyFileWithProgress(string source, string dest, long totalBytes, CancellationToken token, Action<int>? onProgress)
        {
            const int bufferSize = 64 * 1024;
            const long smallFileThreshold = 4 * 1024 * 1024;

            if (totalBytes < smallFileThreshold || onProgress == null)
            {
                File.Copy(source, dest, overwrite: true);
                onProgress?.Invoke(100);
                return;
            }

            using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan);

            byte[] buffer = new byte[bufferSize];
            long copied = 0;
            int lastPercent = 0;
            int bytesRead;

            while ((bytesRead = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (token.IsCancellationRequested) return;
                dst.Write(buffer, 0, bytesRead);
                copied += bytesRead;

                int percent = (int)(100 * copied / totalBytes);
                if (percent > lastPercent)
                {
                    lastPercent = percent;
                    onProgress(percent);
                }
            }
        }

        /// <summary>
        /// Returns true when the given path is a UNC path (\\server\share) or
        /// resides on a mapped network drive.
        /// </summary>
        public static bool IsNetworkPath(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return true;

            try
            {
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
                {
                    var di = new DriveInfo(root[..1]);
                    return di.DriveType == DriveType.Network;
                }
            }
            catch
            {
                // DriveInfo can throw for invalid paths
            }

            return false;
        }

        /// <summary>
        /// Removes the cached entry for <paramref name="serverPath"/> so the
        /// next <see cref="GetLocalPathAsync"/> call re-copies from the server.
        /// </summary>
        public void Invalidate(string serverPath)
        {
            if (_entries.TryRemove(serverPath, out var entry))
            {
                if (!IsPinned(entry.LocalPath))
                    TryDeleteFile(entry.LocalPath);
                MarkIndexDirty();
            }
        }

        /// <summary>
        /// Marks a local cached path as "in use" so it will not be deleted by
        /// <see cref="Invalidate"/>, <see cref="Clear"/>, or LRU eviction while
        /// a native file handle is open.
        /// </summary>
        public void Pin(string localPath)
        {
            lock (_pinLock) _pinnedPaths.Add(localPath);
        }

        /// <summary>
        /// Releases a previously pinned path, allowing it to be cleaned up.
        /// If the path no longer exists in <see cref="_entries"/> it is deleted
        /// immediately (deferred cleanup).
        /// </summary>
        public void Unpin(string localPath)
        {
            bool shouldDelete = false;
            lock (_pinLock)
            {
                _pinnedPaths.Remove(localPath);
                // Check if entry was already evicted/invalidated while pinned
                bool stillTracked = _entries.Values.Any(e =>
                    string.Equals(e.LocalPath, localPath, StringComparison.OrdinalIgnoreCase));
                if (!stillTracked)
                    shouldDelete = true;
            }
            if (shouldDelete)
                TryDeleteFile(localPath);
        }

        private bool IsPinned(string localPath)
        {
            lock (_pinLock) return _pinnedPaths.Contains(localPath);
        }

        /// <summary>
        /// Evicts least-recently-used entries until total cached size is below
        /// <see cref="MaxCacheBytes"/>. Pinned entries are skipped.
        /// </summary>
        private void EvictIfNeeded()
        {
            // Snapshot the dictionary for a consistent view during eviction.
            var snapshot = _entries.ToArray();
            long total = snapshot.Sum(kv => kv.Value.Size);
            if (total <= MaxCacheBytes)
                return;

            // Sort by LRU — oldest access first
            var candidates = snapshot
                .OrderBy(kv => kv.Value.LastAccessUtc)
                .ToList();

            foreach (var kv in candidates)
            {
                if (total <= MaxCacheBytes)
                    break;

                if (IsPinned(kv.Value.LocalPath))
                    continue;

                if (_entries.TryRemove(kv.Key, out var removed))
                {
                    total -= removed.Size;
                    TryDeleteFile(removed.LocalPath);
                }
            }
        }

        /// <summary>Deletes all cached files, clears the dictionary, and removes the index.</summary>
        public void Clear()
        {
            foreach (var entry in _entries.Values)
            {
                if (!IsPinned(entry.LocalPath))
                    TryDeleteFile(entry.LocalPath);
            }

            _entries.Clear();
            TryDeleteFile(_indexPath);

            // Only delete the directory when no pinned files remain
            bool hasPins;
            lock (_pinLock) hasPins = _pinnedPaths.Count > 0;

            if (!hasPins)
            {
                try
                {
                    if (Directory.Exists(_cacheDir))
                        Directory.Delete(_cacheDir, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }

            // Recreate so subsequent cache operations work immediately
            Directory.CreateDirectory(_cacheDir);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop the debounce timer and flush any pending index writes.
            _saveTimer?.Dispose();
            _saveTimer = null;
            if (_indexDirty)
                SaveIndex();

            foreach (var kvp in _keyLocks)
                kvp.Value.Dispose();
            _keyLocks.Clear();
        }

        /// <summary>
        /// Removes cached entries whose server paths are not in the provided
        /// set of <paramref name="validPaths"/>. Call once at startup after
        /// loading Projects.json so orphaned cache entries (from unsaved
        /// IsCached toggling) are cleaned up.
        /// </summary>
        public void Reconcile(IReadOnlySet<string> validPaths)
        {
            var toRemove = _entries.Keys
                .Where(k => !validPaths.Contains(k))
                .ToList();

            foreach (var key in toRemove)
            {
                if (_entries.TryRemove(key, out var entry))
                {
                    if (!IsPinned(entry.LocalPath))
                        TryDeleteFile(entry.LocalPath);
                }
            }

            // Trim per-key locks for paths no longer tracked to prevent unbounded growth
            var staleLocks = _keyLocks.Keys
                .Where(k => !_entries.ContainsKey(k))
                .ToList();
            foreach (var key in staleLocks)
            {
                if (_keyLocks.TryRemove(key, out var sem))
                    sem.Dispose();
            }

            if (toRemove.Count > 0)
                MarkIndexDirty();
        }

        #region Index Persistence

        /// <summary>
        /// Marks the index as needing a write. The actual write is debounced —
        /// it fires 2 seconds after the last mutation. This avoids hammering
        /// disk during rapid pre-caching of many files.
        /// </summary>
        private void MarkIndexDirty()
        {
            _indexDirty = true;
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ =>
            {
                if (_indexDirty && !_disposed)
                    SaveIndex();
            }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }

        /// <summary>JSON-serializable DTO for the cache index file.</summary>
        private sealed class IndexEntry
        {
            public string ServerPath { get; set; } = "";
            public string LocalPath { get; set; } = "";
            public DateTime LastWriteUtc { get; set; }
            public long Size { get; set; }
            public DateTime LastAccessUtc { get; set; }
        }

        /// <summary>
        /// Loads the persisted index from disk and prunes entries whose local
        /// files no longer exist (e.g. manually deleted between sessions).
        /// </summary>
        private void LoadIndex()
        {
            try
            {
                if (!File.Exists(_indexPath)) return;

                var json = File.ReadAllText(_indexPath);
                var entries = JsonSerializer.Deserialize<List<IndexEntry>>(json);
                if (entries == null) return;

                foreach (var e in entries)
                {
                    if (File.Exists(e.LocalPath))
                    {
                        _entries[e.ServerPath] = new CacheEntry(
                            e.LocalPath, e.LastWriteUtc, e.Size, e.LastAccessUtc);
                    }
                    else
                    {
                        // Local file gone — skip this entry
                    }
                }
            }
            catch
            {
                // Corrupt or unreadable index — start fresh
            }
        }

        /// <summary>Persists the current in-memory index to disk.</summary>
        private void SaveIndex()
        {
            try
            {
                var list = _entries.Select(kv => new IndexEntry
                {
                    ServerPath = kv.Key,
                    LocalPath = kv.Value.LocalPath,
                    LastWriteUtc = kv.Value.LastWriteUtc,
                    Size = kv.Value.Size,
                    LastAccessUtc = kv.Value.LastAccessUtc,
                }).ToList();

                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_indexPath, json);
                _indexDirty = false;
            }
            catch
            {
                // Best-effort — don't crash if write fails; _indexDirty stays true
                // so the next timer tick or Dispose will retry.
            }
        }

        #endregion

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
