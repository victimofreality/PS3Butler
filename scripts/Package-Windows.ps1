[CmdletBinding()]
param([string]$DotNetPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $DotNetPath) {
    $localSdk = Join-Path $projectRoot 'artifacts\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) { $DotNetPath = $localSdk }
    else { $DotNetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}

# A fresh publish directory prevents saved data or old build files entering the ZIP.
$packageId = 'PS3Butler-win-x64-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$packageRoot = Join-Path $projectRoot ('artifacts\packages\' + $packageId)
$publishDirectory = Join-Path $packageRoot 'PS3Butler'
New-Item -ItemType Directory -Path $publishDirectory -ErrorAction Stop | Out-Null
& $DotNetPath publish (Join-Path $projectRoot 'PS3Butler.csproj') --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE. No package was created." }
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'PS3Butler.exe'))) {
    throw 'Publish did not produce PS3Butler.exe.'
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'Data')) {
    throw 'Unexpected Data directory in fresh publish output. Packaging stopped.'
}
Set-Content -LiteralPath (Join-Path $publishDirectory 'portable.txt') -Value 'Keep this file beside PS3Butler.exe to store settings and queue in Data.' -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PORTABLE.md') -Destination (Join-Path $publishDirectory 'START-HERE.md')
$archivePath = Join-Path $packageRoot ($packageId + '.zip')
Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
Set-Content -LiteralPath ($archivePath + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($archivePath)) -Encoding ASCII
Write-Output "Package: $archivePath"
Write-Output "SHA256: $hash"
