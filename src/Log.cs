using System;
using System.Globalization;
using System.IO;

namespace DesktopMonitor
{
    // Append-only diagnostics log in %APPDATA%\DesktopMonitor\log.txt, restarted once it passes 100 KB. Never throws.
    public static class Log
    {
        private static readonly object Gate = new object();

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopMonitor"); }
        }

        // Tests point this at a temp file.
        public static string FilePath = Path.Combine(DataDir, "log.txt");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 100 * 1024) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Logging must never take the app down.
            }
        }
    }
}
