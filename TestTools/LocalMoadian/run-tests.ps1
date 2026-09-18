<#
    اجرای کامل تست‌های رگرسیون سامانه مؤدیان.

        .\TestTools\LocalMoadian\run-tests.ps1            سریع  (بدون دیتابیس)
        .\TestTools\LocalMoadian\run-tests.ps1 -Full      کامل  (با دیتابیس)
        .\TestTools\LocalMoadian\run-tests.ps1 -Full -Record   ثبت مرجع جدید

    خودش سرور ساختگی را بالا می‌آورد، داده تست را می‌سازد، تست را اجرا می‌کند،
    و در پایان همه‌چیز را پاک می‌کند. کد خروجی صفر یعنی همه‌چیز سالم است.
#>
param(
    [switch]$Full,
    [switch]$Record,
    [int]$Port = 9090,
    [string]$Server = "MERCEDES\SQL2022",
    [string]$Database = "YAZDSEPAR1405_06_25"
)

$ErrorActionPreference = "Stop"
$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$root  = Resolve-Path (Join-Path $here "..\..")
$msb   = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
$exe   = Join-Path $here "bin\Debug\net6.0\MoadianLocalTest.exe"
$mock  = Join-Path $here "moadian_mock.py"
$seed  = Join-Path $here "seed_bulk_test.sql"
$clean = Join-Path $here "cleanup_bulk_test.sql"

function Say($m) { Write-Host "  $m" -ForegroundColor DarkGray }

# اثر انگشت محتوای همه ردیف‌های غیرتستی TAXDTL.
#
# روش: هر ردیف را کامل (همه ستون‌ها) به XML تبدیل و SHA-256 می‌گیریم، بعد
# هش‌ها را به ترتیب IDD پشت هم می‌چسبانیم و یک SHA-256 نهایی می‌گیریم.
#
# چرا نه جمع ستون‌ها: تغییر «+۱ روی یک ردیف و −۱ روی ردیف دیگر» همدیگر را
# خنثی می‌کند و جمع ثابت می‌ماند. هش این کار را نمی‌کند.
# چرا نه CHECKSUM: XOR-محور است و همان خاصیت خنثی‌شدن را دارد.
function Get-TaxdtlFingerprint($Server, $Database) {
    $q = @"
SET NOCOUNT ON;
SELECT CONVERT(VARCHAR(64), HASHBYTES('SHA2_256',
    (SELECT CONVERT(VARCHAR(64), HASHBYTES('SHA2_256',
                CONVERT(NVARCHAR(MAX), (SELECT t.* FOR XML RAW, BINARY BASE64))), 2) AS [text()]
     FROM dbo.TAXDTL AS t
     WHERE ISNULL(t.TAG, -1) NOT IN (77, 88)   -- NULL NOT IN (...) => NULL => ردیف نادیده می‌ماند
     ORDER BY t.IDD
     FOR XML PATH(''))), 2) AS FP;
"@
    $out = sqlcmd -S $Server -d $Database -E -C -I -b -h -1 -W -Q $q
    if ($LASTEXITCODE -ne 0) {
        throw "خواندن اثر انگشت TAXDTL شکست خورد: $($out -join ' ')"
    }
    $fp = (($out -join "") -replace '\s','')
    if ($fp -notmatch '^[0-9A-Fa-f]{64}$') {
        throw "اثر انگشت TAXDTL معتبر نیست: $fp"
    }
    return $fp
}

$mockProc = $null
$seeded   = $false
$code     = 0        # هر شکستی این را غیرصفر می‌کند و هیچ‌جا بازنویسی نمی‌شود

try {
    # ---------- ۰. اثر انگشت داده واقعی، قبل از هر کاری ----------
    $fpBefore = $null
    if ($Full) {
        try {
            $fpBefore = Get-TaxdtlFingerprint $Server $Database
            Say "اثر انگشت داده واقعی: $($fpBefore.Substring(0,16))..."
        }
        catch {
            Write-Host "  $_" -ForegroundColor Red
            Write-Host "  بدون اثر انگشت، تضمینی برای دست‌نخوردن داده نیست — اجرا متوقف شد." -ForegroundColor Red
            $code = 1
        }
    }

    # ---------- ۱. بیلد ----------
    Say "بیلد solution ..."
    & $msb (Join-Path $root "TaxationApp.sln") /v:q /nologo /clp:ErrorsOnly | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "  بیلد solution شکست خورد" -ForegroundColor Red; $code = 1 }

    Say "بیلد هارنس تست ..."
    & $msb (Join-Path $here "MoadianLocalTest.csproj") /t:"Restore;Build" /v:q /nologo /clp:ErrorsOnly | Out-Null
    if ($code -eq 0 -and $LASTEXITCODE -ne 0) { Write-Host "  بیلد هارنس تست شکست خورد" -ForegroundColor Red; $code = 1 }

    # ---------- ۲. سرور ساختگی ----------
    Say "بالا آوردن سرور ساختگی روی پورت $Port ..."
    $mockProc = Start-Process python -ArgumentList "`"$mock`"", "--port", $Port, "--quiet" `
                                     -PassThru -WindowStyle Hidden
    $ok = $false
    foreach ($i in 1..20) {
        Start-Sleep -Milliseconds 400
        try { Invoke-RestMethod "http://127.0.0.1:$Port/__ping" -TimeoutSec 2 | Out-Null; $ok = $true; break } catch {}
    }
    if (-not $ok) { Write-Host "  سرور ساختگی بالا نیامد" -ForegroundColor Red; $code = 1 }

    # ---------- ۳. داده تست ----------
    $testArgs = @()
    if ($Full) {
        Say "ساخت داده تست در دیتابیس ..."
        # پیش از اجرا علامت می‌زنیم، نه بعدش: اگر seed وسط کار بترکد، باز هم
        # باید پاک‌سازی اجرا شود. خود seed هم داخل یک تراکنش است.
        $seeded = $true
        sqlcmd -S $Server -E -C -I -b -h -1 -i $seed | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ساخت داده تست شکست خورد" -ForegroundColor Red
            $code = 1
        } else {
            $testArgs += "--bulk"
        }
        $seedOk = ($code -eq 0)
    }
    if ($Record) { $testArgs += "--record" }
    $testArgs += "http://127.0.0.1:$Port/"   # آدرس صریح، وگرنه هارنس سراغ ۹۰۹۰ می‌رود

    # ---------- ۴. اجرا ----------
    # اگر ساخت داده شکست خورده، اجرای تست بی‌معنی است و نباید کد خروجی را
    # پاک کند. هر شکستی در $code جمع می‌شود، نه اینکه جایگزین شود.
    if ($code -eq 0) {
        Write-Host ""
        & $exe @testArgs
        if ($LASTEXITCODE -ne 0) { $code = $LASTEXITCODE }
    } else {
        Write-Host ""
        Write-Host "  اجرای تست‌ها به‌خاطر شکست مرحله قبل انجام نشد." -ForegroundColor Red
    }
}
finally {
    if ($seeded) {
        Say "پاک کردن داده تست ..."
        sqlcmd -S $Server -E -C -I -b -h -1 -i $clean | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  پاک‌سازی داده تست شکست خورد — دستی اجرا کنید: $clean" -ForegroundColor Red
            $code = 1
        }
    }
    if ($mockProc -and -not $mockProc.HasExited) {
        Stop-Process -Id $mockProc.Id -Force -ErrorAction SilentlyContinue
    }
}

# ---------- ۵. داده واقعی باید مو به مو همان باشد ----------
if ($Full -and $fpBefore) {
    try { $fpAfter = Get-TaxdtlFingerprint $Server $Database }
    catch { Write-Host "  $_" -ForegroundColor Red; $fpAfter = "UNREADABLE" }
    if ($fpAfter -ne $fpBefore) {
        Write-Host ""
        Write-Host "  داده واقعی TAXDTL عوض شده است" -ForegroundColor Red
        Write-Host "    قبل : $fpBefore" -ForegroundColor DarkGray
        Write-Host "    بعد : $fpAfter"  -ForegroundColor DarkGray
        $code = 1
    } else {
        Say "داده واقعی TAXDTL دست‌نخورده ماند"
    }
}

Write-Host ""
if ($code -eq 0) {
    Write-Host "  همه تست‌ها سبز است." -ForegroundColor Green
} else {
    Write-Host "  تست شکست خورد — بالا را ببینید." -ForegroundColor Red
}
exit $code
