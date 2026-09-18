using System.Reflection;
using System.Text.Json;
using TaxCollectData.Library.Dto.Content;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۱۷ — سازگاری با SDK و قالب JSON روی سیم.
///
/// دو چیز را می‌سنجد:
///   الف) هر فیلدی که TaxService به InvoiceHeaderDto می‌دهد، واقعا روی همان
///        نسخه‌ای از SDK که پروژه به آن لینک شده وجود دارد و نوعش می‌خواند.
///   ب) بسته‌ای که سرور ساختگی رمزگشایی می‌کند، دقیقا همان قالبی است که
///        دستورالعمل انتظار دارد (کلیدهای camelCase، تاریخ میلی‌ثانیه، و …).
/// </summary>
internal static class WireFormat
{
    public static void Run(string baseUrl, HttpClient http,
                           Action<string, bool, string> check, Action<string> line)
    {
        // ---------------- الف) قرارداد SDK ----------------
        var asm = typeof(InvoiceHeaderDto).Assembly;
        var version = asm.GetName().Version?.ToString() ?? "?";
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                      .InformationalVersion ?? version;
        line($"      · SDK لینک‌شده: {info}");

        var props = typeof(InvoiceHeaderDto).GetProperties()
                        .ToDictionary(p => p.Name, p => p.PropertyType);

        string[] used =
        {
            "Taxid","Indatim","Indati2m","Inty","Inno","Irtaxid","Inp","Ins","Insr",
            "Tins","Tob","Bid","Tinb","Sbc","Bpc","Bbc","Ft","Bpn","Scln","Scc","Crn",
            "Billid","Tprdis","Tdis","Tadis","Tvam","Todam","Tbill","Setm","Cap","Insp",
            "Tvop","Tax17","Cdcn","Cdcd","Tonw","Torv","Tocv",
        };
        var missing = used.Where(n => !props.ContainsKey(n)).ToList();
        check("۱۷-۱ همه فیلدهایی که برنامه می‌فرستد روی SDK وجود دارند",
              missing.Count == 0, string.Join(",", missing));

        check("۱۷-۲ نوع Insr عددی است (در 0.0.30 رشته بود، در نسخه فعلی int?)",
              props.TryGetValue("Insr", out var it) && it == typeof(int?),
              props.TryGetValue("Insr", out var it2) ? it2.ToString() : "ندارد");

        check("۱۷-۳ فیلدهای یادداشت (Nti1/Nti2) روی SDK هستند",
              props.ContainsKey("Nti1") && props.ContainsKey("Nti2"), "");

        // ---------------- ب) قالب روی سیم ----------------
        var json = http.GetStringAsync(baseUrl.TrimEnd('/') + "/__last")
                       .GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.GetProperty("invoice").ValueKind != JsonValueKind.Object)
        {
            check("۱۷-۴ آخرین بسته رمزگشایی شد", false, "بسته‌ای ثبت نشده");
            return;
        }

        var packet = root.GetProperty("packet");
        var invoice = root.GetProperty("invoice");
        var header = invoice.GetProperty("header");

        // --- پاکت ---
        check("۱۷-۴ پاکت رمزگذاری‌شده است (کلید متقارن دارد)",
              packet.TryGetProperty("symmetricKey", out var sk) &&
              sk.ValueKind == JsonValueKind.String && sk.GetString()!.Length > 100, "");
        check("۱۷-۵ بردار اولیه (iv) هگز ۳۲ کاراکتری است",
              packet.TryGetProperty("iv", out var iv) &&
              (iv.GetString() ?? "").Length == 32, iv.GetString());
        check("۱۷-۶ نوع بسته INVOICE.V01 است",
              packet.GetProperty("packetType").GetString() == "INVOICE.V01",
              packet.GetProperty("packetType").GetString());
        check("۱۷-۷ امضای محتوا ضمیمه شده است",
              packet.TryGetProperty("dataSignature", out var ds) &&
              ds.ValueKind == JsonValueKind.String && ds.GetString()!.Length > 100, "");
        check("۱۷-۸ شناسه حافظه مالیاتی روی پاکت است",
              (packet.GetProperty("fiscalId").GetString() ?? "").Length == 6,
              packet.GetProperty("fiscalId").GetString());
        check("۱۷-۹ هر پاکت uid یکتا دارد",
              Guid.TryParse(packet.GetProperty("uid").GetString(), out _), "");

        // --- ساختار صورتحساب ---
        check("۱۷-۱۰ صورتحساب سه بخش header / body / payments دارد",
              invoice.TryGetProperty("header", out _) &&
              invoice.TryGetProperty("body", out _) &&
              invoice.TryGetProperty("payments", out _), "");
        check("۱۷-۱۱ body یک آرایه است",
              invoice.GetProperty("body").ValueKind == JsonValueKind.Array, "");

        // --- سرصفحه ---
        check("۱۷-۱۲ کلیدها camelCase هستند (taxid نه Taxid)",
              header.TryGetProperty("taxid", out _), "");
        check("۱۷-۱۳ شماره مالیاتی ۲۲ کاراکتر است",
              (header.GetProperty("taxid").GetString() ?? "").Length == 22,
              header.GetProperty("taxid").GetString());
        check("۱۷-۱۴ تاریخ به میلی‌ثانیه و عددی است",
              header.GetProperty("indatim").ValueKind == JsonValueKind.Number &&
              header.GetProperty("indatim").GetInt64() > 1_600_000_000_000L,
              header.GetProperty("indatim").ToString());
        check("۱۷-۱۵ مبالغ عددی‌اند نه رشته",
              header.GetProperty("tbill").ValueKind == JsonValueKind.Number, "");
        check("۱۷-۱۶ سریال صورتحساب ۱۰ کاراکتر است",
              (header.GetProperty("inno").GetString() ?? "").Length == 10,
              header.GetProperty("inno").GetString());

        bool insrNull = !header.TryGetProperty("insr", out var insr) ||
                        insr.ValueKind == JsonValueKind.Null;
        check("۱۷-۱۷ قاعده ارسال (insr) خالی می‌رود (ماده ۹ خودکار خاموش است)",
              insrNull, insr.ToString());

        bool notesEmpty =
            (!header.TryGetProperty("nti1", out var n1) || n1.ValueKind == JsonValueKind.Null) &&
            (!header.TryGetProperty("nti2", out var n2) || n2.ValueKind == JsonValueKind.Null);
        check("۱۷-۱۸ یادداشت‌ها (nti1/nti2) خالی می‌روند", notesEmpty, "");

        // --- اقلام ---
        var first = invoice.GetProperty("body")[0];
        check("۱۷-۱۹ هر قلم شناسه کالا/خدمت دارد",
              first.TryGetProperty("sstid", out var ss) &&
              !string.IsNullOrWhiteSpace(ss.GetString()), "");
        check("۱۷-۲۰ هر قلم واحد اندازه‌گیری دارد",
              first.TryGetProperty("mu", out var mu) &&
              !string.IsNullOrWhiteSpace(mu.GetString()), "");
        check("۱۷-۲۱ مبلغ کل قلم عددی است",
              first.GetProperty("tsstam").ValueKind == JsonValueKind.Number, "");

        // --- پاکت بیرونی ---
        var env = root.GetProperty("envelope");
        check("۱۷-۲۲ درخواست امضای بیرونی دارد",
              env.TryGetProperty("signature", out var sig) &&
              sig.ValueKind == JsonValueKind.String && sig.GetString()!.Length > 100, "");
    }
}
