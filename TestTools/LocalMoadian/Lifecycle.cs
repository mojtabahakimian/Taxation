using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۲۸ — چرخهٔ کامل صورتحساب روی «سرور مؤدیان کامل» (moadian_mock_full.py).
///
/// تفاوتش با گروه‌های ۸ تا ۱۵:
///   • سرور یک «کارپوشه» دارد: خریدار تایید/رد می‌کند، ۳۰ روز می‌گذرد و تایید
///     سیستمی می‌شود، ارجاعیِ تاییدشده مرجعش را باطل می‌کند.
///   • کدهای خطا از ساختار ۷ رقمی سند «کد خطاهای سامانه مودیان» (EC_V02) ساخته
///     می‌شوند، نه از یک فهرست دلبخواه.
///   • مبالغ ردیف را *کد تولید* می‌سازد (CorrectionMath.RecalculateRow)، cap/insp را
///     CalculateCapInsp مسیر گروهی، و ارسال با TaxService.SendInvoices است. ولی
///     سرجمع‌های سرصفحه را خودِ این تست جمع می‌زند (Recalc) و سازندهٔ سرصفحهٔ مسیر
///     گروهی/تکی اصلاً اجرا نمی‌شود — آن‌ها دیتابیس می‌خواهند (گروه‌های ۱۹، ۲۱، ۲۲).
///     نکتهٔ باز: CalculateCapInsp برای نقدی cap = tbill می‌دهد، ولی golden/*.json
///     (مسیر گروهی) cap = 0 نشان می‌دهد. سرور کامل cap نقدی را کنترل نمی‌کند، پس این
///     گروه این تفاوت را نمی‌بیند.
///
/// ⚠️ قید مهم (CLAUDE.md بخش ۳ و ۱۰):
///   سرور را از روی اسناد نوشته‌ایم. انتظارِ هر تست هم از همان اسناد آمده و
///   منبعش در نام تست آمده است. پس سبز بودن این گروه یعنی «کد تولید با خوانشِ
///   ما از اسناد می‌خواند» — نه اینکه سامانهٔ واقعی همین رفتار را دارد.
///   نام همهٔ تست‌ها با «سرور کامل:» شروع می‌شود تا با گروه ۲۷ (حکم واقعی) خلط نشود.
///
///   خانه‌هایی که سند در آن‌ها دوپهلوست، assert نمی‌شوند؛ فقط گزارش می‌شوند
///   (بخش «ابهام‌ها» در انتهای اجرا).
/// </summary>
internal static class Lifecycle
{
    private const string MemoryId = "A11216";
    private const string Seller = "11111111111";

    private static TaxService _tax = null!;
    private static string _base = "";
    private static HttpClient _http = null!;
    private static Action<string, bool, string> _check = null!;
    private static Action<string> _line = null!;
    private static int _n;
    private static readonly List<string> Ambiguities = new();

    public static void Run(TaxService tax, string baseUrl, HttpClient http,
                           Action<string, bool, string> check, Action<string> line)
    {
        _tax = tax; _base = baseUrl.TrimEnd('/') + "/"; _http = http;
        _check = check; _line = line; _n = 1;
        Ambiguities.Clear();

        Post("__reset", new { });

        A_Originals();
        B_ReferralOnOriginalInEveryState();
        C_ReferralOnReferral();
        D_Cancellation();
        E_SalesReturn();
        F_Correction();
        G_ReferenceSingleUse();
        H_Dates();
        I_Settlement();
        J_WarningsNeverBlock();
        K_Resend();
        L_KnownDivergences();

        if (Ambiguities.Count > 0)
        {
            _line("");
            _line("  ابهام‌های سند (assert نشدند، فقط گزارش):");
            foreach (var a in Ambiguities) _line("    ? " + a);
        }
    }

    // ================================================================ A
    // اصلی: نوع × روش تسویه × نوع شخص خریدار، با تعداد اعشاری و تخفیف

    private static void A_Originals()
    {
        _line("  — A. صورتحساب اصلی: نوع × تسویه × نوع شخص");
        foreach (int inty in new[] { 1, 2 })
        foreach (int setm in new[] { 1, 2, 3 })
        foreach (int tob in new[] { 1, 2, 3, 4 })
        {
            var inv = Original(inty, setm, tob);
            var r = Send(inv);
            string expect = inty == 1 ? "AWAITING_REACTION" : "NO_NEED_REACTION";
            string st = r.Ok ? Dash(r.Taxid) : "";
            Check($"سرور کامل: اصلی نوع {inty} / {SetmName(setm)} / {TobName(tob)} پذیرفته و «{Fa(expect)}» (FAQ 3-1، ص۳۵)",
                  r.Ok && st == expect, $"{r.Codes} {st}");
        }

        // نوع اول: اعتبارسنجی پیش از ارسالِ کد تولید (MoadianRules.ValidateBuyer)
        // باید با حکم سرور هم‌نظر باشد — در هر دو جهت.
        var cases = new (int tob, string? tinb, string? bid, string? bpc, string label)[]
        {
            (1, "00123456790001", null, null,         "حقیقی با شماره اقتصادی ۱۴ رقمی"),
            (1, "0012345679",     null, null,         "حقیقی با شماره اقتصادی ۱۰ رقمی"),
            (1, null,             "0012345679", "1234567890", "حقیقی با کد ملی + کد پستی"),
            (1, null,             null, null,         "حقیقی بی‌هویت"),
            (2, "10100302746",    null, null,         "حقوقی ۱۱ رقمی"),
            (2, "101003027460",   null, null,         "حقوقی ۱۲ رقمی"),
            (2, null,             null, null,         "حقوقی بدون شماره اقتصادی"),
            (2, "0912-345-678",   null, null,         "حقوقی با تلفن در فیلد (مثل ۹ فقرهٔ مروارید)"),
            (3, "10100302746",    null, null,         "مشارکت مدنی ۱۱ رقمی"),
            (4, "12345678901201", null, null,         "اتباع ۱۴ رقمی"),
            (4, null,             "123456789012", "1234567890", "اتباع با کد فراگیر + کد پستی"),
            (4, null,             "1234567890",   "1234567890", "اتباع با کد فراگیر ۱۰ رقمی"),
        };
        foreach (var c in cases)
        {
            var inv = Original(1, 1, c.tob);
            inv.H.Tinb = c.tinb; inv.H.Bid = c.bid; inv.H.Bpc = c.bpc;
            bool appOk = MoadianRules.ValidateBuyer(1, 1, c.tob, c.tinb, c.bid, c.bpc, out _);
            var r = Send(inv);
            Check($"سرور کامل: پیش‌اعتبارسنجی خریدار با حکم سرور هم‌نظر است — {c.label}",
                  appOk == r.Ok, $"برنامه={(appOk ? "قبول" : "رد")} سرور={(r.Ok ? "قبول" : "رد")} {r.Codes}");
        }

        // سه کد واقعی مروارید باید دقیقاً همان‌جا تولید شوند (رمزگشایی ساختار EC_V02)
        var empty = Original(1, 1, 2); empty.H.Tinb = null;
        var rEmpty = Send(empty);
        Check("سرور کامل: شماره اقتصادی خالیِ حقوقی → 00012 (EC_V02: خطا/وجودی/فیلد ۰۱۲)",
              !rEmpty.Ok && rEmpty.Has("00012"), rEmpty.Codes);
        var regex = Original(1, 1, 2); regex.H.Tinb = "123456789012";
        var rRegex = Send(regex);
        Check("سرور کامل: شماره اقتصادی ۱۲ رقمیِ حقوقی → 0101204 (خطا/ساختار/فیلد ۰۱۲/الگو)",
              !rRegex.Ok && rRegex.Has("0101204"), rRegex.Codes);

        // ریاضیات برش: تعداد اعشاری و مبلغ واحد نامُدوّر
        var frac = Original(1, 1, 2, lines: new[]
        {
            L("2820000000001", 2.5m, 333_333m, 7m, 10m),
            L("2820000000002", 0.3333m, 1_000_001m, 0m, 9m),
            L("2820000000003", 7m, 142_857m, 100_000m, 9m),
            L("2820000000004", 1.75m, 100_001m, 0m, 9m),     // ۱۷۵٬۰۰۱٫۷۵ — برش و گرد فرق دارند
        });
        var rFrac = Send(frac);
        Check("سرور کامل: سه قلمِ اعشاری با برشِ CorrectionMath پذیرفته می‌شود (FAQ 9-4)",
              rFrac.Ok, rFrac.Codes);

        // قلمِ مبلغ صفر: دادهٔ عملیاتی ۵۸ فقره 0103705 + 0104105. InvoiceValidator کد تولید
        // باید همین را پیش از ارسال بگیرد.
        var zero = Original(1, 1, 2, lines: new[] { L("2820000000001", 1m, 100_000m, 0m, 10m),
                                                     L("2820000000005", 2m, 0m, 0m, 10m) });
        var vZero = InvoiceValidator.Validate(zero.H, zero.B);
        var rZero = Send(zero);
        Check("سرور کامل: قلمِ مبلغ واحد صفر → 0103705 و 0104105؛ InvoiceValidator هم پیش از ارسال می‌گیرد (دادهٔ عملیاتی ۵۸)",
              !rZero.Ok && rZero.Has("0103705") && rZero.Has("0104105") && vZero.Errors.Any(e => e.Contains("مبلغ واحد")),
              $"سرور={rZero.Codes} validator={vZero.Errors.Count} خطا");

        // همان محاسبه با گرد کردن به‌جای برش باید رد شود — تستِ خرابکاری.
        // ۲٫۵ × ۳۳۳٬۳۳۵ = ۸۳۳٬۳۳۷٫۵ : برش ۸۳۳٬۳۳۷ ، گرد (بانکی) ۸۳۳٬۳۳۸.
        var rounded = Original(1, 1, 2, lines: new[] { L("2820000000001", 2.5m, 333_335m, 0m, 10m) });
        foreach (var b in rounded.B) { b.Prdis = Math.Round(b.Am * b.Fee); b.Adis = b.Prdis - b.Dis;
                                       b.Vam = Math.Round(b.Adis * b.Vra / 100m); b.Tsstam = b.Adis + b.Vam; }
        Recalc(rounded, keepTsstam: true);
        var rRound = Send(rounded);
        Check("سرور کامل: همان اقلام با Math.Round رد می‌شود (برش است، نه گرد کردن)",
              !rRound.Ok, rRound.Codes);
    }

    // ================================================================ B
    // ارجاعی روی اصلی، در هر وضعیت کارپوشه

    private static void B_ReferralOnOriginalInEveryState()
    {
        _line("  — B. ارجاعی روی صورتحساب اصلی، در هر وضعیت مرجع");
        var states = new (string name, int inty, Action<string> drive)[]
        {
            ("AWAITING_REACTION", 1, _ => { }),
            ("APPROVED",          1, t => Buyer(t, "approve")),
            ("REJECTED",          1, t => Buyer(t, "reject")),
            ("SYSTEMIC_APPROVED", 1, t => Advance(31)),
            ("NO_NEED_REACTION",  2, _ => { }),
        };

        foreach (var s in states)
        foreach (int ins in new[] { 2, 3, 4 })
        {
            Post("__reset", new { });
            var orig = Original(s.inty, 1, 2);
            var r0 = Send(orig);
            if (!r0.Ok) { Check($"سرور کامل: مرجع برای {InsName(ins)}/{s.name}", false, r0.Codes); continue; }
            s.drive(r0.Taxid);
            string st = Dash(r0.Taxid);

            var refl = Referral(ins, orig, r0.Taxid);
            var r = Send(refl);
            // ص۱۶ بند ۳ و FAQ 4-1: مرجعِ اصلی در «هر وضعیت» قابل ارجاع است؛ شرط وضعیت
            // فقط وقتی است که مرجع خودش اصلاحی/برگشتی باشد.
            Check($"سرور کامل: {InsName(ins)} روی اصلیِ «{Fa(s.name)}» پذیرفته می‌شود (FAQ 4-1، 4-9؛ V7.9 ص۱۶)",
                  st == s.name && r.Ok, $"وضعیت مرجع={st} {r.Codes}");

            if (r.Ok && ins == 3)
                Check($"سرور کامل: بعد از ابطالی، مرجعِ «{Fa(s.name)}» باطل شده است (ص۳۵)",
                      Dash(r0.Taxid) == "CANCELED", Dash(r0.Taxid));
        }
    }

    // ================================================================ C
    // ارجاعی روی ارجاعی — شرط وضعیت (FAQ 4-3 نکته ۳)

    private static void C_ReferralOnReferral()
    {
        _line("  — C. اصلاحی/برگشتی روی اصلاحی/برگشتی (زنجیره)");
        var states = new (string name, bool allowed, Action<string> drive)[]
        {
            ("AWAITING_REACTION", false, _ => { }),
            ("REJECTED",          false, t => Buyer(t, "reject")),
            ("APPROVED",          true,  t => Buyer(t, "approve")),
            ("SYSTEMIC_APPROVED", true,  _ => Advance(31)),
        };

        foreach (int first in new[] { 2, 4 })
        foreach (int second in new[] { 2, 4 })
        foreach (var s in states)
        {
            Post("__reset", new { });
            var orig = Original(1, 1, 2, lines: TwoLines());
            var r0 = Send(orig);
            var mid = Referral(first, orig, r0.Taxid);
            var r1 = Send(mid);
            if (!r0.Ok || !r1.Ok) { Check($"سرور کامل: زنجیره {InsName(first)}→{InsName(second)}", false, r0.Codes + " | " + r1.Codes); continue; }
            s.drive(r1.Taxid);

            var last = Referral(second, mid, r1.Taxid);
            var r2 = Send(last);
            Check($"سرور کامل: {InsName(second)} روی {InsName(first)}ِ «{Fa(s.name)}» " +
                  (s.allowed ? "پذیرفته" : "با 0300601 رد") + " می‌شود (FAQ 4-3 نکته ۳؛ EC_V02 ص۳۳)",
                  s.allowed ? r2.Ok : (!r2.Ok && r2.Has("0300601")), r2.Codes);
        }

        // ۳۰ روز بعد، همان ارجاعیِ رد شده به خاطر «در انتظار واکنش»، پذیرفته می‌شود
        Post("__reset", new { });
        var o = Original(1, 1, 2, lines: TwoLines()); var ro = Send(o);
        var c1 = Referral(2, o, ro.Taxid); var rc1 = Send(c1);
        var early = Send(Referral(2, c1, rc1.Taxid));
        Advance(31);
        var late = Send(Referral(2, c1, rc1.Taxid));
        Check("سرور کامل: اصلاحی روی اصلاحیِ منتظر رد، و پس از ۳۰ روز (تایید سیستمی) پذیرفته (FAQ 11-2)",
              !early.Ok && late.Ok, $"{early.Codes} → {late.Codes}");

        // ارجاعیِ تاییدشده، مرجعش را باطل می‌کند: دیگر نمی‌شود روی اصلی ارجاع داد
        Post("__reset", new { });
        var o2 = Original(1, 1, 2, lines: TwoLines()); var r2o = Send(o2);
        var c2 = Referral(2, o2, r2o.Taxid); var r2c = Send(c2);
        Buyer(r2c.Taxid, "approve");
        Check("سرور کامل: با تایید اصلاحی، صورتحساب اصلی «باطل شده» تلقی می‌شود (FAQ ص۳۵)",
              Dash(r2o.Taxid) == "CANCELED", Dash(r2o.Taxid));
        var again = Send(Referral(4, o2, r2o.Taxid));
        Check("سرور کامل: برگشتی روی اصلیِ مصرف‌شده با 0300601 رد می‌شود (FAQ 4-9)",
              !again.Ok && again.Has("0300601"), again.Codes);
        var onTip = Send(Referral(4, c2, r2c.Taxid));
        Check("سرور کامل: برگشتی روی آخرین حلقه (اصلاحیِ تاییدشده) پذیرفته می‌شود (FAQ 4-3 نکته ۲، 4-10)",
              onTip.Ok, onTip.Codes);
    }

    // ================================================================ D
    // ابطالی (FAQ 4-5، 4-7، 4-9، 4-11؛ V7.9 ص۱۷)

    private static void D_Cancellation()
    {
        _line("  — D. ابطالی");
        Post("__reset", new { });

        // ابطالی بدون بدنه و بدون مبالغ — مسیر تولید body خالی می‌فرستد
        var o = Original(1, 2, 2); var ro = Send(o);
        var cancel = Referral(3, o, ro.Taxid);
        var rc = Send(cancel);
        Check("سرور کامل: ابطالی بدون اقلام پذیرفته می‌شود (V7.9 ص۱۷؛ FAQ 4-7)",
              rc.Ok && Payload(rc.Taxid)?["body"] is JsonArray { Count: 0 }, rc.Codes);
        Check("سرور کامل: صورتحساب ابطالی خودش «عدم نیاز به واکنش» است",
              Dash(rc.Taxid) == "NO_NEED_REACTION", Dash(rc.Taxid));

        var dup = Send(Referral(3, o, ro.Taxid));
        Check("سرور کامل: ابطالِ دوباره همان مرجع با 0300601 رد می‌شود (EC_V02 ص۳۳)",
              !dup.Ok && dup.Has("0300601"), dup.Codes);

        var onCancel = Send(Referral(3, cancel, rc.Taxid));
        Check("سرور کامل: ابطالیِ روی ابطالی رد می‌شود (FAQ 4-11؛ V7.9 ص۱۷ بند ۴-۲)",
              !onCancel.Ok && onCancel.Has("0300601"), onCancel.Codes);

        var corrOnCancelled = Send(Referral(2, o, ro.Taxid));
        Check("سرور کامل: اصلاحی روی مرجعِ ابطال‌شده رد می‌شود",
              !corrOnCancelled.Ok && corrOnCancelled.Has("0300601"), corrOnCancelled.Codes);

        // FAQ 4-9: ابطالیِ «ارجاعیِ در انتظار واکنش» مجاز است
        Post("__reset", new { });
        var o2 = Original(1, 1, 2, lines: TwoLines()); var r2 = Send(o2);
        var c = Referral(2, o2, r2.Taxid); var rcc = Send(c);
        var cancelReferral = Send(Referral(3, c, rcc.Taxid));
        Check("سرور کامل: ابطالِ اصلاحیِ «در انتظار واکنش» پذیرفته می‌شود (FAQ 4-9)",
              cancelReferral.Ok, cancelReferral.Codes);

        // ابهام: ابطالِ اصلاحیِ «تاییدشده». FAQ 4-9 می‌گوید ابطالی روی «ارجاعیِ در انتظار
        // واکنش»؛ ولی FAQ 4-1 همان اصلاحیِ تاییدشده را «مرجع» می‌داند و مرجع «با هر وضعیت»
        // ابطال‌پذیر است. سند دو جواب می‌دهد → فقط گزارش.
        Post("__reset", new { });
        var o3 = Original(1, 1, 2, lines: TwoLines()); var r3 = Send(o3);
        var c3 = Referral(2, o3, r3.Taxid); var r3c = Send(c3);
        Buyer(r3c.Taxid, "approve");
        var cancelApproved = Send(Referral(3, c3, r3c.Taxid));
        Ambiguity($"ابطالِ اصلاحیِ تاییدشده: سرور کامل «{(cancelApproved.Ok ? "پذیرفت" : "رد کرد " + cancelApproved.Codes)}» " +
                  "— FAQ 4-9 (فقط ارجاعیِ منتظر) در برابر FAQ 4-1 (اصلاحیِ تاییدشده = مرجع با هر وضعیت)");

        // فروشنده باید همان فروشندهٔ مرجع باشد
        Post("__reset", new { });
        var o4 = Original(1, 1, 2); var r4 = Send(o4);
        var other = Referral(3, o4, r4.Taxid); other.H.Tins = "22222222222";
        var r4o = Send(other);
        Check("سرور کامل: ابطالی با فروشندهٔ دیگر → 0300902 (EC_V02 جدول ۷ ردیف ۱۶)",
              !r4o.Ok && r4o.Has("0300902"), r4o.Codes);
    }

    // ================================================================ E
    // برگشت از فروش (FAQ 4-4 نکات ۱ تا ۵)

    private static void E_SalesReturn()
    {
        _line("  — E. برگشت از فروش");

        foreach (int inty in new[] { 1, 2 })
        foreach (int setm in new[] { 1, 2, 3 })
        {
            Post("__reset", new { });
            var o = Original(inty, setm, 2, lines: TwoLines()); var ro = Send(o);
            var ret = Referral(4, o, ro.Taxid, rows => rows[0].Am -= 1);
            var r = Send(ret);
            Check($"سرور کامل: برگشتیِ جزئی، نوع {inty} / {SetmName(setm)} پذیرفته می‌شود — مبالغ از CorrectionMath",
                  ro.Ok && r.Ok, ro.Codes + " | " + r.Codes);
        }

        var bad = new (string label, string code, Action<List<TAXDTL>> edit)[]
        {
            ("افزایش تعداد",            "0303601", rows => rows[0].Am += 1),
            ("بدون کاهش",               "0303601", _ => { }),
            ("تغییر نرخ مالیات",         "0304401", rows => { rows[0].Vra = 5; rows[1].Am -= 1; }),
            ("تغییر مبلغ واحد",          "0303701", rows => { rows[0].Fee -= 1000; rows[1].Am -= 1; }),
            ("برگشت همهٔ اقلام",         "0303602", rows => { foreach (var x in rows) x.Am = 0; }),
        };
        foreach (var b in bad)
        {
            Post("__reset", new { });
            var o = Original(1, 1, 2, lines: TwoLines()); var ro = Send(o);
            var r = Send(Referral(4, o, ro.Taxid, b.edit));
            Check($"سرور کامل: برگشتی با «{b.label}» → {b.code} (FAQ 4-4)",
                  !r.Ok && r.Has(b.code), r.Codes);
        }

        // برگشتیِ دوم باید روی شمارهٔ برگشتیِ اول بخورد، نه اصلی (FAQ 4-10)
        Post("__reset", new { });
        var oo = Original(1, 1, 2, lines: TwoLines()); var roo = Send(oo);
        var ret1 = Referral(4, oo, roo.Taxid, rows => rows[0].Am -= 1); var rr1 = Send(ret1);
        Buyer(rr1.Taxid, "approve");
        var onOriginal = Send(Referral(4, oo, roo.Taxid, rows => rows[0].Am -= 2));
        var onReturn = Send(Referral(4, ret1, rr1.Taxid, rows => rows[1].Am -= 1));
        Check("سرور کامل: برگشتیِ دوم روی اصلی رد و روی برگشتیِ اول پذیرفته می‌شود (FAQ 4-10)",
              !onOriginal.Ok && onReturn.Ok, $"{onOriginal.Codes} | {onReturn.Codes}");

        // اقلام اطلاعاتی خریدار در برگشتی تغییرناپذیر است
        Post("__reset", new { });
        var ob = Original(1, 1, 2, lines: TwoLines()); var rob = Send(ob);
        var changedBuyer = Referral(4, ob, rob.Taxid, rows => rows[0].Am -= 1);
        changedBuyer.H.Tinb = "10100302747";
        var rcb = Send(changedBuyer);
        Check("سرور کامل: برگشتی با شماره اقتصادی خریدارِ دیگر → 0301201 (FAQ 4-4 نکته ۱)",
              !rcb.Ok && rcb.Has("0301201"), rcb.Codes);
    }

    // ================================================================ F
    // اصلاحی (FAQ 4-3، 4-6)

    private static void F_Correction()
    {
        _line("  — F. اصلاحی");

        Post("__reset", new { });
        var o = Original(1, 2, 2, lines: TwoLines()); var ro = Send(o);
        var up = Send(Referral(2, o, ro.Taxid, rows => rows[0].Am += 3));
        Check("سرور کامل: اصلاحی با افزایش تعداد پذیرفته می‌شود (FAQ 4-6)", up.Ok, up.Codes);

        Post("__reset", new { });
        o = Original(1, 1, 2, lines: TwoLines()); ro = Send(o);
        var disc = Send(Referral(2, o, ro.Taxid, rows => rows[1].Dis = 12_345));
        Check("سرور کامل: اصلاحیِ تخفیف پذیرفته و مبالغ با CorrectionMath سازگار است", disc.Ok, disc.Codes);

        Post("__reset", new { });
        o = Original(1, 1, 2, lines: TwoLines()); ro = Send(o);
        var newItem = Send(Referral(2, o, ro.Taxid, rows => rows.Add(Row("2820000000099", 1, 50_000, 0, 9))));
        Check("سرور کامل: اصلاحی با شناسه کالای جدید → 0303301 (FAQ 4-6)",
              !newItem.Ok && newItem.Has("0303301"), newItem.Codes);

        foreach (var (field, code, mut) in new (string, string, Action<Draft>)[]
                 {
                     ("نوع شخص خریدار",        "0301001", d => { d.H.Tob = 3; }),
                     ("شماره اقتصادی خریدار",   "0301201", d => { d.H.Tinb = "10100302747"; }),
                     ("نوع صورتحساب",           "0300401", d => { d.H.Inty = 2; }),
                     ("الگوی صورتحساب",         "0300701", d => { d.H.Inp = 2; }),
                 })
        {
            Post("__reset", new { });
            o = Original(1, 1, 2, lines: TwoLines()); ro = Send(o);
            var c = Referral(2, o, ro.Taxid); mut(c);
            var r = Send(c);
            Check($"سرور کامل: اصلاحیِ «{field}» → {code} (باید ابطال و صدور مجدد شود — FAQ 4-3 نکته ۱)",
                  !r.Ok && r.Has(code), r.Codes);
        }
    }

    // ================================================================ G
    // FAQ 4-10: «از هر شماره مالیاتی مرجع تنها یک بار استفاده می‌شود»

    private static void G_ReferenceSingleUse()
    {
        _line("  — G. یک‌بار مصرف بودن شماره مرجع (FAQ 4-10)");
        Post("__reset", new { });
        var o = Original(1, 1, 2, lines: TwoLines()); var ro = Send(o);
        var first = Send(Referral(2, o, ro.Taxid, rows => rows[0].Am += 1));
        var second = Send(Referral(2, o, ro.Taxid, rows => rows[0].Am += 2));
        Check("سرور کامل: اصلاحیِ دوم روی همان مرجع با 0300601 رد می‌شود (FAQ 4-10)",
              first.Ok && !second.Ok && second.Has("0300601"), $"{first.Codes} | {second.Codes}");

        // ارجاعیِ ردشده «مصرف» حساب نمی‌شود — تلاش بعدی آزاد است
        Post("__reset", new { });
        o = Original(1, 1, 2, lines: TwoLines()); ro = Send(o);
        var broken = Referral(2, o, ro.Taxid); broken.H.Tbill += 5;
        var rb = Send(broken);
        var retry = Send(Referral(2, o, ro.Taxid));
        Check("سرور کامل: ارجاعیِ ردشده مرجع را مصرف نمی‌کند؛ تلاش درست بعدی پذیرفته می‌شود",
              !rb.Ok && retry.Ok, $"{rb.Codes} | {retry.Codes}");

        // اصلاحیِ رد شده توسط خریدار: FAQ 11-9 می‌گوید فروشنده می‌تواند با همان مرجع
        // ارجاعیِ دیگری صادر کند. ولی FAQ 4-10 شماره مرجع را یک‌بار مصرف می‌داند → ابهام.
        Post("__reset", new { });
        o = Original(1, 1, 2, lines: TwoLines()); ro = Send(o);
        var cc = Referral(2, o, ro.Taxid); var rcc = Send(cc);
        Buyer(rcc.Taxid, "reject");
        var afterReject = Send(Referral(2, o, ro.Taxid, rows => rows[0].Am += 1));
        Ambiguity($"اصلاحیِ دوم روی اصلی وقتی اصلاحیِ اول را خریدار رد کرده: سرور کامل «{(afterReject.Ok ? "پذیرفت" : "رد کرد " + afterReject.Codes)}» " +
                  "— FAQ 4-10 (یک‌بار مصرف) در برابر FAQ 4-9/11-9 (مرجع با هر وضعیت)");
    }

    // ================================================================ H
    // تاریخ: مهلت ۱۲ روزه، ساعتِ جلوتر از سرور، اصلاحیِ پیش از مرجع (EC_V02 ص۳۳)

    private static void H_Dates()
    {
        _line("  — H. تاریخ و مهلت (02002)");
        Post("__reset", new { });
        long server = ServerNow();

        var d11 = Original(1, 1, 2, issuedMs: server - 11L * 86_400_000);
        var r11 = Send(d11);
        Check("سرور کامل: صدورِ ۱۱ روز پیش پذیرفته می‌شود", r11.Ok, r11.Codes);

        // مرز دقیق assert نمی‌شود: FAQ 9-2 می‌گوید ۱۲ روز، ولی دادهٔ واقعی (CLAUDE.md بخش ۵،
        // ۵۳۴ فاکتور نوع اول) پذیرش تا ۱۳ و رد از ۱۴ روز را نشان می‌دهد. پس فقط دو سوی
        // امنِ مرز assert می‌شود (۱۱ و ۱۵) و روز ۱۳ به عنوان ابهام گزارش می‌شود.
        var d15 = Original(1, 1, 2, issuedMs: server - 15L * 86_400_000);
        var r15 = Send(d15);
        Check("سرور کامل: صدورِ ۱۵ روز پیش → 02002 (بیرون از مهلت، هم طبق FAQ 9-2 هم دادهٔ واقعی)",
              !r15.Ok && r15.Has("02002"), r15.Codes);

        // دادهٔ عملیاتی: ۵۲ فاکتورِ خارج از مهلت با 0200201 + 00107 («قاعده ارسال خالی است»)
        // رد شدند. کد تولید با AutoArticle9 = false هیچ‌وقت insr نمی‌فرستد، پس این همان
        // سرنوشتِ هر فاکتورِ دیرکردی است — رفتارِ عمدی (CLAUDE.md بخش ۱)، اینجا فقط ثبت می‌شود.
        var late = Original(1, 1, 2, issuedMs: server - 15L * 86_400_000);
        var lateSrv = DateTimeOffset.FromUnixTimeMilliseconds(server).UtcDateTime;
        late.H.Insr = MoadianRules.ResolveInsr(lateSrv.AddDays(-15), lateSrv);        // ← کد تولید
        var rLate = Send(late);
        Check("سرور کامل: فاکتورِ دیرکرد با insr کد تولید (null) → 02002 + 00107 (دادهٔ عملیاتی ۵۲ فقره)",
              late.H.Insr == null && !rLate.Ok && rLate.Has("02002") && rLate.Has("00107"),
              $"insr={late.H.Insr?.ToString() ?? "null"} {rLate.Codes}");

        var d13 = Original(1, 1, 2, issuedMs: server - 13L * 86_400_000);
        var r13 = Send(d13);
        Ambiguity($"صدورِ ۱۳ روز پیش: سرور کامل «{(r13.Ok ? "پذیرفت" : "رد کرد " + r13.Codes)}» — " +
                  "FAQ 9-2 (۱۲ روز) در برابر دادهٔ واقعی نوع اول (پذیرش تا ۱۳ روز، CLAUDE.md بخش ۵)");

        // MoadianRules باید همین مرز را بشناسد (فقط هشدار می‌دهد، بلاک نمی‌کند)
        var srvDt = DateTimeOffset.FromUnixTimeMilliseconds(server).UtcDateTime;
        Check("سرور کامل: MoadianRules.IsArticle9 مرز ۱۲ روز را با سرور هم‌جهت می‌بیند",
              !MoadianRules.IsArticle9(srvDt.AddDays(-11), srvDt) && MoadianRules.IsArticle9(srvDt.AddDays(-13), srvDt), "");

        var ahead = Original(1, 1, 2, issuedMs: server + 120_000);
        var rAhead = Send(ahead);
        Check("سرور کامل: indatim دو دقیقه جلوتر از ساعت سرور → 02002 (EC_V02 ص۳۳: «بعد از زمان سرور»)",
              !rAhead.Ok && rAhead.Has("02002"), rAhead.Codes);

        // همان payload، وقتی ساعت سرور به آن رسید، پذیرفته می‌شود — تنها توضیحِ سازگار
        // با «ابطالیِ 02002 که دو دقیقه بعد با payload یکسان پذیرفته شد» (CLAUDE.md بخش ۶).
        Post("__config", new { clock_offset_ms = 180_000 });
        var ahead2 = Original(1, 1, 2, issuedMs: server + 120_000);
        var rLater = Send(ahead2);
        Post("__config", new { clock_offset_ms = 0 });
        Check("سرور کامل: همان indatim پس از جلو رفتن ساعت سرور پذیرفته می‌شود (فرضیهٔ 02002 گذرا)",
              rLater.Ok, rLater.Codes);

        // اصلاحیِ با تاریخِ پیش از مرجع
        Post("__reset", new { });
        server = ServerNow();
        var o = Original(1, 1, 2, lines: TwoLines(), issuedMs: server - 60_000); var ro = Send(o);
        var early = Referral(2, o, ro.Taxid); early.H.Indatim = server - 3_600_000; early.H.Indati2m = early.H.Indatim;
        early.H.Taxid = _tax.RequestTaxId(MemoryId, FromMs(early.H.Indatim));
        var re = Send(early);
        Check("سرور کامل: اصلاحی با تاریخ صدورِ پیش از مرجع → 02002 (EC_V02 ص۳۳)",
              !re.Ok && re.Has("02002"), re.Codes);
    }

    // ================================================================ I
    // روش تسویه — cap/insp از CalculateCapInsp مسیر گروهی

    private static void I_Settlement()
    {
        _line("  — I. روش تسویه (cap/insp از CalculateCapInsp)");
        Post("__reset", new { });

        foreach (decimal share in new[] { 0.01m, 0.5m, 0.99m })
        {
            var inv = Original(1, 3, 2, lines: TwoLines(), capShare: share);
            var r = Send(inv);
            Check($"سرور کامل: نقدی/نسیه با سهم نقدی {share:P0} — cap+insp = tbill−tvam−todam (FAQ 10-5/10-6)",
                  r.Ok, $"cap={inv.H.Cap} insp={inv.H.Insp} {r.Codes}");
        }

        var wrong = Original(1, 3, 2, lines: TwoLines(), capShare: 0.5m); wrong.H.Insp += 1;
        var rw = Send(wrong);
        Check("سرور کامل: نقدی/نسیه با یک ریال اختلاف → 02029",
              !rw.Ok && rw.Has("02029"), rw.Codes);

        var noInsp = Original(1, 3, 2, capShare: 0.5m); noInsp.H.Insp = 0;
        var rn = Send(noInsp);
        Check("سرور کامل: نقدی/نسیه با نسیهٔ صفر رد می‌شود",
              !rn.Ok, rn.Codes);
    }

    // ================================================================ J
    // هشدار هرگز بلاک نیست (CLAUDE.md بخش ۰)

    private static void J_WarningsNeverBlock()
    {
        _line("  — J. هشدار (رقم اول ۱) ثبت را متوقف نمی‌کند");
        Post("__reset", new { });

        // دادهٔ عملیاتی: هر ۱۴٬۶۴۱ صورتحساب موفق هشدار داشتند؛ 14800 روی ۱۰۰٪ (کد تولید
        // همیشه یک InvoiceExtension خالی می‌فرستد) و 1300501 روی ۹۹٪ (سریال داخل taxid
        // تصادفی است — Random.Shared در RequestTaxId — ولی inno شمارهٔ فاکتور است).
        var o = Original(1, 1, 2, lines: TwoLines()); var ro = Send(o);
        Check("سرور کامل: اصلیِ عادی پذیرفته می‌شود با هشدارهای 14800 و 1300501 و بدون هیچ خطا (دادهٔ عملیاتی ۱۴٬۶۴۱ فقره)",
              ro.Ok && ro.Errors.Count == 0 && ro.Has("14800") && ro.Has("1300501"), ro.Codes);

        // cap/insp برای نقدی/نسیه «خارج از الگو»ست: فقط هشدار 14029/14030، نه خطا.
        // همین تفاوت cap=tbill (CalculateCapInsp) و cap=0 (golden) را بی‌اثر می‌کند.
        var cash = Original(1, 1, 2);
        var rc = Send(cash);
        Check($"سرور کامل: نقدی با cap={cash.H.Cap} پذیرفته می‌شود و cap فقط هشدار 14029 دارد (دادهٔ عملیاتی ۱۴٬۵۰۵)",
              rc.Ok && rc.Has("14029") && !rc.Errors.Any(e => e.StartsWith("02029")), rc.Codes);
    }

    // ================================================================ K
    // ارسال مجدد با همان taxid (CLAUDE.md بخش ۴ — RESEND_BTN)

    private static void K_Resend()
    {
        _line("  — K. ارسال مجدد با همان شماره مالیاتی");
        Post("__reset", new { });
        var o = Original(1, 1, 2); var r1 = Send(o);
        var r2 = Send(o);
        Check("سرور کامل: ارسالِ دوبارهٔ عینِ همان صورتحساب → 0300101 و فقط یک نسخه در کارپوشه",
              r1.Ok && !r2.Ok && r2.Has("0300101") && CountDash() == 1, $"{r2.Codes} n={CountDash()}");
    }

    // ================================================================ L
    // واگرایی‌های شناخته‌شده — دیده‌بان، نه حکم

    private static void L_KnownDivergences()
    {
        _line("  — L. واگرایی‌های ثبت‌شده (دیده‌بان)");

        // ۱. عوارض: کد تولید tsstam = adis + vam (بدون odam). سند: + odam + olam.
        Post("__reset", new { });
        var withOdam = Original(1, 1, 2);
        foreach (var b in withOdam.B) { b.Odam = 1_000; }
        Recalc(withOdam, keepTsstam: true);
        var r = Send(withOdam);
        Check("سرور کامل: [واگرایی ۱ CLAUDE.md] با odam غیرصفر، tsstamِ کد تولید (بی‌عوارض) رد می‌شود → 02059",
              !r.Ok && r.Has("02059"), r.Codes);

        // ۲. تحمل ۵ دقیقه‌ای InvoiceValidator برای تاریخِ آینده، در حالی که سند هیچ
        // تحملی نمی‌گوید. این تست رفتار *فعلی* را قفل می‌کند؛ اگر روزی تحمل حذف شد،
        // قرمز می‌شود و باید آگاهانه به‌روز شود.
        long server = ServerNow();
        var h = Original(1, 1, 2, issuedMs: server + 120_000);
        var v = InvoiceValidator.Validate(h.H, h.B);
        bool appSilent = !v.Errors.Any(e => e.Contains("02002"));
        var rs = Send(h);
        Check("سرور کامل: [یافته] indatim +۲ دقیقه: InvoiceValidator ساکت است ولی سرور 02002 می‌دهد",
              appSilent && !rs.Ok && rs.Has("02002"),
              $"validator={(appSilent ? "ساکت" : string.Join("/", v.Errors))} سرور={rs.Codes}");
    }

    // ================================================================ ساخت صورتحساب

    internal sealed class Draft
    {
        public TaxModel.InvoiceModel.Header H = null!;
        public List<TaxModel.InvoiceModel.Body> B = null!;
        public List<TAXDTL> Rows = null!;
    }

    private static TAXDTL Row(string sstid, decimal am, decimal fee, decimal dis, decimal vra)
    {
        var t = new TAXDTL { Sstid = sstid, Am = am, Fee = fee, Dis = dis, Vra = vra, Odam = 0 };
        CorrectionMath.RecalculateRow(t);                          // ← کد تولید
        return t;
    }

    private static TAXDTL L(string sstid, decimal am, decimal fee, decimal dis, decimal vra) =>
        Row(sstid, am, fee, dis, vra);

    private static TAXDTL[] TwoLines() => new[]
    {
        // ۵٫۷۵ × ۱۲۳٬۴۵۷ = ۷۰۹٬۸۷۷٫۷۵ → برش ۷۰۹٬۸۷۷ ، گرد ۷۰۹٬۸۷۸. کسرِ بالای نیم و غیرِ
        // مساوی عمدی است: با کسر ۰٫۵ گرد کردنِ بانکیِ .NET همان برش را می‌دهد و
        // خرابکاریِ Truncate→Round دیده نمی‌شد (این اتفاق افتاد — همین‌جا کشف شد).
        L("2820000000011", 5.75m, 123_457m, 1_000m, 10m),
        L("2820000000012", 3m, 99_999m, 0m, 9m),
    };

    private static Draft Original(int inty, int setm, int tob, TAXDTL[]? lines = null,
                                  long? issuedMs = null, decimal capShare = 0.4m)
    {
        long when = issuedMs ?? ServerNow() - 60_000;
        var rows = (lines ?? new[] { L("2820000000001", 10m, 100_000m, 0m, 10m) })
                   .Select(Clone).ToList();

        var h = new TaxModel.InvoiceModel.Header
        {
            Taxid = _tax.RequestTaxId(MemoryId, FromMs(when)),
            Indatim = when,
            Indati2m = when,
            Inty = inty,
            Inno = new CL_FUNTIONS().GenerateFixedLengthInno("1405", Random.Shared.Next(1, 900_000)),
            Inp = 1,
            Ins = 1,
            Tins = Seller,
            Tob = tob,
            Setm = setm,
        };
        if (inty == 1)
        {
            switch (tob)
            {
                case 1: h.Tinb = "00123456790001"; break;
                case 2: h.Tinb = "10100302746"; break;
                case 3: h.Tinb = "10100302746"; break;
                case 4: h.Tinb = "12345678901201"; break;
            }
        }
        var d = new Draft { H = h, Rows = rows };
        Recalc(d, capShare: capShare);
        return d;
    }

    /// <summary>
    /// ارجاعی از روی یک پیش‌نویس: «کلیه اقلام اطلاعاتی صورتحساب مرجع» (FAQ 4-8) +
    /// irtaxid + شمارهٔ مالیاتیِ تازه. ویرایش ردیف‌ها روی کپی و بعد بازمحاسبه با
    /// CorrectionMath، یعنی دقیقاً کاری که فرم اصلاحی می‌کند.
    /// </summary>
    private static Draft Referral(int ins, Draft reference, string refTaxid,
                                  Action<List<TAXDTL>>? edit = null)
    {
        long when = Math.Max(reference.H.Indatim + 1_000, ServerNow() - 30_000);
        var rows = reference.Rows.Select(Clone).ToList();
        // برگشتی بدون کاهش تعداد، خودش نامعتبر است (FAQ 4-4 نکته ۲) — پیش‌فرضِ
        // برگشتی یک واحد از قلم اول کم می‌کند تا فقط قاعدهٔ موردِ تست فعال شود.
        if (edit == null && ins == 4) edit = rs => rs[0].Am -= 1;
        edit?.Invoke(rows);
        rows = rows.Where(r => (r.Am ?? 0) > 0).ToList();
        foreach (var r in rows) CorrectionMath.RecalculateRow(r);   // ← کد تولید

        var src = reference.H;
        var h = new TaxModel.InvoiceModel.Header
        {
            Taxid = _tax.RequestTaxId(MemoryId, FromMs(when)),
            Indatim = when, Indati2m = when,
            Inty = src.Inty, Inno = src.Inno, Inp = src.Inp, Ins = ins, Irtaxid = refTaxid,
            Tins = src.Tins, Tob = src.Tob, Tinb = src.Tinb, Bid = src.Bid, Bpc = src.Bpc,
            Setm = src.Setm,
        };
        var d = new Draft { H = h, Rows = rows };
        if (ins == 3)
        {
            // V7.9 ص۱۷: اقلام ابطالی از مرجع واکشی می‌شود — بدنه و مبالغ نمی‌فرستیم.
            d.Rows = new List<TAXDTL>();
            d.B = new List<TaxModel.InvoiceModel.Body>();
            return d;
        }
        Recalc(d);
        return d;
    }

    private static void Recalc(Draft d, bool keepTsstam = false, decimal capShare = 0.4m)
    {
        if (d.B == null || !keepTsstam)
        {
            d.B = d.Rows.Select(r => new TaxModel.InvoiceModel.Body
            {
                Sstid = r.Sstid, Sstt = "کالای آزمایشی", Mu = "1627",
                Am = r.Am ?? 0, Fee = r.Fee ?? 0, Prdis = r.Prdis ?? 0, Dis = r.Dis ?? 0,
                Adis = r.Adis ?? 0, Vra = r.Vra ?? 0, Vam = r.Vam ?? 0,
                Odam = r.Odam ?? 0, Tsstam = r.Tsstam ?? 0,
            }).ToList();
        }
        // سرجمع‌ها از روی بدنه‌ای که واقعاً فرستاده می‌شود
        d.H.Tprdis = d.B.Sum(b => b.Prdis);
        d.H.Tdis = d.B.Sum(b => b.Dis);
        d.H.Tadis = d.B.Sum(b => b.Adis);
        d.H.Tvam = d.B.Sum(b => b.Vam);
        d.H.Todam = d.B.Sum(b => b.Odam + b.Olam);
        d.H.Tbill = d.B.Sum(b => b.Tsstam);

        decimal? capIn = d.H.Setm == 3
            ? Math.Truncate((d.H.Tbill - d.H.Tvam - d.H.Todam) * capShare)
            : null;
        var (cap, insp, err) = CapInsp(d.H.Setm, d.H.Tbill, capIn, d.H.Inty, d.H.Tvam, d.H.Todam);
        d.H.Cap = cap ?? 0; d.H.Insp = insp ?? 0;
        if (err != null) _line($"      (CalculateCapInsp: {err})");
    }

    private static TAXDTL Clone(TAXDTL t) => new()
    {
        Sstid = t.Sstid, Am = t.Am, Fee = t.Fee, Dis = t.Dis, Vra = t.Vra, Odam = t.Odam,
        Prdis = t.Prdis, Adis = t.Adis, Vam = t.Vam, Tsstam = t.Tsstam,
    };

    /// <summary>CalculateCapInsp مسیر گروهی (private) — بدون دست زدن به کد اصلی.</summary>
    private static (decimal? cap, decimal? insp, string? err) CapInsp(
        int setm, decimal tbill, decimal? cap, int inty, decimal tvam, decimal todam)
    {
        var type = typeof(Prg_Moadian.Bulk.SendInvoiceBulk);
        var m = type.GetMethod("CalculateCapInsp",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var inst = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var res = m.Invoke(inst, new object?[] { setm, tbill, cap, inty, tvam, todam })!;
        var rt = res.GetType();
        return ((decimal?)rt.GetField("Item1")!.GetValue(res),
                (decimal?)rt.GetField("Item2")!.GetValue(res),
                (string?)rt.GetField("Item3")!.GetValue(res));
    }

    // ================================================================ ارسال و استعلام

    private sealed record Result(bool Ok, string Taxid, List<string> Errors, List<string> Warnings)
    {
        public string Codes => string.Join(",", Errors.Concat(Warnings));
        /// <summary>
        /// مقایسه با ۵ رقم اول (نوع + نوع اعتبارسنجی + شمارهٔ فیلد). سند EC_V02 نمونهٔ
        /// «02002» را ۵ رقمی نشان می‌دهد ولی سامانهٔ زنده «0200201» می‌فرستد (۱۵۴ فقره در
        /// دادهٔ عملیاتی). جزئیاتِ دو رقم آخر در تست‌ها حکم نیست.
        /// </summary>
        public bool Has(string code) => Errors.Concat(Warnings).Any(c =>
            c == code || (code.Length == 5 && c.Length == 7 && c.StartsWith(code, StringComparison.Ordinal)));
    }

    private static Result Send(Draft d)
    {
        try
        {
            var resp = _tax.SendInvoices(d.H, d.B, new List<TaxModel.InvoiceModel.Payment>());  // ← کد تولید
            return Lookup(resp.ReferenceNumber, d.H.Taxid!);
        }
        catch (Exception ex)
        {
            return new Result(false, d.H.Taxid ?? "", new List<string> { "EXCEPTION:" + ex.Message }, new());
        }
    }

    private static Result Lookup(string reference, string taxid)
    {
        using var doc = JsonDocument.Parse(Get("__state"));
        foreach (var bucket in new[] { "invoices", "rejected" })
        {
            if (!doc.RootElement.TryGetProperty(bucket, out var obj)) continue;
            foreach (var p in obj.EnumerateObject())
            {
                if (p.Value.GetProperty("reference").GetString() != reference) continue;
                var errs = p.Value.GetProperty("errors").EnumerateArray()
                            .Select(e => e.GetProperty("code").GetString()!).ToList();
                var warns = p.Value.GetProperty("warnings").EnumerateArray()
                            .Select(e => e.GetProperty("code").GetString()!).ToList();
                return new Result(p.Value.GetProperty("success").GetBoolean(), taxid, errs, warns);
            }
        }
        return new Result(false, taxid, new List<string> { "(بدون پاسخ)" }, new());
    }

    private static string Dash(string taxid)
    {
        var arr = JsonNode.Parse(Get("__invoice_status?taxIds=" + Uri.EscapeDataString(taxid)))?.AsArray();
        return arr?.FirstOrDefault()?["invoiceStatus"]?.GetValue<string>() ?? "?";
    }

    private static int CountDash()
    {
        var node = JsonNode.Parse(Get("__state"))?["invoices"]?.AsObject();
        return node?.Count ?? -1;
    }

    private static JsonNode? Payload(string taxid) => JsonNode.Parse(Get("__payloads"))?[taxid];

    private static void Buyer(string taxid, string action)
    {
        var res = Post("__buyer_action", new { taxid, action });
        if (!(res?["ok"]?.GetValue<bool>() ?? false))
            _line($"      (اقدام خریدار «{action}» روی {taxid} ناموفق: {res?.ToJsonString()})");
    }

    private static void Advance(int days) => Post("__advance_days", new { days });

    private static long ServerNow() => JsonNode.Parse(Get("__clock"))!["serverTime"]!.GetValue<long>();

    private static string Get(string path) =>
        _http.GetStringAsync(_base + path).GetAwaiter().GetResult();

    private static JsonNode? Post(string path, object body)
    {
        var resp = _http.PostAsJsonAsync(_base + path, body).GetAwaiter().GetResult();
        var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        try { return JsonNode.Parse(text); } catch { return null; }
    }

    // ================================================================ کمکی

    private static DateTime FromMs(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(new TimeSpan(3, 30, 0)).DateTime;

    private static void Check(string name, bool ok, string detail) =>
        _check($"۲۸-{Fa(_n++)} {name}", ok, detail);

    private static void Ambiguity(string text) => Ambiguities.Add(text);

    private static string Fa(int n) => string.Concat(n.ToString().Select(c => (char)('۰' + (c - '0'))));

    private static string Fa(string status) => status switch
    {
        "AWAITING_REACTION" => "در انتظار واکنش",
        "APPROVED" => "تایید شده",
        "REJECTED" => "رد شده",
        "SYSTEMIC_APPROVED" => "تایید سیستمی",
        "NO_NEED_REACTION" => "عدم نیاز به واکنش",
        "IMPOSSIBLE_REACTION" => "عدم امکان واکنش",
        "CANCELED" => "باطل شده",
        _ => status,
    };

    private static string SetmName(int s) => s switch { 1 => "نقدی", 2 => "نسیه", 3 => "نقدی/نسیه", _ => s.ToString() };
    private static string TobName(int t) => t switch { 1 => "حقیقی", 2 => "حقوقی", 3 => "مشارکت مدنی", 4 => "اتباع", _ => t.ToString() };
    private static string InsName(int i) => i switch { 1 => "اصلی", 2 => "اصلاحی", 3 => "ابطالی", 4 => "برگشتی", _ => i.ToString() };
}
