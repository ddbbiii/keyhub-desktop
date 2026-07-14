$ErrorActionPreference = "Stop"

$version = "8.30.1"
$expectedSha256 = "D29144DEFF3A68AA93CED33DDDF84B7FDC26070ADD4AA0F4513094C8332AFC4E"
$archiveName = "gitleaks_${version}_windows_x64.zip"
$downloadUrl = "https://github.com/gitleaks/gitleaks/releases/download/v$version/$archiveName"
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$toolDirectory = Join-Path $tempRoot "gitleaks-$version"
$archivePath = Join-Path $tempRoot $archiveName

if (Test-Path -LiteralPath $toolDirectory) {
    Remove-Item -LiteralPath $toolDirectory -Recurse -Force
}

Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
$actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($actualSha256 -ne $expectedSha256) {
    throw "Gitleaks archive checksum mismatch."
}

Expand-Archive -LiteralPath $archivePath -DestinationPath $toolDirectory -Force
& (Join-Path $toolDirectory "gitleaks.exe") git --redact --verbose
if ($LASTEXITCODE -ne 0) {
    throw "Gitleaks reported a secret or failed with exit code $LASTEXITCODE."
}
