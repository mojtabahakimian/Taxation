using Prg_Moadian.Bulk;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Prg_Grpsend
{
    /// <summary>
    /// همهٔ هشدارهای ساخت فاکتور در ارسال گروهی، یکجا. قبلاً برای هر هشدارِ هر فاکتور یک
    /// پنجرهٔ جدا باز می‌شد. پیش‌فرض همه تیک دارند — هشدار است، نه بلاک.
    /// </summary>
    public partial class WIN_BULKWARNINGS : Window
    {
        public sealed class Row : INotifyPropertyChanged
        {
            private bool _send = true;
            public long Number { get; init; }
            public string Kinds { get; init; } = "";
            public string Messages { get; init; } = "";
            public HashSet<string> KindSet { get; init; } = new();
            public bool Send
            {
                get => _send;
                set { if (_send != value) { _send = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Send))); } }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private sealed record KindItem(string Kind, string Title);

        private readonly List<Row> _rows;
        private bool _decided;

        /// <summary>فاکتورهایی که کاربر نمی‌خواهد بفرستد؛ null یعنی لغو کل ارسال.</summary>
        public ISet<long>? Excluded { get; private set; }

        public WIN_BULKWARNINGS(IReadOnlyList<BulkWarning> warnings, int totalSelected)
        {
            InitializeComponent();

            _rows = warnings
                .GroupBy(w => w.Number)
                .OrderBy(g => g.Key)
                .Select(g => new Row
                {
                    Number = g.Key,
                    KindSet = g.Select(w => w.Kind).ToHashSet(),
                    Kinds = string.Join("، ", g.Select(w => w.Kind).Distinct()),
                    Messages = string.Join("\n", g.Select(w => StripNumber(w.Message, g.Key)).Distinct()),
                })
                .ToList();
            foreach (var r in _rows) r.PropertyChanged += (_, _) => UpdateCount();
            DG_WARNINGS.ItemsSource = _rows;

            CMB_KIND.ItemsSource = _rows
                .SelectMany(r => r.KindSet)
                .GroupBy(k => k)
                .OrderByDescending(g => g.Count())
                .Select(g => new KindItem(g.Key, $"{g.Key} ({g.Count()} فاکتور)"))
                .ToList();
            CMB_KIND.SelectedIndex = 0;

            TXT_SUMMARY.Text = $"{_rows.Count} فاکتور از {totalSelected} فاکتور انتخابی هشدار دارند.";
            UpdateCount();
        }

        /// <summary>شمارهٔ فاکتور در ستون جدا هست؛ «فاکتور ۱۲۳: » ابتدای پیام تکراری است.</summary>
        private static string StripNumber(string message, long number)
        {
            string prefix = $"فاکتور {number}: ";
            return message.StartsWith(prefix) ? message[prefix.Length..] : message;
        }

        private void UpdateCount()
        {
            int on = _rows.Count(r => r.Send);
            TXT_COUNT.Text = $"از {_rows.Count} فاکتور هشداردار، {on} فاکتور ارسال می‌شود و {_rows.Count - on} فاکتور کنار گذاشته می‌شود.";
        }

        private void SetAll(System.Func<Row, bool> which, bool send)
        {
            foreach (var r in _rows.Where(which)) r.Send = send;
        }

        private void BTN_ALL_Click(object sender, RoutedEventArgs e) => SetAll(_ => true, true);
        private void BTN_NONE_Click(object sender, RoutedEventArgs e) => SetAll(_ => true, false);

        private void BTN_KIND_OFF_Click(object sender, RoutedEventArgs e)
        {
            if (CMB_KIND.SelectedItem is KindItem k) SetAll(r => r.KindSet.Contains(k.Kind), false);
        }

        private void BTN_KIND_ON_Click(object sender, RoutedEventArgs e)
        {
            if (CMB_KIND.SelectedItem is KindItem k) SetAll(r => r.KindSet.Contains(k.Kind), true);
        }

        private void BTN_SEND_Click(object sender, RoutedEventArgs e)
        {
            DG_WARNINGS.CommitEdit();
            Excluded = _rows.Where(r => !r.Send).Select(r => r.Number).ToHashSet();
            _decided = true;
            DialogResult = true;
        }

        private void BTN_CANCEL_Click(object sender, RoutedEventArgs e)
        {
            Excluded = null;
            _decided = true;
            DialogResult = false;
        }

        // بستن پنجره با × یعنی لغو کل ارسال — نه «همه بروند».
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_decided) Excluded = null;
        }
    }
}
