using System;
using System.IO;

namespace Prg_Grpsend.Utility
{
    public static class LockLogger
    {
        //enable_lock_log.flag این فایل رو خالی کنار Realse EXE بساز
        private static readonly string LogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lock_debug.log");

        private static readonly bool IsEnabled = GetIsEnabled();

        private static bool GetIsEnabled()
        {
#if DEBUG
            return true;  // در Debug همیشه فعال
#else
        // در Release فقط اگر فایل flag وجود داشته باشه
        return File.Exists(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "enable_lock_log.flag"));
#endif
        }

        public static void Write(string message)
        {
            if (!IsEnabled) return;
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }
        }

        public static void Clear()
        {
            if (!IsEnabled) return;
            try { File.Delete(LogPath); } catch { }
        }
    }
}