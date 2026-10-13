param([string]$DockerContext = 'default', [string]$Project = 'chesslens')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$platform = docker --context $DockerContext info --format '{{.OSType}} {{.OperatingSystem}}'
if ($LASTEXITCODE -ne 0 -or $platform -notlike 'linux *' -or $platform -like '*Docker Desktop*') {
    throw 'Use a Linux Docker Engine context. On Windows 11, run scripts/start.sh inside Ubuntu WSL 2.'
}
& "$PSScriptRoot/init-env.ps1"
$composeArgs = @('--context', $DockerContext, 'compose', '-p', $Project, '-f', 'compose.yaml', '-f', 'compose.app.yaml')
docker @composeArgs build
if ($LASTEXITCODE -ne 0) { throw 'Image build failed; existing services have not been stopped.' }
docker @composeArgs stop api worker
if ($LASTEXITCODE -ne 0) { throw 'Could not stop old application versions before migration.' }
docker @composeArgs up -d --no-build --wait --wait-timeout 240
if ($LASTEXITCODE -ne 0) { throw 'Container startup failed. Check Docker is running and inspect the output above.' }
$port = if ($env:APP_PORT) { $env:APP_PORT } else { '8088' }
Write-Output "ChessLens local URL: http://localhost:$port"
Write-Output "Readiness: http://localhost:$port/health/ready"
