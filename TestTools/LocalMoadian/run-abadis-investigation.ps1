param([switch]$ProbeOnly)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$simple = $null
$complete = $null
try {
    $simple = Start-Process python -ArgumentList @((Join-Path $PSScriptRoot 'moadian_mock.py'), '--port', '19091', '--quiet') -WindowStyle Hidden -PassThru
    $complete = Start-Process python -ArgumentList @((Join-Path $PSScriptRoot 'moadian_mock_full.py'), '--port', '19191', '--quiet') -WindowStyle Hidden -PassThru
    foreach ($port in @(19091,19191)) {
        $ready = $false
        for ($i = 0; $i -lt 30; $i++) {
            try { Invoke-RestMethod "http://127.0.0.1:$port/__ping" -TimeoutSec 2 | Out-Null; $ready = $true; break } catch { Start-Sleep -Milliseconds 300 }
        }
        if (!$ready) { throw "Mock not ready on port $port" }
    }
    if ($ProbeOnly) {
        & (Join-Path $PSScriptRoot '../Abadis915Probe/bin/Debug/net6.0/Abadis915Probe.exe') 'http://127.0.0.1:19191/' *> (Join-Path $root 'abadis-probe-tests.log')
    } else {
        & (Join-Path $PSScriptRoot 'bin/Debug/net6.0/MoadianLocalTest.exe') 'http://127.0.0.1:19091/' '--full=http://127.0.0.1:19191/' *> (Join-Path $root 'abadis-mock-tests.log')
    }
    $result = $LASTEXITCODE
    if ($ProbeOnly) { Get-Content (Join-Path $root 'abadis-probe-tests.log') }
    else { Get-Content (Join-Path $root 'abadis-mock-tests.log') -Tail 20 }
    exit $result
} finally {
    foreach ($process in @($simple,$complete)) {
        if ($null -ne $process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
