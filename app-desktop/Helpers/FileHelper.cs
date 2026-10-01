using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Helper for appending text to a file in a thread‑safe, shared‑access manner.
    /// Uses a FileStream with FileShare.ReadWrite so multiple processes or diagnostics
    /// can read while the app is writing.
    /// </summary>
    public static class FileHelper
    {
        private const int BufferSize = 64 * 1024; // 64 KB buffer for efficient writes

        /// <summary>
        /// Append a line to a text file asynchronously. Creates the directory if missing.
        /// </summary>
        public static async Task AppendTextSharedAsync(string fullPath, string line)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException(nameof(fullPath));

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await using var fs = new FileStream(
                fullPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                BufferSize,
                useAsync: true);

            await using var writer = new StreamWriter(fs, Encoding.UTF8);
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }

        /// <summary>
        /// Synchronous version – useful when the caller is already on a background thread.
        /// </summary>
        public static void AppendTextShared(string fullPath, string line)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException(nameof(fullPath));

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var fs = new FileStream(
                fullPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                BufferSize,
                useAsync: false);

            using var writer = new StreamWriter(fs, Encoding.UTF8);
            writer.WriteLine(line);
        }
    }
}
