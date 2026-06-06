using System;
using System.IO;

namespace Prg_Grpsend.Utility
{
    public static class LockLogger
    {
        private static readonly string LogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lock_debug.log");

        public static void Write(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }
        }

        public static void Clear()
        {
            try { File.Delete(LogPath); } catch { }
        }
    }
}