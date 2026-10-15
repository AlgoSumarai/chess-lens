param([string]$Destination = '')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$workspace = Split-Path -Parent $PSScriptRoot
$target = if ($Destination) { [IO.Path]::GetFullPath($Destination) } else { Join-Path $workspace '.local/stockfish-19' }
$archive = Join-Path $target 'stockfish-windows-x86-64-universal.zip'
$expected = '3c8bf1f9ea66a09350a40df4f632288285ac206d99f33ab5842c408fc30b48a7'
if (Test-Path -LiteralPath $target) {
    throw 'Select a new installation directory; an existing directory is never overwritten.'
}
New-Item -ItemType Directory -Path $target | Out-Null
Invoke-WebRequest -UseBasicParsing -TimeoutSec 180 -Uri 'https://github.com/official-stockfish/Stockfish/releases/download/sf_19/stockfish-windows-x86-64-universal.zip' -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    throw 'Stockfish archive checksum does not match the official release asset.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $target
$sourceArchive = Join-Path $target 'stockfish-sf19-source.tar.gz'
Invoke-WebRequest -UseBasicParsing -TimeoutSec 180 -Uri 'https://codeload.github.com/official-stockfish/Stockfish/tar.gz/edb0d9db6731067ec50ce619ff372b463bc4dd5d' -OutFile $sourceArchive
if ((Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne '4fc20699a5ecf7883b036ce180020f5e57979623ad719fd2a52839209521581b') {
    throw 'Stockfish corresponding source checksum does not match.'
}
$materials = Join-Path $target 'stockfish/corresponding-source'
New-Item -ItemType Directory -Path $materials | Out-Null
& tar -xzf $sourceArchive --strip-components=1 -C $materials
if ($LASTEXITCODE -ne 0) { throw 'Stockfish source extraction failed.' }
$network = Join-Path $materials 'src/nn-1a298aa575a0.nnue'
Invoke-WebRequest -UseBasicParsing -TimeoutSec 180 -Uri 'https://data.stockfishchess.org/nn/nn-1a298aa575a0.nnue' -OutFile $network
if ((Get-FileHash -LiteralPath $network -Algorithm SHA256).Hash.ToLowerInvariant() -ne '1a298aa575a085434d29027978dc36867fe9c5bcea9376654b7a8eba1e52dfc2') {
    throw 'Stockfish source network checksum does not match.'
}
Copy-Item -LiteralPath (Join-Path $workspace 'licenses/stockfish') -Destination (Join-Path $materials 'chesslens-notices') -Recurse
Write-Output 'Verified official Stockfish 19 with licences, complete source/network and build instructions.'
Get-ChildItem -LiteralPath $target -Filter '*.exe' -Recurse | Select-Object -ExpandProperty FullName
