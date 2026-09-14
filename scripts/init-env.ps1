$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $workspace '.env'
if (!(Test-Path -LiteralPath $envFile)) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $password = [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
    [System.IO.File]::WriteAllText($envFile, "POSTGRES_PASSWORD=$password`n")
    Write-Output 'Created local .env with a random database password.'
}
