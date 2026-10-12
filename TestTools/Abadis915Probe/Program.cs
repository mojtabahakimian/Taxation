using System.Reflection;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Service;

// Uses the existing encrypted C# send helper, fresh dates and synthetic amounts.
// No customer database writes and no production endpoint.
var url = args.Single();
if (!url.StartsWith("http://127.0.0.1:")) throw new Exception("Local mock required");
var harness = Assembly.Load("MoadianLocalTest").GetType("MoadianLocalTest.Program", true);
var flags = BindingFlags.Static | BindingFlags.NonPublic;
var key = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test_private_key.txt")).Trim();
var tax = new TaxService("A2HGPP", key, url);
tax.RequestToken();
harness.GetField("BaseUrl", flags).SetValue(null, url);
harness.GetField("_tax", flags).SetValue(null, tax);
using var http = new HttpClient();
await http.PostAsync(url + "__reset", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
int failures = 0;
void Check(string label, bool ok, string detail = "") {
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {label}: {detail}");
    if (!ok) failures++;
}
TaxModel.InvoiceModel.Header New(int ins = 1, string reference = null, string inno = null) =>
    (TaxModel.InvoiceModel.Header)harness.GetMethod("NewInvoice", flags).Invoke(null, new object[] { ins, reference, inno, null });
(bool Ok, string Codes) Send(TaxModel.InvoiceModel.Header h, string label) {
    var r = harness.GetMethod("Send", flags).Invoke(null, new object[] { h, label, null });
    return ((bool)r.GetType().GetProperty("Ok").GetValue(r), (string)r.GetType().GetProperty("Codes").GetValue(r));
}
const string code = "61459886888";
Check("local real-person rejects exact 11-digit code", !MoadianRules.ValidateBuyer(1,1,1,code,null,null,out var error), error);
Check("local partnership accepts exact 11-digit code", MoadianRules.ValidateBuyer(1,1,3,code,null,null,out error), error);
var bad = New(); bad.Tob = 1; bad.Tinb = code;
var r = Send(bad,"Abadis real-person");
Check("encrypted send real-person rejected with 0101204", !r.Ok && r.Codes.Contains("0101204"), r.Codes);
var good = New(); good.Tob = 3; good.Tinb = code;
r = Send(good,"Abadis partnership");
Check("encrypted send partnership original accepted", r.Ok, r.Codes);
var old = New(); old.Inty = 2; old.Tob = 1; old.Tinb = null;
r = Send(old,"type2 reference");
Check("type2 reference accepted", r.Ok, r.Codes);
var changed = New(2,old.Taxid,old.Inno); changed.Tob = 3; changed.Tinb = code;
r = Send(changed,"type1 partnership correction of type2");
Check("changing buyer/type via correction still rejected", !r.Ok && r.Codes.Contains("0300401") && r.Codes.Contains("0301001") && r.Codes.Contains("0301201"), r.Codes);
var same = New(2,good.Taxid,good.Inno); same.Tob = 3; same.Tinb = code;
r = Send(same,"unchanged partnership correction");
Check("unchanged partnership correction accepted", r.Ok, r.Codes);
Console.WriteLine($"Probe failures: {failures}");
return failures == 0 ? 0 : 1;
