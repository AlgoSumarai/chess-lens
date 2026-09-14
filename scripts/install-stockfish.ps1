$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$workspace = Split-Path -Parent $PSScriptRoot
$target = Join-Path $workspace '.local/stockfish-19'
$archive = Join-Path $workspace '.local/stockfish-19.zip'
$expected = '3c8bf1f9ea66a09350a40df4f632288285ac206d99f33ab5842c408fc30b48a7'
New-Item -ItemType Directory -Path $target -Force | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/official-stockfish/Stockfish/releases/download/sf_19/stockfish-windows-x86-64-universal.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    throw 'Stockfish archive checksum does not match the official release asset.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
Write-Output 'Verified and extracted official Stockfish 19 with bundled licence/source material.'
Get-ChildItem -LiteralPath $target -Filter '*.exe' -Recurse | Select-Object -ExpandProperty FullName
