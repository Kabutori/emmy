param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $destination = Join-Path $repo 'artifacts/package'
    if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
    New-Item -ItemType Directory "$destination/plugin","$destination/host" -Force | Out-Null
    dotnet run --project tests/Emmy.Tests/Emmy.Tests.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed; packaging cancelled.' }
    dotnet build src/DalamudEmmy/DalamudEmmy.csproj -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed; packaging cancelled.' }
    dotnet publish src/Emmy.Host/Emmy.Host.csproj -c $Configuration -r win-x64 --self-contained true -o "$destination/host" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Host build failed; packaging cancelled.' }
    Get-ChildItem "src/DalamudEmmy/bin/$Configuration" -File | Copy-Item -Destination "$destination/plugin"
    Copy-Item "src/DalamudEmmy/bin/$Configuration/runtimes" "$destination/plugin" -Recurse
    Copy-Item 'scripts/Start-Emmy.ps1','scripts/Install-Plugin.ps1' $destination
    Copy-Item 'docs/INSTALLATION.md','docs/ACCEPTANCE.md' $destination
    $commit = git rev-parse HEAD
    @{version='0.2.0-preview';protocol=1;sourceCommit=$commit;dalamudApi=15;configuration=$Configuration;ingameAccepted=$false} | ConvertTo-Json | Set-Content "$destination/build-manifest.json"
    Compress-Archive "$destination/*" 'artifacts/Emmy-preview.zip' -Force
    Write-Host 'Package: artifacts/Emmy-preview.zip'
} finally { Pop-Location }
