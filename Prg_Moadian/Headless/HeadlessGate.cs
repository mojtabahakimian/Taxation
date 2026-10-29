using Prg_Moadian.CNNMANAGER;

namespace Prg_Moadian.Headless
{
    /// <summary>
    /// قفلِ مشترکِ همه‌ی میزبان‌های بی‌پنجره (ارسالِ تکی، گروهی، استعلام).
    ///
    /// هسته پر از عضوِ استاتیک است (CL_MOADIAN، CL_ERRLST، TaxApiService.Instance، رشته‌ی اتصالِ
    /// CL_CCNNMANAGER، …). در MOADIAN.EXE هر کار پروسه‌ی خودش را داشت؛ در پروسه‌ی ماندگارِ سرور دو کار
    /// هم‌زمان حالتِ هم را خراب می‌کردند. پس در هر لحظه فقط یک کار، چه تکی چه گروهی چه استعلام.
    /// SemaphoreSlim است نه lock، چون ارسالِ گروهی async است و قفل باید از روی await رد شود.
    /// </summary>
    internal static class HeadlessGate
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// قفل را می‌گیرد و رشته‌ی اتصالِ میزبان را می‌گذارد؛ Dispose هر دو را برمی‌گرداند، و CONNECTION_STR ِ استاتیک را هم
        /// به مقدارِ پیش از ورود — وگرنه کدی در همان پروسه که بی ConnectionOverride کار می‌کند (هارنسِ تست) به دیتابیسِ
        /// آخرین میزبان، یا رشته‌ی خرابی که سازنده با آن شکست خورده بود، وصل می‌ماند.
        /// </summary>
        public static IDisposable Enter(string connectionString)
        {
            Gate.Wait();
            return new Releaser(connectionString);
        }

        public static async Task<IDisposable> EnterAsync(string connectionString, CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            return new Releaser(connectionString);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly string? _previous;
            private int _done;

            public Releaser(string connectionString)
            {
                _previous = CL_CCNNMANAGER.CONNECTION_STR;
                CL_CCNNMANAGER.ConnectionOverride = connectionString;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) != 0) return;
                CL_CCNNMANAGER.ConnectionOverride = null;
                CL_CCNNMANAGER.CONNECTION_STR = _previous!;
                Gate.Release();
            }
        }
    }
}
