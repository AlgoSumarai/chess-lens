$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$line = Get-Content -LiteralPath (Join-Path $workspace '.env') | Where-Object { $_.StartsWith('POSTGRES_PASSWORD=') } | Select-Object -First 1
if (!$line) { throw 'Run scripts/init-env.ps1 first.' }
$databasePassword = $line.Substring('POSTGRES_PASSWORD='.Length)
$env:ConnectionStrings__Database = "Host=localhost;Port=54329;Database=chesslens;Username=chesslens;Password=$databasePassword;Include Error Detail=false"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$enginePath = Join-Path $workspace '.local/stockfish-19/stockfish/stockfish-windows-x86-64-universal.exe'
if (Test-Path -LiteralPath $enginePath) { $env:Stockfish__Path = $enginePath }
