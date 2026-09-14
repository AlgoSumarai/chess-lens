$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
& "$PSScriptRoot/init-env.ps1"
docker compose -f compose.yaml -f compose.app.yaml up -d --build
if ($LASTEXITCODE -ne 0) { throw 'Container startup failed. Check Docker is running and inspect the output above.' }
Write-Output 'ChessLens local URL: http://localhost:8088'
Write-Output 'Readiness: http://localhost:8088/health/ready'
