$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$hostDirectory=Join-Path $repo 'artifacts/package/host'
$previousData=$env:EMMY_DATA_DIR
$previousToken=$env:EMMY_TEST_TOKEN_FILE
Push-Location $repo
try {
    foreach ($test in @('api','dom','web')) {
        $env:EMMY_DATA_DIR=Join-Path ([System.IO.Path]::GetTempPath()) ("emmy-ci-"+[guid]::NewGuid())
        New-Item -ItemType Directory $env:EMMY_DATA_DIR | Out-Null
        $env:EMMY_TEST_TOKEN_FILE=Join-Path $env:EMMY_DATA_DIR 'bridge.token'
        $process=Start-Process (Join-Path $hostDirectory 'Emmy.Host.exe') -WorkingDirectory $hostDirectory -PassThru -RedirectStandardOutput "$env:EMMY_DATA_DIR/host.log" -RedirectStandardError "$env:EMMY_DATA_DIR/host-error.log"
        try {
            $ready=$false
            for ($attempt=0;$attempt -lt 50;$attempt++) {
                if ($process.HasExited) { throw "Test host exited: $(Get-Content "$env:EMMY_DATA_DIR/host-error.log" -Raw)" }
                try { Invoke-WebRequest 'http://127.0.0.1:17840/' -TimeoutSec 1 | Out-Null; $ready=$true; break } catch { Start-Sleep -Milliseconds 200 }
            }
            if (!$ready) { throw 'Test host did not become ready.' }
            node "scripts/test-$test.cjs"
            if ($LASTEXITCODE -ne 0) { throw "$test checks failed." }
        } finally {
            if (!$process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }
        }
    }
} finally {
    $env:EMMY_DATA_DIR=$previousData
    $env:EMMY_TEST_TOKEN_FILE=$previousToken
    Pop-Location
}
