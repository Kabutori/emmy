$ErrorActionPreference='Stop'
$hostPath=Join-Path $PSScriptRoot 'host/Emmy.Host.exe'
if (!(Test-Path $hostPath)) { throw 'Extract the complete package before starting Emmy.' }
Start-Process $hostPath -ArgumentList '--open' -WorkingDirectory (Split-Path $hostPath -Parent)
