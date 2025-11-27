using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prg_Moadian.Service
{
    public static class TimeSync
    {
        // این متغیر اختلاف زمانی را نگه می‌دارد
        public static TimeSpan TimeOffset { get; private set; } = TimeSpan.Zero;

        public static void SyncWithServer(long serverTimeUnixMs)
        {
            // زمان فعلی سیستم به فرمت یونیکس
            long systemTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // محاسبه اختلاف (ساعت سرور منهای ساعت سیستم)
            long diffMs = serverTimeUnixMs - systemTimeUnixMs;

            TimeOffset = TimeSpan.FromMilliseconds(diffMs);
        }

        // هر جا تاریخ خواستید از این پروپرتی استفاده کنید
        public static DateTime Now
        {
            get { return DateTime.Now.Add(TimeOffset); }
        }

        public static DateTime UtcNow
        {
            get { return DateTime.UtcNow.Add(TimeOffset); }
        }

        // متد مخصوص سامانه مودیان برای دریافت Long
        public static long GetMoadianTimestamp()
        {
            return DateTimeOffset.UtcNow.Add(TimeOffset).ToUnixTimeMilliseconds();
        }
    }
}
