using System;
using System.Threading;

namespace Prg_Moadian.Service
{
    public static class TimeSync
    {
        private static long _timeOffsetTicks = 0;

        public static TimeSpan TimeOffset => new TimeSpan(Interlocked.Read(ref _timeOffsetTicks));

        public static void SyncWithServer(long serverTimeUnixMs)
        {
            long systemTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long diffMs = serverTimeUnixMs - systemTimeUnixMs;
            Interlocked.Exchange(ref _timeOffsetTicks, TimeSpan.FromMilliseconds(diffMs).Ticks);
        }

        public static DateTime Now => DateTime.Now.Add(TimeOffset);

        public static DateTime UtcNow => DateTime.UtcNow.Add(TimeOffset);

        public static long GetMoadianTimestamp()
        {
            return DateTimeOffset.UtcNow.Add(TimeOffset).ToUnixTimeMilliseconds();
        }
    }
}
