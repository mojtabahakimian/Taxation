using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Headless;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۳۲ — «ارسالِ مجدد» ِ بی‌پنجره (ResendRunner) و گروه ۳۳ — «صورتحسابِ اصلاحی/ابطالی» ِ بی‌پنجره (CorrectionRunner).
///
/// ResendRunner متنِ RESEND_BTN_Click را کپی کرده؛ ۳۲-۱ متنِ سه قطعه‌اش را (بی توضیح و فاصله) با خودِ پنجره مقایسه
/// می‌کند تا اگر یکی عوض شد و دیگری نه، قرمز شود — هارنسِ ویندوزی Roslyn ندارد که مثلِ ۲۹ پنجره را کامپایل کند.
/// </summary>
internal static class HeadlessFixPath
{
    private const int SendTag = 77;
    private const int HeadTag = 88;
    private const long FirstNumber = 1_000_001;

    public static void Run(CL_CCNNMANAGER db, string cs, string mockBaseUrl, HttpClient http, Action<string, bool, string> check)
    {
        SourceParity(check);

        var target = db.DoGetDataSQL<double>(
            $"SELECT NUMBER FROM dbo.HEAD_LST WHERE TAG={HeadTag} AND NUMBER>={FirstNumber} ORDER BY NUMBER").Select(x => (long)x).Skip(3).FirstOrDefault();
        if (target == 0) { check("۳۲-۰ فاکتورِ تستی هست", false, ""); return; }

        // ---------------- ۳۲: ارسالِ مجدد ----------------
        Reset(http, mockBaseUrl);
        db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}");
        var s1 = SingleSendRunner.Run(new SingleSendRunner.Request { ConnectionString = cs, Number = target, Tag = SendTag, TaxUrlOverride = mockBaseUrl, Confirm = _ => true });
        if (!s1.Sent) { check("۳۲-۲ ارسالِ اولیه برای ارسالِ مجدد", false, s1.Error ?? ""); return; }
        var taxid = s1.TaxId!;
        var original = JsonNode.Parse(Payloads(http, mockBaseUrl)[taxid]!.ToJsonString())!;   // net6: DeepClone ندارد
        long rowsBefore = Count(db, $"Taxid=N'{taxid}'");
        db.DoExecuteSQL($"UPDATE dbo.TAXDTL SET TheStatus=N'FAILED' WHERE Taxid=N'{taxid}'");

        check("۳۲-۲ قاعده‌ی دکمه: فقط PENDING / IN_PROGRESS / FAILED (UNKNOWN و SUCCESS نه)",
              ResendRunner.Resendable("pending") && ResendRunner.Resendable("IN_PROGRESS") && ResendRunner.Resendable("FAILED")
              && !ResendRunner.Resendable("UNKNOWN") && !ResendRunner.Resendable("SUCCESS") && !ResendRunner.Resendable(null), "");

        Reset(http, mockBaseUrl);
        var r = ResendRunner.Run(cs, false, new[] { taxid }, "t32", mockBaseUrl);
        var sent = Payloads(http, mockBaseUrl);
        var again = sent.TryGetValue(taxid, out var p) ? p : null;
        check("۳۲-۳ ارسالِ مجدد: همان taxid، inno، تاریخ‌ها و مبالغ؛ صورتحسابِ تازه ساخته نشد",
              r.Error == null && r.Items.Single().Sent && sent.Count == 1 && again != null
              && new[] { "taxid", "inno", "indatim", "indati2m", "tbill", "tvam", "tinb", "tob" }
                    .All(k => again["header"]![k]?.ToJsonString() == original["header"]![k]?.ToJsonString())
              && again["body"]!.AsArray().Count == original["body"]!.AsArray().Count,
              $"خطا={r.Error ?? r.Items.FirstOrDefault()?.Error} ارسال={sent.Count}");

        long dup = Count(db, $"Taxid=N'{taxid}' AND REMARKS LIKE N'ResendDuplicate|t32|%' AND TheStatus=N'PENDING' AND NUMBER={target} AND TAG={SendTag}");
        check("۳۲-۴ ردیف‌های تازه: همان تعداد، ResendDuplicate، PENDING، با NUMBER و TAG (گروه ۲۴)",
              dup == rowsBefore, $"{dup} از {rowsBefore}");

        // ---------------- ۳۳: اصلاحی و ابطالی ----------------
        db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE Taxid=N'{taxid}' AND REMARKS LIKE N'ResendDuplicate%'");
        db.DoExecuteSQL($"UPDATE dbo.TAXDTL SET TheStatus=N'SUCCESS', TheSuccess=1 WHERE Taxid=N'{taxid}'");

        var draft = CorrectionRunner.Load(cs, taxid, false);
        check("۳۳-۱ بارگذاری: ردیف‌های مرجع، Irtaxid = شماره‌ی مرجع و Taxid خالی",
              draft != null && draft.Rows.Count == rowsBefore && draft.Rows.All(x => x.Irtaxid == taxid && x.Taxid == null)
              && draft.ReferenceIndatim > 0 && draft.ReferenceTaxId == taxid,
              $"{draft?.Rows.Count} ردیف");
        if (draft == null) return;

        var am0 = draft.Rows[0].Am ?? 0;
        draft.Rows[0].Am = Math.Max(1, Math.Truncate(am0 / 2));
        var chk = CorrectionRunner.Validate(cs, draft, 2, null);
        check("۳۳-۲ اعتبارسنجیِ اصلاحی: بی‌خطا؛ همه‌ی ردیف‌ها Ins=2 و جمع‌ها با اقلام می‌خوانند",
              chk.Errors.Count == 0 && draft.Rows.All(x => x.Ins == 2)
              && draft.Rows[0].Tbill == draft.Rows.Sum(x => x.Tsstam ?? 0),
              string.Join(" | ", chk.Errors.Concat(chk.Warnings)));

        Reset(http, mockBaseUrl);
        var c1 = CorrectionRunner.Send(cs, false, draft, null, mockBaseUrl);
        var corr = c1.TaxId is { } t1 && Payloads(http, mockBaseUrl).TryGetValue(t1, out var cp) ? cp : null;
        check("۳۳-۳ ارسالِ اصلاحی: ins=2، مرجع = شماره‌ی اول، شماره‌ی مالیاتیِ تازه، مقدارِ قلمِ اول عوض شد",
              c1.Sent && corr != null && corr["header"]!["ins"]!.GetValue<int>() == 2
              && corr["header"]!["irtaxid"]!.GetValue<string>() == taxid
              && c1.TaxId!.Length == 22 && c1.TaxId != taxid
              && corr["body"]!.AsArray().Count == draft.Rows.Count
              && corr["body"]![0]!["am"]!.GetValue<decimal>() == draft.Rows[0].Am,
              $"Sent={c1.Sent} خطا={c1.Error} taxid={c1.TaxId}");
        long corrRows = Count(db, $"Taxid=N'{c1.TaxId}' AND Ins=2 AND TheStatus=N'PENDING' AND Irtaxid=N'{taxid}' AND NUMBER={target}");
        check("۳۳-۴ ردیف‌های اصلاحی در یک تراکنش با شماره‌ی تازه، PENDING و مرجع ثبت شدند",
              corrRows == draft.Rows.Count, $"{corrRows} ردیف");

        var ebt = CorrectionRunner.Load(cs, taxid, false)!;
        var w1 = CorrectionRunner.Validate(cs, ebt, 3, null);
        Reset(http, mockBaseUrl);
        var c2 = CorrectionRunner.Send(cs, false, ebt, null, mockBaseUrl);
        var ebp = c2.TaxId is { } t2 && Payloads(http, mockBaseUrl).TryGetValue(t2, out var ep) ? ep : null;
        check("۳۳-۵ ابطالی: بدنه ندارد (ص۱۷)، ins=3 و مرجع همان",
              w1.Errors.Count == 0 && c2.Sent && ebp != null && ebp["header"]!["ins"]!.GetValue<int>() == 3
              && ebp["header"]!["irtaxid"]!.GetValue<string>() == taxid && (ebp["body"]?.AsArray().Count ?? 0) == 0,
              $"Sent={c2.Sent} خطا={c2.Error} بدنه={ebp?["body"]?.AsArray().Count}");

        var again3 = CorrectionRunner.Validate(cs, CorrectionRunner.Load(cs, taxid, false)!, 3, null);
        check("۳۳-۶ ابطالیِ دوم روی همان مرجع: هشدارِ ص۱۵ (هشدار، نه مانع)",
              again3.Errors.Count == 0 && again3.Warnings.Any(w => w.Contains("فقط می‌تواند مرجع یک ابطالی")), string.Join(" | ", again3.Warnings));

        var bad = CorrectionRunner.Load(cs, taxid, false)!;
        bad.Rows[0].Inno = " ";
        var badCheck = CorrectionRunner.Validate(cs, bad, 2, null);
        var noIns = CorrectionRunner.Validate(cs, CorrectionRunner.Load(cs, taxid, false)!, 0, null);
        check("۳۳-۷ خطاها مانع‌اند: سریالِ خالی و موضوعِ انتخاب‌نشده",
              badCheck.Errors.Any(e => e.Contains("Inno")) && noIns.Errors.Count > 0, string.Join(" | ", badCheck.Errors.Concat(noIns.Errors)));

        var early = CorrectionRunner.Load(cs, taxid, false)!;
        var earlyCheck = CorrectionRunner.Validate(cs, early, 2,
            DateTimeOffset.FromUnixTimeMilliseconds(early.ReferenceIndatim).UtcDateTime.AddDays(-3));
        check("۳۳-۸ تاریخِ سفارشیِ زودتر از مرجع: هشدارِ ص۲۵",
              earlyCheck.Warnings.Any(w => w.Contains("ص۲۵")), string.Join(" | ", earlyCheck.Warnings));
        check("۳۳-۹ بعد از همه، رشته‌ی اتصالِ میزبان پاک است",
              CL_CCNNMANAGER.ConnectionOverride == null, CL_CCNNMANAGER.ConnectionOverride ?? "");
    }

    /// <summary>متنِ سه قطعه‌ی ارسالِ مجدد در ResendRunner با Prg_TrackSentInvoice/MainWindow.xaml.cs.</summary>
    private static void SourceParity(Action<string, bool, string> check)
    {
        var root = RepoRoot();
        if (root == null) { check("۳۲-۱ سورسِ پنجره و ResendRunner پیدا شد", false, ""); return; }
        var wpf = File.ReadAllText(Path.Combine(root, "Prg_TrackSentInvoice", "MainWindow.xaml.cs"));
        var core = File.ReadAllText(Path.Combine(root, "Prg_Moadian", "Headless", "ResendRunner.cs"));

        var diffs = new List<string>();
        foreach (var name in new[] { "CreateHeaderFromFullTaxDtl", "CreateBodyListFromFullTaxDtl" })
            if (Norm(Body(wpf, name)) != Norm(Body(core, name))) diffs.Add(name);

        var region = Regex.Match(wpf, @"#region Cleaning_RestoreValiding(.*?)#endregion", RegexOptions.Singleline).Groups[1].Value;
        if (Norm(region) != Norm(Body(core, "Clean"))) diffs.Add("Cleaning_RestoreValiding");

        check("۳۲-۱ متنِ ساختِ سربرگ، اقلام و پاک‌سازیِ ResendRunner همان RESEND_BTN ِ پنجره است",
              diffs.Count == 0, string.Join("، ", diffs));
    }

    /// <summary>بدنه‌ی یک متد (بینِ آکولادِ اول و جفتش) با اسمش.</summary>
    private static string Body(string src, string method)
    {
        var m = Regex.Match(src, @"\b" + method + @"\s*\([^)]*\)\s*\{");
        if (!m.Success) return "<" + method + " نیست>";
        int depth = 0, start = m.Index + m.Length - 1;
        for (int i = start; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start + 1, i - start - 1);
        }
        return "<" + method + " ناقص>";
    }

    /// <summary>بی توضیح، بی فاصله، بی پیشوندِ TaxModel.</summary>
    private static string Norm(string s)
    {
        s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
        s = Regex.Replace(s, @"//[^\n]*", "");
        s = s.Replace("TaxModel.", "");
        return Regex.Replace(s, @"\s+", "");
    }

    private static string? RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "TaxationApp.sln"))) d = d.Parent;
        return d?.FullName;
    }

    private static long Count(CL_CCNNMANAGER db, string where) =>
        db.DoGetDataSQL<long>($"SELECT COUNT(*) FROM dbo.TAXDTL WHERE {where}").First();

    private static Dictionary<string, JsonNode> Payloads(HttpClient http, string baseUrl)
    {
        var json = http.GetStringAsync(Root(baseUrl) + "/__payloads").GetAwaiter().GetResult();
        return JsonNode.Parse(json)!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

    private static void Reset(HttpClient http, string baseUrl) =>
        http.PostAsync(Root(baseUrl) + "/__reset", new StringContent("{}", System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();

    private static string Root(string baseUrl) => baseUrl.TrimEnd('/').Replace("/sandbox", "");
}
