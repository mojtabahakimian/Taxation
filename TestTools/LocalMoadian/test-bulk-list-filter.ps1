# Executes the actual list/warning SQL extracted from the WPF source.
# Fixtures use SQL table variables only; no application tables are changed.
param(
    [string]$Server = 'MERCEDES\SQL2022',
    [string]$Database = 'YAZDSEPAR1405',
    [long]$InvoiceDate = 0
)
$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot '..\..\Prg_Grpsend\MainWindow.xaml.cs'
$source = Get-Content $sourcePath -Raw -Encoding UTF8

function Extract([string]$pattern) {
    $match = [regex]::Match($source, $pattern, 'Singleline')
    if (-not $match.Success) { throw "Production SQL not found: $pattern" }
    return $match.Groups[1].Value
}

$filter = Extract 'string notExistsCondition = includeSentInvoices \? "" : \$@"(.*?)";'
$warning = Extract 'string checkSql = \$@"(.*?)";'
$mainApi = Extract 'string apiSqlCondition = .*?\? "(.*?)"'
$sandboxApi = Extract 'string apiSqlCondition = .*?: "(.*?)";'

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder['Data Source'] = $Server
$builder['Initial Catalog'] = $Database
$builder['Integrated Security'] = $true
$builder['TrustServerCertificate'] = $true
$connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString

function Query([string]$sql) {
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    try {
        $table = New-Object System.Data.DataTable
        $reader = $command.ExecuteReader()
        try { $table.Load($reader) } finally { $reader.Dispose() }
        return ,$table
    } finally { $command.Dispose() }
}

function Assert-Numbers($table, [string]$expected, [string]$label) {
    $actual = ($table.Rows | ForEach-Object { [int]$_.NUMBER }) -join ','
    if ($actual -ne $expected) { throw "${label}: expected [$expected], got [$actual]" }
    Write-Output "PASS $label [$actual]"
}

try {
    $connection.Open()
    foreach ($headTag in @(13, 2)) {
        # 1/2/3: main SUCCESS/PENDING/UNKNOWN; 4/5: retryable errors;
        # 6/7: sandbox (including legacy NULL ApiTypeSent); 8: another document;
        # 9: correction only; 10: no history; 11: failed then successful;
        # 12/13: legacy NULL TAG/Ins (must not guess their document identity).
        $fixture = @"
SET NOCOUNT ON;
DECLARE @HEAD_LST TABLE (NUMBER float, TAG float);
DECLARE @TAXDTL TABLE (NUMBER float, TAG float, Ins int, ApiTypeSent bit, TheStatus nvarchar(30));
INSERT INTO @HEAD_LST SELECT n, $headTag FROM
    (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13)) v(n);
INSERT INTO @TAXDTL VALUES
 (1,2,1,1,'SUCCESS'),(1,2,1,1,'SUCCESS'),
 (2,2,1,1,'PENDING'),(3,2,1,1,'UNKNOWN'),
 (4,2,1,1,'FAILED'),(5,2,1,1,'LOCAL_ERROR'),
 (6,2,1,0,'SUCCESS'),(7,2,1,NULL,'SUCCESS'),
 (8,5,1,1,'SUCCESS'),(9,2,2,1,'SUCCESS'),
 (11,2,1,1,'FAILED'),(11,2,1,1,'SUCCESS'),
 (12,NULL,1,1,'SUCCESS'),(13,2,NULL,1,'SUCCESS');
"@
        foreach ($api in @(1, 0)) {
            $apiSql = if ($api -eq 1) { $mainApi } else { $sandboxApi }
            foreach ($includeSent in @($false, $true)) {
                $clause = if ($includeSent) { '' } else { $filter.Replace('{apiSqlCondition}', $apiSql) }
                $query = "SELECT dbo.HEAD_LST.NUMBER FROM dbo.HEAD_LST WHERE dbo.HEAD_LST.TAG=$headTag $clause ORDER BY dbo.HEAD_LST.NUMBER"
                $query = $query.Replace('dbo.HEAD_LST', '@HEAD_LST').Replace('dbo.TAXDTL', '@TAXDTL')
                # Table variables need aliases for qualified column references.
                $query = $query.Replace('@HEAD_LST.', 'H.').Replace('@TAXDTL.', 'T.')
                $query = $query.Replace('FROM @HEAD_LST', 'FROM @HEAD_LST H').Replace('FROM @TAXDTL', 'FROM @TAXDTL T')
                $expected = if ($includeSent) { '1,2,3,4,5,6,7,8,9,10,11,12,13' }
                    elseif ($api -eq 1) { '4,5,6,7,8,9,10,12,13' }
                    else { '1,2,3,4,5,8,9,10,11,12,13' }
                Assert-Numbers (Query ($fixture + $query)) $expected "list head=$headTag api=$api includeSent=$includeSent"
            }
        }
        $query = $warning.Replace('{condition}', [string]$headTag).Replace('{string.Join(",", selected)}', '1,2,3,4,5,6,7,8,9,10,11,12,13')
        $query = $query.Replace('dbo.TAXDTL', '@TAXDTL')
        $table = Query ($fixture + $query)
        $count = [int]$table.Rows[0][0]
        if ($count -ne 4) { throw "warning head=${headTag}: expected 4 distinct invoices, got $count" }
        Write-Output "PASS warning head=$headTag count=$count"
    }

    if ($InvoiceDate -gt 0) {
        # Read-only check against customer data using the same production filter.
        $clause = $filter.Replace('{apiSqlCondition}', $mainApi)
        $table = Query @"
SELECT dbo.HEAD_LST.NUMBER
FROM dbo.HEAD_LST
LEFT JOIN dbo.HEAD_LST_EXTENDED e ON e.NUMBER=dbo.HEAD_LST.NUMBER AND e.tgu=dbo.HEAD_LST.TAG
WHERE dbo.HEAD_LST.TAG=13 AND dbo.HEAD_LST.DATE_N=$InvoiceDate
  AND (ISNULL(e.ins,1)=1 OR e.irtaxid IS NULL OR e.irtaxid=N'0' OR e.irtaxid=N'')
  $clause
ORDER BY dbo.HEAD_LST.NUMBER;
"@
        Write-Output "Customer date $InvoiceDate : $($table.Rows.Count) invoices remain in the main unsent list."
    }
    Write-Output 'PASS all 10 bulk-list regression cases; application tables were read only.'
} finally { $connection.Dispose() }
