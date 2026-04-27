using System;
using System.IO;

namespace Finn.Utils
{
    internal static class ErrorLogger
    {
        private static readonly object _sync = new object();

        /// <summary>Maximum crash.log size before it is rotated (5 MB).</summary>
        private const long MaxLogBytes = 5 * 1024 * 1024;

        /// <summary>Number of rotated log files to keep (crash.log.1 … crash.log.N).</summary>
        private const int MaxBackups = 3;

        public static void Log(Exception? ex, string? context = null)
        {
            try
            {
                // Try to write logs next to the executable. If that is not writable
                // (single-file publish or protected install dir), fall back to
                // LocalApplicationData which is per-user and writable.
                var baseDir = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
                var logDir = Path.Combine(baseDir, "logs");

                try
                {
                    Directory.CreateDirectory(logDir);
                }
                catch
                {
                    // Fallback to per-user local app data
                    var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    baseDir = Path.Combine(localApp, "Finn");
                    logDir = Path.Combine(baseDir, "logs");
                    Directory.CreateDirectory(logDir);
                }

                var path = Path.Combine(logDir, "crash.log");

                lock (_sync)
                {
                    RotateIfNeeded(path);

                    using var sw = new StreamWriter(path, append: true);
                    sw.WriteLine("----------------------------------------");
                    sw.WriteLine(DateTime.UtcNow.ToString("o") + (context != null ? " - " + context : string.Empty));
                    if (ex != null)
                    {
                        sw.WriteLine(ex.ToString());
                    }
                    else
                    {
                        sw.WriteLine("(no exception provided)");
                    }
                }
            }
            catch
            {
                // Swallow any logging failures to avoid cascading crashes
            }
        }

        /// <summary>
        /// When <paramref name="logPath"/> exceeds <see cref="MaxLogBytes"/>, shifts
        /// existing backups down by one slot and renames the current file to slot 1.
        /// Must be called inside <see cref="_sync"/>.
        /// </summary>
        private static void RotateIfNeeded(string logPath)
        {
            try
            {
                if (!File.Exists(logPath)) return;
                if (new FileInfo(logPath).Length < MaxLogBytes) return;

                // Shift: .3 → deleted, .2 → .3, .1 → .2, (current) → .1
                for (int i = MaxBackups; i >= 1; i--)
                {
                    string older = logPath + "." + i;
                    string newer = i == 1 ? logPath : logPath + "." + (i - 1);
                    if (File.Exists(older))
                        File.Delete(older);
                    if (File.Exists(newer))
                        File.Move(newer, older);
                }
            }
            catch
            {
                // Rotation failure is non-fatal; logging continues to the existing file.
            }
        }
    }
}
