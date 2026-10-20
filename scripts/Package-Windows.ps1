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
$projectFile = Join-Path $projectRoot 'PS3Butler.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
$previewVersion = [string]$projectXml.Project.PropertyGroup.Version
if (-not $previewVersion) { throw 'Project preview version is missing.' }
$sourceCommit = (& git -C $projectRoot rev-parse --short=12 HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
$sourceChanges = & git -C $projectRoot status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source changes.' }
$dirtySuffix = if ($sourceChanges) { '.dirty' } else { '' }
$buildStamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss') + '.' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$buildIdentity = $previewVersion + '+' + $buildStamp + '.' + $sourceCommit + $dirtySuffix
$packageId = 'PS3Butler-' + $previewVersion + '-win-x64-' + $buildStamp
$packageRoot = Join-Path $projectRoot ('artifacts\packages\' + $packageId)
$publishDirectory = Join-Path $packageRoot 'PS3Butler'
New-Item -ItemType Directory -Path $publishDirectory -ErrorAction Stop | Out-Null
& $DotNetPath publish $projectFile --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false "-p:InformationalVersion=$buildIdentity" -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE. No package was created." }
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'PS3Butler.exe'))) {
    throw 'Publish did not produce PS3Butler.exe.'
}
if (Test-Path -LiteralPath (Join-Path $publishDirectory 'Data')) {
    throw 'Unexpected Data directory in fresh publish output. Packaging stopped.'
}
Set-Content -LiteralPath (Join-Path $publishDirectory 'portable.txt') -Value 'Keep this file beside PS3Butler.exe to store settings and queue in Data.' -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PORTABLE.md') -Destination (Join-Path $publishDirectory 'START-HERE.md')
$actualIdentity = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishDirectory 'PS3Butler.dll')).ProductVersion
if ($actualIdentity -ne $buildIdentity) { throw 'Published build identity differs from requested identity.' }
Set-Content -LiteralPath (Join-Path $publishDirectory 'BUILD.txt') -Value $buildIdentity -Encoding UTF8
$archivePath = Join-Path $packageRoot ($packageId + '.zip')
Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
Set-Content -LiteralPath ($archivePath + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($archivePath)) -Encoding ASCII
Write-Output "Package: $archivePath"
Write-Output "SHA256: $hash"
Write-Output "Build: $buildIdentity"
