// ============================================================================
//  فقط برای ساخت لینوکسی هارنس (گروه ۲۹). هرگز همراه برنامه منتشر نشود.
// ============================================================================
//
//  دیتابیس ساختگیِ درون‌حافظه، درست روی درزِ CL_CCNNMANAGER.
//
//  ⚠️ صادقانه: این دیتابیس «منطق C# دور و بر دیتابیس» را می‌آزماید، نه خودِ SQL را.
//     رشته‌های SQL این‌جا با regex شناسایی و با یک مفسر کوچک اجرا می‌شوند؛ این‌که
//     SQL Server واقعی همین رشته‌ها را همین‌طور می‌فهمد (نام ستون‌ها، نوع ستون‌ها،
//     JOIN های ویوی DRVD_TBL، HEAD_BACK_ANBAR با TAG-11، collation) این‌جا
//     آزموده «نمی‌شود». آن فقط روی ماشین توسعه‌دهنده با ‎-Full‎ راستی‌آزمایی می‌شود.
//     ردیف‌های DRVD_TBL «خروجیِ» ویو هستند که دستی ساخته شده‌اند، نه جدول‌های پایه.
//
//  چطور وصل می‌شود (بدون تغییر سورس تولید، فقط در زمان اجرا، فقط وقتی فعال است):
//    یک لایه زیرِ CL_CCNNMANAGER، روی خود SqlClient، چهار پیشوند Harmony:
//      SqlConnection.Open            → کاری نکن (اتصال شبکه‌ای زده نمی‌شود)
//      SqlCommand.ExecuteDbDataReader → نتیجهٔ FakeDb.Query به صورت DataTableReader
//      SqlCommand.ExecuteNonQuery     → FakeDb.Execute
//      SqlCommand.ExecuteScalar       → اولین خانهٔ FakeDb.Query
//    پس همهٔ CL_CCNNMANAGER (DoGetDataSQL<T>، DoExecuteSQL، try/catch، لاگ) و Dapper
//    (ساختن پارامترها، نگاشت ستون به خاصیت) همان کد واقعی‌اند.
//    چرا نه روی خود DoGetDataSQL<T>؟ Harmony کد اشتراکیِ جنریک (__Canon) را بدون
//    زمینهٔ نوع کامپایل می‌کند و typeof(T) داخلش null می‌شود — آزموده و کنار گذاشته شد.
//    بدیهی است هر استفادهٔ مستقیم از SqlConnection در همین پروسه هم به FakeDb می‌رسد.
//    تراکنش (BeginTransaction) پشتیبانی نمی‌شود و با خطای «اتصال بسته است» می‌افتد
//    (فقط DoGetDataSQL_Safe/DoExecuteSQL_Safe از آن استفاده می‌کنند که کسی صدایشان نمی‌زند).
//
//  کوئری ناشناخته → استثنای بلند با متن SQL (هرگز نتیجهٔ خالیِ بی‌صدا).
//  هر نوشتن (INSERT/UPDATE/DELETE) با SQL و پارامترهایش در Journal ثبت می‌شود.
//
//  فعال‌سازی فقط صریح: FakeDb.Activate() (گروه ۲۹). NoDbStub دست‌نخورده است.
// ============================================================================

using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Dapper;
using HarmonyLib;
using Microsoft.Data.SqlClient;
using Prg_Moadian.CNNMANAGER;

namespace MoadianLocalTest.Shim
{
    /// <summary>یک فراخوانی ثبت‌شده در دیتابیس ساختگی.</summary>
    internal sealed record FakeDbCall(long Seq, bool IsWrite, string Shape, string Sql,
                                      IReadOnlyDictionary<string, object?> Params, int Affected);

    internal sealed class FakeDbException : Exception
    {
        public FakeDbException(string message) : base(message) { }
    }

    internal sealed class FakeDb
    {
        // ------------------------------------------------------------ نصب و فعال‌سازی

        private static readonly object InstallGate = new();
        private static bool _installed;
        private static volatile FakeDb? _current;

        public static FakeDb? Current => _current;

        /// <summary>دیتابیس تازه و خالی می‌سازد و فعالش می‌کند.</summary>
        public static FakeDb Activate()
        {
            Install();
            var db = new FakeDb();
            _current = db;
            return db;
        }

        public static void Deactivate() => _current = null;

        private static void Install()
        {
            lock (InstallGate)
            {
                if (_installed) return;
                var h = new Harmony("moadian.linux.shim.fakedb");
                void Pre(Type t, string method, string prefix, Type[]? args = null)
                {
                    var m = (args == null ? AccessTools.Method(t, method) : AccessTools.Method(t, method, args))
                            ?? throw new InvalidOperationException($"FakeDb: {t.Name}.{method} پیدا نشد");
                    h.Patch(m, prefix: new HarmonyMethod(typeof(FakeDb), prefix));
                }
                Pre(typeof(SqlConnection), nameof(SqlConnection.Open), nameof(OpenPrefix), Type.EmptyTypes);
                Pre(typeof(SqlCommand), "ExecuteDbDataReader", nameof(ReaderPrefix));
                Pre(typeof(SqlCommand), nameof(SqlCommand.ExecuteNonQuery), nameof(NonQueryPrefix));
                Pre(typeof(SqlCommand), nameof(SqlCommand.ExecuteScalar), nameof(ScalarPrefix));
                _installed = true;
            }
        }

        // وقتی FakeDb فعال نیست همهٔ پیشوندها true برمی‌گردانند → SqlClient واقعی بی‌تغییر اجرا می‌شود.
        private static bool OpenPrefix() => _current == null;

        private static bool ReaderPrefix(SqlCommand __instance, ref DbDataReader __result)
        {
            var db = _current;
            if (db == null) return true;
            var text = Normalize(__instance.CommandText);
            if (Regex.IsMatch(text, @"^(INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase))
            {
                db.Execute(__instance.CommandText, Params(__instance));
                __result = new DataTable().CreateDataReader();
            }
            else __result = db.Query(__instance.CommandText, Params(__instance)).CreateDataReader();
            return false;
        }

        private static bool NonQueryPrefix(SqlCommand __instance, ref int __result)
        {
            var db = _current;
            if (db == null) return true;
            __result = db.Execute(__instance.CommandText, Params(__instance));
            return false;
        }

        private static bool ScalarPrefix(SqlCommand __instance, ref object? __result)
        {
            var db = _current;
            if (db == null) return true;
            var dt = db.Query(__instance.CommandText, Params(__instance));
            __result = dt.Rows.Count == 0 || dt.Columns.Count == 0 ? null : dt.Rows[0][0];
            return false;
        }

        private static IReadOnlyDictionary<string, object?> Params(SqlCommand cmd)
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (SqlParameter p in cmd.Parameters)
                d[p.ParameterName.TrimStart('@')] = p.Value is DBNull ? null : p.Value;
            return d;
        }

        // ------------------------------------------------------------ جدول‌ها

        internal sealed class Table
        {
            public string Name = "";
            public Dictionary<string, Type> Schema = new(StringComparer.OrdinalIgnoreCase);
            public List<string> Columns = new();
            public List<Dictionary<string, object?>> Rows = new();
            public string[]? PrimaryKey;
            /// <summary>مقدار پیش‌فرض ستون‌هایی که در INSERT نیامده‌اند (مثل DEFAULT (getdate())).</summary>
            public Dictionary<string, Func<object?>> Defaults = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>ستون‌های NOT NULL طبق DDL؛ نقض فقط ثبت می‌شود مگر StrictNotNull.</summary>
            public HashSet<string> NotNull = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>نقض‌های NOT NULL (طبق DDL تعریف‌شده برای جدول) که رخ داده ولی اعمال نشده‌اند.</summary>
        public List<string> DdlViolations { get; } = new();

        /// <summary>
        /// هر استثنایی که FakeDb داده (SQL ناشناخته، ستون ناموجود، …). کد تولید چند جا
        /// استثنای دیتابیس را می‌بلعد (مثلاً RecordFailedInvoiceLocal و استعلام خودکار)؛
        /// این فهرست نمی‌گذارد یک شکل ناشناخته آن‌جا بی‌صدا گم شود.
        /// </summary>
        public List<string> Errors { get; } = new();

        /// <summary>اگر true، نقض NOT NULL مثل SQL Server استثنا می‌دهد.</summary>
        public bool StrictNotNull { get; set; }

        private readonly Dictionary<string, Table> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();
        private long _seq;

        public List<FakeDbCall> Journal { get; } = new();
        public SortedDictionary<string, int> ShapeHits { get; } = new(StringComparer.Ordinal);

        /// <summary>تابعی که پیش از هر نوشتن صدا زده می‌شود؛ اگر پیام برگرداند، نوشتن با آن خطا شکست می‌خورد.</summary>
        public Func<string, IReadOnlyDictionary<string, object?>, string?>? WriteFault { get; set; }

        public Table DefineTable(string name, Type model, string[]? primaryKey = null,
                                 IEnumerable<string>? exclude = null, IDictionary<string, Type>? overrides = null)
        {
            var ex = new HashSet<string>(exclude ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var t = new Table { Name = name, PrimaryKey = primaryKey };
            foreach (var p in model.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite || p.GetIndexParameters().Length > 0 || ex.Contains(p.Name)) continue;
                if (t.Schema.ContainsKey(p.Name)) continue;
                var type = overrides != null && overrides.TryGetValue(p.Name, out var o) ? o : p.PropertyType;
                t.Schema[p.Name] = type;
                t.Columns.Add(p.Name);
            }
            if (overrides != null)
                foreach (var kv in overrides.Where(kv => !t.Schema.ContainsKey(kv.Key)))
                {
                    t.Schema[kv.Key] = kv.Value;
                    t.Columns.Add(kv.Key);
                }
            lock (_gate) _tables[name] = t;
            return t;
        }

        public Table GetTable(string name) =>
            _tables.TryGetValue(name, out var t) ? t : throw new FakeDbException($"Invalid object name 'dbo.{name}'. (FakeDb: جدول تعریف نشده)");

        public bool HasTable(string name) => _tables.ContainsKey(name);

        /// <summary>یک ردیف از روی یک شیء (خواص هم‌نام) درج می‌کند — فقط برای فیکسچر، در Journal نمی‌آید.</summary>
        public void Seed(string table, object row)
        {
            var t = GetTable(table);
            var r = NewRow(t);
            foreach (var p in row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!t.Schema.TryGetValue(p.Name, out var type) || p.GetIndexParameters().Length > 0) continue;
                r[CanonicalColumn(t, p.Name)] = Coerce(p.GetValue(row), type, p.Name);
            }
            lock (_gate) t.Rows.Add(r);
        }

        public IReadOnlyList<Dictionary<string, object?>> Rows(string table)
        {
            lock (_gate) return GetTable(table).Rows.Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        public List<FakeDbCall> Writes()
        {
            lock (_gate) return Journal.Where(j => j.IsWrite).ToList();
        }

        public long Mark() { lock (_gate) return _seq; }

        public List<FakeDbCall> WritesSince(long mark)
        {
            lock (_gate) return Journal.Where(j => j.IsWrite && j.Seq > mark).ToList();
        }

        private static Dictionary<string, object?> NewRow(Table t)
        {
            var r = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in t.Columns) r[c] = null;
            return r;
        }

        private static string CanonicalColumn(Table t, string name)
        {
            foreach (var c in t.Columns)
                if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) return c;
            throw new FakeDbException($"Invalid column name '{name}'. (FakeDb: ستون در طرح جدول {t.Name} نیست)");
        }

        // ------------------------------------------------------------ مسیریاب

        private delegate DataTable QueryHandler(Match m, IReadOnlyDictionary<string, object?> p);
        private delegate int WriteHandler(Match m, IReadOnlyDictionary<string, object?> p);

        private const RegexOptions RX = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

        private static readonly Regex RxDrvMrCorrect = new(
            @"^SELECT (?<cols>.+?) FROM\(SELECT dbo\.HEAD_BACK_ANBAR\.NUMBER1, .+\) AS DRVD_TBL WHERE NUMBER=(?<n>-?\d+) AND DRVD_TBL\.TAG=(?<t>-?\d+)$", RX);
        private static readonly Regex RxDrvDena = new(
            @"^SELECT (?<cols>dbo\.HEAD_LST\.NUMBER, dbo\.HEAD_LST\.TAG, .+?) FROM dbo\.HEAD_LST INNER JOIN dbo\.INVO_LST ON .+ WHERE \(dbo\.HEAD_LST\.NUMBER = (?<n>-?\d+)\) AND \(dbo\.HEAD_LST\.TAG = (?<t>-?\d+)\)$", RX);
        private static readonly Regex RxDepartSub = new(
            @"^SELECT \* FROM dbo\.DEPART WHERE DEPATMAN = \(SELECT DEPATMAN FROM dbo\.HEAD_LST WHERE TAG = (?<t>-?\d+) AND NUMBER = (?<n>-?\d+)\)$", RX);
        private static readonly Regex RxMax = new(
            @"^SELECT (?<isnull>ISNULL\()?MAX\((?<col>\w+)(?<plus> ?\+ ?1)?\)(?(isnull) ?, ?0\) ?\+ ?1|) FROM (?:dbo\.)?(?<table>\w+)(?: WITH \([\w, ]+\))?$", RX);
        private static readonly Regex RxSelect = new(
            @"^SELECT (?:TOP ?\(? ?(?<top>\d+) ?\)? )?(?<cols>.+?) FROM (?:dbo\.)?(?<table>\w+)(?: WHERE (?<where>.+?))?(?: ORDER BY (?<order>.+?))?$", RX);
        private static readonly Regex RxUpdateHle = new(
            @"^UPDATE hle SET hle\.irtaxid = t\.Taxid FROM dbo\.HEAD_LST_EXTENDED hle INNER JOIN dbo\.TAXDTL t ON hle\.NUMBER = t\.NUMBER AND hle\.TGU = t\.TAG WHERE (?<where>.+)$", RX);
        private static readonly Regex RxInsert = new(
            @"^INSERT INTO (?:dbo\.)?(?<table>\w+) ?\((?<cols>[^)]+)\) ?VALUES ?\((?<vals>.+)\)$", RX);
        private static readonly Regex RxUpdate = new(
            @"^UPDATE (?:TOP ?\((?<top>\d+)\) )?(?:dbo\.)?(?<table>\w+) SET (?<set>.+?)(?: WHERE (?<where>.+))?$", RX);
        private static readonly Regex RxDelete = new(
            @"^DELETE FROM (?:dbo\.)?(?<table>\w+) WHERE (?<where>.+)$", RX);

        public static string Normalize(string sql) =>
            Regex.Replace(sql ?? "", @"\s+", " ").Trim().TrimEnd(';').Trim();

        /// <summary>شکل کلی SQL: مقادیر ثابت با ? جایگزین می‌شوند.</summary>
        private static string ShapeOf(string handler, string norm)
        {
            string s = Regex.Replace(norm, @"N?'(?:[^']|'')*'", "?");
            s = Regex.Replace(s, @"(?<![\w@.])-?\d+(?:\.\d+)?(?![\w])", "?");
            if (s.Length > 170) s = s.Substring(0, 120) + " … " + s.Substring(s.Length - 45);
            return handler + " :: " + s;
        }

        public DataTable Query(string sql, IReadOnlyDictionary<string, object?> p)
        {
            try { return QueryCore(sql, p); }
            catch (FakeDbException ex) { lock (_gate) Errors.Add(ex.Message); throw; }
        }

        public int Execute(string sql, IReadOnlyDictionary<string, object?> p)
        {
            try { return ExecuteCore(sql, p); }
            catch (FakeDbException ex) { lock (_gate) Errors.Add(ex.Message); throw; }
        }

        /// <summary>SQL نامعتبری که SQL Server هم با خطای نحوی رد می‌کند (نه «ناشناخته برای FakeDb»).</summary>
        private static readonly Regex RxEmptyComparison = new(@"(=|<>|<|>)\s*$|(=|<>)\s*(AND|OR|ORDER)\b", RX);

        private DataTable QueryCore(string sql, IReadOnlyDictionary<string, object?> p)
        {
            var norm = Normalize(sql);
            if (RxEmptyComparison.IsMatch(norm))
            {
                lock (_gate) Record(false, ShapeOf("SYNTAX-ERROR", norm), sql, p, -1);
                throw new FakeDbException("Incorrect syntax near '='. (FakeDb: مقایسه بدون مقدار — SQL Server هم رد می‌کند)\n" + norm);
            }
            lock (_gate)
            {
                Match m;
                string handler;
                DataTable result;
                if ((m = RxDrvMrCorrect.Match(norm)).Success) { handler = "DRVD_TBL(MrCorrect)"; result = QueryView(m, "m"); }
                else if ((m = RxDrvDena.Match(norm)).Success) { handler = "DRVD_TBL(DenaFaraz)"; result = QueryView(m, "d"); }
                else if ((m = RxDepartSub.Match(norm)).Success) { handler = "DEPART(subquery)"; result = QueryDepartSub(m); }
                else if ((m = RxMax.Match(norm)).Success) { handler = "MAX"; result = QueryMax(m); }
                else if ((m = RxSelect.Match(norm)).Success && _tables.ContainsKey(m.Groups["table"].Value)) { handler = "SELECT"; result = QuerySelect(m, p); }
                else throw Unknown("query", norm);

                Record(false, ShapeOf(handler, norm), sql, p, result.Rows.Count);
                return result;
            }
        }

        private int ExecuteCore(string sql, IReadOnlyDictionary<string, object?> p)
        {
            var norm = Normalize(sql);
            lock (_gate)
            {
                var fault = WriteFault?.Invoke(norm, p);
                Match m;
                string handler;
                Func<int> apply;
                if ((m = RxUpdateHle.Match(norm)).Success) { handler = "UPDATE hle JOIN TAXDTL"; var mm = m; apply = () => UpdateHle(mm, p); }
                else if ((m = RxInsert.Match(norm)).Success) { handler = "INSERT"; var mm = m; apply = () => Insert(mm, p); }
                else if ((m = RxUpdate.Match(norm)).Success) { handler = "UPDATE"; var mm = m; apply = () => Update(mm, p); }
                else if ((m = RxDelete.Match(norm)).Success) { handler = "DELETE"; var mm = m; apply = () => Delete(mm, p); }
                else throw Unknown("write", norm);

                if (fault != null)
                {
                    Record(true, ShapeOf(handler, norm) + " [FAULT]", sql, p, -1);
                    throw new FakeDbException(fault);
                }
                int n = apply();
                Record(true, ShapeOf(handler, norm), sql, p, n);
                return n;
            }
        }

        private void Record(bool write, string shape, string sql, IReadOnlyDictionary<string, object?> p, int affected)
        {
            ShapeHits[shape] = ShapeHits.TryGetValue(shape, out var c) ? c + 1 : 1;
            Journal.Add(new FakeDbCall(++_seq, write, shape, sql,
                new Dictionary<string, object?>(p, StringComparer.OrdinalIgnoreCase), affected));
        }

        private static Exception Unknown(string kind, string norm) =>
            new FakeDbException("FakeDb: شکل SQL ناشناخته (" + kind + ") — پشتیبانی نمی‌شود، عمداً خالی برنگرداندیم:\n" +
                                (norm.Length > 600 ? norm.Substring(0, 600) + " …" : norm));

        // ------------------------------------------------------------ اجرای SELECT

        private DataTable QueryView(Match m, string product)
        {
            var t = GetTable("DRVD_TBL");
            decimal n = decimal.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            decimal tag = decimal.Parse(m.Groups["t"].Value, CultureInfo.InvariantCulture);
            var rows = t.Rows.Where(r => SqlEq(r["NUMBER"], n) == true && SqlEq(r["TAG"], tag) == true
                                         && (r["PRODUCT"] as string ?? product) == product).ToList();
            var cols = SplitTopLevel(m.Groups["cols"].Value, ',').Select(ColumnAlias).ToList();
            return Build(t, rows, cols);
        }

        private static string ColumnAlias(string item)
        {
            item = item.Trim();
            var asm = Regex.Match(item, @"\bAS (\w+)$", RX);
            if (asm.Success) return asm.Groups[1].Value;
            int dot = item.LastIndexOf('.');
            return dot >= 0 ? item.Substring(dot + 1).Trim() : item;
        }

        private DataTable QueryDepartSub(Match m)
        {
            var head = GetTable("HEAD_LST");
            decimal n = decimal.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            decimal tag = decimal.Parse(m.Groups["t"].Value, CultureInfo.InvariantCulture);
            var sub = head.Rows.Where(r => SqlEq(r["TAG"], tag) == true && SqlEq(r["NUMBER"], n) == true)
                          .Select(r => r["DEPATMAN"]).ToList();
            if (sub.Count > 1)
                throw new FakeDbException("Subquery returned more than 1 value. This is not permitted when the subquery follows =");
            var dep = GetTable("DEPART");
            object? key = sub.FirstOrDefault();
            var rows = dep.Rows.Where(r => SqlEq(r["DEPATMAN"], key) == true).ToList();
            return Build(dep, rows, dep.Columns);
        }

        private DataTable QueryMax(Match m)
        {
            var t = GetTable(m.Groups["table"].Value);
            string col = CanonicalColumn(t, m.Groups["col"].Value);
            var vals = t.Rows.Select(r => r[col]).Where(v => v != null).Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).ToList();
            object? max = vals.Count == 0 ? null : vals.Max();
            if (max != null && m.Groups["plus"].Success) max = (decimal)max + 1;
            if (m.Groups["isnull"].Success) max = (max == null ? 0m : (decimal)max) + 1;
            var dt = new DataTable();
            var type = Nullable.GetUnderlyingType(t.Schema[col]) ?? t.Schema[col];
            dt.Columns.Add(new DataColumn("Column1", type) { AllowDBNull = true });
            var row = dt.NewRow();
            row[0] = max == null ? DBNull.Value : Convert.ChangeType(max, type, CultureInfo.InvariantCulture);
            dt.Rows.Add(row);
            return dt;
        }

        private DataTable QuerySelect(Match m, IReadOnlyDictionary<string, object?> p)
        {
            var t = GetTable(m.Groups["table"].Value);
            var colsText = m.Groups["cols"].Value.Trim();
            List<string> cols;
            if (colsText == "*") cols = t.Columns;
            else
            {
                cols = new List<string>();
                foreach (var c in SplitTopLevel(colsText, ','))
                {
                    var name = c.Trim();
                    if (!Regex.IsMatch(name, @"^\w+$"))
                        throw Unknown("select-list", m.Value);
                    cols.Add(CanonicalColumn(t, name));
                }
            }
            var where = m.Groups["where"].Success ? ParseWhere(m.Groups["where"].Value, t, p) : (_ => true);
            IEnumerable<Dictionary<string, object?>> rows = t.Rows.Where(where);
            if (m.Groups["order"].Success) rows = OrderBy(rows, m.Groups["order"].Value, t);
            if (m.Groups["top"].Success) rows = rows.Take(int.Parse(m.Groups["top"].Value, CultureInfo.InvariantCulture));
            return Build(t, rows.ToList(), cols);
        }

        private static IEnumerable<Dictionary<string, object?>> OrderBy(IEnumerable<Dictionary<string, object?>> rows, string order, Table t)
        {
            IOrderedEnumerable<Dictionary<string, object?>>? o = null;
            foreach (var part in SplitTopLevel(order, ','))
            {
                var pm = Regex.Match(part.Trim(), @"^(?:\w+\.)?(?<c>\w+)(?: (?<d>ASC|DESC))?$", RX);
                if (!pm.Success) throw Unknown("order-by", order);
                string col = CanonicalColumn(t, pm.Groups["c"].Value);
                bool desc = pm.Groups["d"].Value.Equals("DESC", StringComparison.OrdinalIgnoreCase);
                Func<Dictionary<string, object?>, object?> key = r => r[col];
                var cmp = Comparer<object?>.Create(SqlCompareForSort);
                o = o == null
                    ? (desc ? rows.OrderByDescending(key, cmp) : rows.OrderBy(key, cmp))
                    : (desc ? o.ThenByDescending(key, cmp) : o.ThenBy(key, cmp));
            }
            return o ?? rows;
        }

        private static DataTable Build(Table t, List<Dictionary<string, object?>> rows, List<string> cols)
        {
            var dt = new DataTable(t.Name);
            var canon = new List<string>();
            foreach (var c in cols)
            {
                var cc = CanonicalColumn(t, c);
                canon.Add(cc);
                var type = Nullable.GetUnderlyingType(t.Schema[cc]) ?? t.Schema[cc];
                if (dt.Columns.Contains(cc)) continue;
                dt.Columns.Add(new DataColumn(cc, type) { AllowDBNull = true });
            }
            foreach (var r in rows)
            {
                var dr = dt.NewRow();
                foreach (var cc in canon.Distinct(StringComparer.OrdinalIgnoreCase))
                    dr[cc] = r[cc] ?? DBNull.Value;
                dt.Rows.Add(dr);
            }
            return dt;
        }

        // ------------------------------------------------------------ WHERE

        private static Func<Dictionary<string, object?>, bool> ParseWhere(string where, Table t, IReadOnlyDictionary<string, object?> p)
        {
            var preds = new List<Func<Dictionary<string, object?>, bool?>>();
            foreach (var raw in SplitTopLevelAnd(where))
            {
                string term = StripParens(raw.Trim());
                Match m;
                if ((m = Regex.Match(term, @"^(?:\w+\.)?(?<c>\w+) IS (?<not>NOT )?NULL$", RX)).Success)
                {
                    string col = CanonicalColumn(t, m.Groups["c"].Value);
                    bool not = m.Groups["not"].Success;
                    preds.Add(r => not ? r[col] != null : r[col] == null);
                }
                else if ((m = Regex.Match(term, @"^ISNULL\( ?(?:\w+\.)?(?<c>\w+) ?, ?(?<d>N?'(?:[^']|'')*'|-?\d+(?:\.\d+)?) ?\) (?<not>NOT )?LIKE (?<pat>N?'(?:[^']|'')*')$", RX)).Success)
                {
                    string col = CanonicalColumn(t, m.Groups["c"].Value);
                    object? dflt = Literal(m.Groups["d"].Value, p);
                    var rx = LikeRegex((string)Literal(m.Groups["pat"].Value, p)!);
                    bool not = m.Groups["not"].Success;
                    preds.Add(r => { var v = Convert.ToString(r[col] ?? dflt, CultureInfo.InvariantCulture) ?? ""; bool hit = rx.IsMatch(v); return not ? !hit : hit; });
                }
                else if ((m = Regex.Match(term, @"^(?<fn>ISNULL\( ?)?(?:\w+\.)?(?<c>\w+)(?(fn) ?, ?(?<d>N?'(?:[^']|'')*'|-?\d+(?:\.\d+)?) ?\)|) ?(?<op>=|<>|!=|>=|<=|>|<) ?(?<v>@\w+|N?'(?:[^']|'')*'|-?\d+(?:\.\d+)?)$", RX)).Success)
                {
                    string col = CanonicalColumn(t, m.Groups["c"].Value);
                    object? dflt = m.Groups["fn"].Success ? Literal(m.Groups["d"].Value, p) : null;
                    object? val = Literal(m.Groups["v"].Value, p);
                    string op = m.Groups["op"].Value;
                    preds.Add(r =>
                    {
                        object? left = r[col] ?? dflt;
                        if (op == "=") return SqlEq(left, val);
                        if (op == "<>" || op == "!=") { var e = SqlEq(left, val); return e == null ? null : !e; }
                        if (left == null || val == null) return null;
                        int c = SqlCompare(left, val);
                        return op switch { ">" => c > 0, "<" => c < 0, ">=" => c >= 0, "<=" => c <= 0, _ => null };
                    });
                }
                else throw Unknown("where-term", term);
            }
            // SQL: شرط باید دقیقاً TRUE باشد؛ NULL (مقایسه با NULL) یعنی رد
            return r => preds.All(f => f(r) == true);
        }

        private static string StripParens(string s)
        {
            while (s.StartsWith("(") && s.EndsWith(")") && MatchingParen(s, 0) == s.Length - 1)
                s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        private static int MatchingParen(string s, int open)
        {
            int depth = 0; bool q = false;
            for (int i = open; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\'') q = !q;
                if (q) continue;
                if (ch == '(') depth++;
                else if (ch == ')' && --depth == 0) return i;
            }
            return -1;
        }

        private static IEnumerable<string> SplitTopLevelAnd(string s)
        {
            var parts = new List<string>();
            int depth = 0, start = 0; bool q = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\'') q = !q;
                if (q) continue;
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (depth == 0 && i + 5 <= s.Length && string.Compare(s, i, " AND ", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    parts.Add(s.Substring(start, i - start));
                    start = i + 5;
                    i += 4;
                }
                else if (depth == 0 && i + 4 <= s.Length && string.Compare(s, i, " OR ", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                    throw Unknown("where-OR", s);
            }
            parts.Add(s.Substring(start));
            return parts;
        }

        private static List<string> SplitTopLevel(string s, char sep)
        {
            var parts = new List<string>();
            int depth = 0, start = 0; bool q = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\'') q = !q;
                if (q) continue;
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (ch == sep && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(s.Substring(start));
            return parts;
        }

        private static Regex LikeRegex(string pattern) =>
            new("^" + Regex.Escape(pattern).Replace("%", ".*").Replace("_", ".") + "$", RX);

        private static object? Literal(string token, IReadOnlyDictionary<string, object?> p)
        {
            token = token.Trim();
            if (token.StartsWith("@"))
            {
                var key = token.Substring(1);
                if (!p.TryGetValue(key, out var v))
                    throw new FakeDbException($"Must declare the scalar variable \"{token}\". (FakeDb: پارامتر داده نشده)");
                return v is DBNull ? null : v;
            }
            if (token.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return null;
            if (token.Equals("GETDATE()", StringComparison.OrdinalIgnoreCase)) return DateTime.Now;
            var sm = Regex.Match(token, @"^N?'(?<s>(?:[^']|'')*)'$", RX);
            if (sm.Success) return sm.Groups["s"].Value.Replace("''", "'");
            if (decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
            throw Unknown("literal", token);
        }

        // ------------------------------------------------------------ مقایسهٔ SQL

        private static bool IsNumeric(object v) => v is sbyte or byte or short or ushort or int or uint or long or ulong
                                                       or float or double or decimal or bool;

        private static decimal ToDec(object v) => v is bool b ? (b ? 1m : 0m) : Convert.ToDecimal(v, CultureInfo.InvariantCulture);

        /// <summary>تساوی به سبک SQL: NULL با هر چیزی NULL است؛ رشته‌ها بی‌حساسیت به حروف و فاصلهٔ انتها (Arabic_CI_AS).</summary>
        internal static bool? SqlEq(object? a, object? b)
        {
            if (a == null || b == null || a is DBNull || b is DBNull) return null;
            return SqlCompare(a, b) == 0;
        }

        private static int SqlCompare(object a, object b)
        {
            if (IsNumeric(a) && IsNumeric(b)) return ToDec(a).CompareTo(ToDec(b));
            if (a is DateTime da && b is DateTime db2) return da.CompareTo(db2);
            if (IsNumeric(a) && b is string sb) return ToDec(a).CompareTo(ParseSqlNumber(sb));
            if (a is string sa && IsNumeric(b)) return ParseSqlNumber(sa).CompareTo(ToDec(b));
            return string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture)?.TrimEnd(),
                                  Convert.ToString(b, CultureInfo.InvariantCulture)?.TrimEnd(),
                                  StringComparison.OrdinalIgnoreCase);
        }

        private static decimal ParseSqlNumber(string s) =>
            decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                ? d
                : throw new FakeDbException($"Conversion failed when converting the varchar value '{s}' to data type int.");

        private static int SqlCompareForSort(object? a, object? b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;       // SQL Server: NULL اول در ASC
            if (b == null) return 1;
            return SqlCompare(a, b);
        }

        /// <summary>تبدیل مقدار به نوع ستون، مثل تبدیل ضمنی SQL Server هنگام درج.</summary>
        internal static object? Coerce(object? v, Type target, string column)
        {
            if (v == null || v is DBNull) return null;
            var u = Nullable.GetUnderlyingType(target) ?? target;
            if (u.IsInstanceOfType(v)) return v;
            try
            {
                if (u == typeof(string)) return Convert.ToString(v, CultureInfo.InvariantCulture);
                if (u == typeof(bool))
                {
                    if (v is string s) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                    return ToDec(v) != 0;
                }
                if (u == typeof(DateTime)) return Convert.ToDateTime(v, CultureInfo.InvariantCulture);
                if (v is string str) return Convert.ChangeType(ParseSqlNumber(str), u, CultureInfo.InvariantCulture);
                if (v is bool bb) return Convert.ChangeType(bb ? 1 : 0, u, CultureInfo.InvariantCulture);
                return Convert.ChangeType(v, u, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is not FakeDbException)
            {
                throw new FakeDbException($"FakeDb: تبدیل مقدار «{v}» ({v.GetType().Name}) به نوع ستون {column} ({u.Name}) ممکن نیست: {ex.Message}");
            }
        }

        // ------------------------------------------------------------ نوشتن

        private int Insert(Match m, IReadOnlyDictionary<string, object?> p)
        {
            var t = GetTable(m.Groups["table"].Value);
            var cols = m.Groups["cols"].Value.Split(',').Select(c => c.Trim()).ToList();
            var vals = SplitTopLevel(m.Groups["vals"].Value, ',').Select(v => v.Trim()).ToList();
            if (cols.Count != vals.Count)
                throw new FakeDbException($"There are more columns in the INSERT statement than values specified in the VALUES clause. ({cols.Count} ستون / {vals.Count} مقدار)");
            var row = NewRow(t);
            var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cols.Count; i++)
            {
                var cc = CanonicalColumn(t, cols[i]);
                row[cc] = Coerce(Literal(vals[i], p), t.Schema[cc], cc);
                given.Add(cc);
            }
            foreach (var d in t.Defaults)
                if (!given.Contains(d.Key)) row[CanonicalColumn(t, d.Key)] = Coerce(d.Value(), t.Schema[CanonicalColumn(t, d.Key)], d.Key);
            var nulls = t.NotNull.Where(c => row[CanonicalColumn(t, c)] == null).ToList();
            if (nulls.Count > 0)
            {
                var msg = $"Cannot insert the value NULL into column(s) '{string.Join("', '", nulls)}', table 'dbo.{t.Name}'";
                if (StrictNotNull) throw new FakeDbException(msg + "; column does not allow nulls. INSERT fails.");
                DdlViolations.Add(msg + " ← " + Regex.Replace(m.Groups["cols"].Value, @"\s+", " ").Trim());
            }
            if (t.PrimaryKey != null &&
                t.Rows.Any(r => t.PrimaryKey.All(k => SqlEq(r[k], row[k]) == true)))
                throw new FakeDbException($"Violation of PRIMARY KEY constraint 'PK_{t.Name}'. Cannot insert duplicate key in object 'dbo.{t.Name}'.");
            t.Rows.Add(row);
            return 1;
        }

        private int Update(Match m, IReadOnlyDictionary<string, object?> p)
        {
            var t = GetTable(m.Groups["table"].Value);
            var sets = new List<(string col, string expr)>();
            foreach (var a in SplitTopLevel(m.Groups["set"].Value, ','))
            {
                var am = Regex.Match(a.Trim(), @"^(?:\w+\.)?(?<c>\w+) ?= ?(?<v>.+)$", RX);
                if (!am.Success) throw Unknown("set", a);
                sets.Add((CanonicalColumn(t, am.Groups["c"].Value), am.Groups["v"].Value.Trim()));
            }
            var where = m.Groups["where"].Success ? ParseWhere(m.Groups["where"].Value, t, p) : (_ => true);
            IEnumerable<Dictionary<string, object?>> target = t.Rows.Where(where).ToList();
            if (m.Groups["top"].Success) target = target.Take(int.Parse(m.Groups["top"].Value, CultureInfo.InvariantCulture));
            int n = 0;
            foreach (var r in target)
            {
                foreach (var (col, expr) in sets)
                {
                    object? v = Regex.IsMatch(expr, @"^\w+$", RX) && t.Schema.ContainsKey(expr) && !expr.Equals("NULL", StringComparison.OrdinalIgnoreCase)
                        ? r[CanonicalColumn(t, expr)]
                        : Literal(expr, p);
                    r[col] = Coerce(v, t.Schema[col], col);
                }
                n++;
            }
            return n;
        }

        private int Delete(Match m, IReadOnlyDictionary<string, object?> p)
        {
            var t = GetTable(m.Groups["table"].Value);
            var where = ParseWhere(m.Groups["where"].Value, t, p);
            return t.Rows.RemoveAll(r => where(r));
        }

        private int UpdateHle(Match m, IReadOnlyDictionary<string, object?> p)
        {
            var hle = GetTable("HEAD_LST_EXTENDED");
            var tax = GetTable("TAXDTL");
            var where = ParseWhere(m.Groups["where"].Value, tax, p);
            int n = 0;
            foreach (var h in hle.Rows)
            {
                // UPDATE … FROM … JOIN: اگر چند ردیف t جفت شوند، SQL Server یکی را (نامعین) برمی‌دارد؛
                // همهٔ ردیف‌های یک صورتحساب Taxid یکسان دارند.
                var match = tax.Rows.FirstOrDefault(t => where(t)
                                                        && SqlEq(h["NUMBER"], t["NUMBER"]) == true
                                                        && SqlEq(h["TGU"], t["TAG"]) == true);
                if (match == null) continue;
                h["irtaxid"] = Coerce(match["Taxid"], hle.Schema["irtaxid"], "irtaxid");
                n++;
            }
            return n;
        }
    }
}
