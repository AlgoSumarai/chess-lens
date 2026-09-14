# Export only the public root certificate from a successfully validated TLS chain.
# No private keys are read or exported. No machine-wide trust settings are changed.
$ErrorActionPreference = 'Stop'
$request = [System.Net.WebRequest]::Create('https://api.nuget.org/v3/index.json')
$response = $request.GetResponse()
try {
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($request.ServicePoint.Certificate)
    $chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
    if (!$chain.Build($certificate)) { throw 'The existing Windows trust store did not validate the certificate chain.' }
    $root = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
    $pem = "-----BEGIN CERTIFICATE-----`n" + [Convert]::ToBase64String($root.RawData, [Base64FormattingOptions]::InsertLineBreaks) + "`n-----END CERTIFICATE-----`n"
    $directory = Join-Path (Split-Path -Parent $PSScriptRoot) '.local'
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $directory 'build-ca.pem'), $pem)
    Write-Output "Exported the already trusted public build CA: $($root.Subject)"
} finally { $response.Dispose() }
