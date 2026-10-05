$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'plugin'
if (!(Test-Path "$source/DalamudEmmy.dll")) { throw 'Plugin package is incomplete.' }
$destination=Join-Path $env:APPDATA 'Emmy/plugin'
New-Item -ItemType Directory $destination -Force | Out-Null
Copy-Item "$source/*" $destination -Recurse -Force
Write-Host "Add this DLL to Dalamud /xlsettings > Experimental > Dev Plugin Locations:"
Write-Host "$destination/DalamudEmmy.dll"
