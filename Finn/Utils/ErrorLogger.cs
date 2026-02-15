using System;
using System.IO;

namespace Finn.Utils
{
    internal static class ErrorLogger
    {
        private static readonly object _sync = new object();

        public static void Log(Exception? ex, string? context = null)
        {
            try
            {
                var baseDir = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
                var logDir = Path.Combine(baseDir, "logs");
                Directory.CreateDirectory(logDir);
                var path = Path.Combine(logDir, "crash.log");

                lock (_sync)
                {
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
    }
}
