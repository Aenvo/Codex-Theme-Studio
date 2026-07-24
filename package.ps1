[CmdletBinding()]
param(
    [string]$Version,

    [switch]$SkipVerification
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$globalJson = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'global.json') |
    ConvertFrom-Json
$projectProperties = [xml](Get-Content -Raw -LiteralPath (
    Join-Path $projectRoot 'Directory.Build.props'))
$requiredSdkVersion = [string]$globalJson.sdk.version
$declaredVersion = [string](
    $projectProperties.Project.PropertyGroup.Version |
        Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $declaredVersion
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Release version must use major.minor.patch format. Actual: $Version"
}
$runtimeBaseline = Get-Content -Raw -LiteralPath (
    Join-Path $projectRoot 'eng\runtime-baseline.json') |
    ConvertFrom-Json
$runtimeIdentifier = [string]$runtimeBaseline.runtimeIdentifier
$nodeVersion = [string]$runtimeBaseline.nodeVersion
$nodeArchiveName = "node-v$nodeVersion-$runtimeIdentifier.zip"
$nodeArchiveSha256 = [string]$runtimeBaseline.nodeArchiveSha256
$artifactRoot = Join-Path $projectRoot 'artifacts'
$cacheRoot = Join-Path $artifactRoot 'cache'
$finalReleaseRoot = Join-Path $artifactRoot "release\$Version"
$packageName = "CodexThemeManager-$Version-win-x64-portable"

function Resolve-DotNet {
    $candidates = @()
    if ($env:DOTNET_ROOT) {
        $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
    }

    if ($env:LOCALAPPDATA) {
        $candidates += (Join-Path $env:LOCALAPPDATA (
            "CodexThemeStudio\devtools\dotnet-$requiredSdkVersion\dotnet.exe"))
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        $sdks = & $candidate --list-sdks
        if ($LASTEXITCODE -eq 0 -and
            $sdks -match "^$([regex]::Escape($requiredSdkVersion))\s") {
            return $candidate
        }
    }

    throw "Required .NET SDK $requiredSdkVersion was not found."
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Copy-TreeWithoutOverwriteConflict {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $sourcePrefix = [IO.Path]::GetFullPath($Source).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        if ($file.Extension -ieq '.pdb') {
            continue
        }

        if (-not $file.FullName.StartsWith(
            $sourcePrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Publish file escaped its source root: $($file.FullName)"
        }

        $relativePath = $file.FullName.Substring($sourcePrefix.Length)
        $destinationPath = Join-Path $Destination $relativePath
        $destinationParent = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null

        if (Test-Path -LiteralPath $destinationPath) {
            $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
            $destinationHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationPath).Hash
            if ($sourceHash -ne $destinationHash) {
                throw "Publish outputs conflict at $relativePath."
            }

            continue
        }

        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath
    }
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required release input is missing: $Source"
    }

    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination
}

if (Test-Path -LiteralPath $finalReleaseRoot) {
    throw "Release output already exists and will not be overwritten: $finalReleaseRoot"
}

$dotnet = Resolve-DotNet
$dotnetRoot = Split-Path -Parent $dotnet
$workRoot = Join-Path $artifactRoot ("work\package-" + [guid]::NewGuid().ToString('N'))
$desktopPublish = Join-Path $workRoot 'desktop'
$agentPublish = Join-Path $workRoot 'agent'
$releaseRoot = Join-Path $workRoot 'release'
$packageDirectory = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$nodeArchive = Join-Path $cacheRoot $nodeArchiveName
$nodeExtractRoot = Join-Path $cacheRoot "node-v$nodeVersion-$runtimeIdentifier"
$packageCompleted = $false

New-Item -ItemType Directory -Force -Path $cacheRoot, $workRoot | Out-Null

try {
if (-not $SkipVerification) {
    & (Join-Path $projectRoot 'build.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw 'Release verification failed.'
    }
}

if (-not (Test-Path -LiteralPath $nodeArchive)) {
    Invoke-WebRequest `
        -Uri "https://nodejs.org/download/release/v$nodeVersion/$nodeArchiveName" `
        -OutFile $nodeArchive
}

$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $nodeArchive).Hash.ToLowerInvariant()
if ($archiveHash -ne $nodeArchiveSha256) {
    throw "Node.js archive SHA-256 mismatch. Expected $nodeArchiveSha256, got $archiveHash."
}

if (-not (Test-Path -LiteralPath (Join-Path $nodeExtractRoot 'node.exe'))) {
    if (Test-Path -LiteralPath $nodeExtractRoot) {
        throw "Incomplete Node.js cache exists and will not be overwritten: $nodeExtractRoot"
    }

    Expand-Archive -LiteralPath $nodeArchive -DestinationPath $cacheRoot
}

$nodeExecutable = Join-Path $nodeExtractRoot 'node.exe'
$actualNodeVersion = (& $nodeExecutable --version).Trim()
if ($LASTEXITCODE -ne 0 -or $actualNodeVersion -ne "v$nodeVersion") {
    throw "Pinned Node.js verification failed. Actual version: $actualNodeVersion"
}

Invoke-Checked $dotnet @(
    'publish',
    (Join-Path $projectRoot 'src\CodexThemeStudio.Desktop\CodexThemeStudio.Desktop.csproj'),
    '--configuration', 'Release',
    '--runtime', $runtimeIdentifier,
    '--self-contained', 'true',
    '--no-restore',
    '--disable-build-servers',
    '-m:1',
    '--output', $desktopPublish,
    '-p:RestoreLockedMode=true',
    '-p:UseSharedCompilation=false',
    "-p:Version=$Version",
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)
Invoke-Checked $dotnet @(
    'publish',
    (Join-Path $projectRoot 'src\CodexThemeStudio.Agent\CodexThemeStudio.Agent.csproj'),
    '--configuration', 'Release',
    '--runtime', $runtimeIdentifier,
    '--self-contained', 'true',
    '--no-restore',
    '--disable-build-servers',
    '-m:1',
    '--output', $agentPublish,
    '-p:RestoreLockedMode=true',
    '-p:UseSharedCompilation=false',
    "-p:Version=$Version",
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Copy-TreeWithoutOverwriteConflict -Source $desktopPublish -Destination $packageDirectory
Copy-TreeWithoutOverwriteConflict `
    -Source $agentPublish `
    -Destination (Join-Path $packageDirectory 'agent')

$publishedDesktopExecutable = Join-Path $packageDirectory 'CodexThemeStudio.Desktop.exe'
$renamedDesktopExecutable = Join-Path $packageDirectory 'CodexThemeManager.exe'
if (-not (Test-Path -LiteralPath $publishedDesktopExecutable -PathType Leaf)) {
    throw "Published Desktop executable is missing: $publishedDesktopExecutable"
}
Move-Item -LiteralPath $publishedDesktopExecutable -Destination $renamedDesktopExecutable

$runtimeRoot = Join-Path $packageDirectory 'runtime'
$injectorRoot = Join-Path $runtimeRoot 'injector'
$nodeRoot = Join-Path $runtimeRoot 'node'
New-Item -ItemType Directory -Force -Path $injectorRoot, $nodeRoot | Out-Null
Copy-RequiredFile $nodeExecutable (Join-Path $nodeRoot 'node.exe')

$injectorFiles = @(
    'index.mjs',
    'main-runtime.mjs',
    'renderer-payload.mjs',
    'renderer-runtime.mjs',
    'security.mjs',
    'windows-discovery.ps1'
)
foreach ($name in $injectorFiles) {
    Copy-RequiredFile `
        (Join-Path $projectRoot "runtime\injector\$name") `
        (Join-Path $injectorRoot $name)
}

Copy-RequiredFile `
    (Join-Path $projectRoot 'README.md') `
    (Join-Path $packageDirectory 'README.md')
Copy-RequiredFile `
    (Join-Path $projectRoot 'LICENSE') `
    (Join-Path $packageDirectory 'LICENSE')
Copy-RequiredFile `
    (Join-Path $projectRoot 'docs\user-guide.md') `
    (Join-Path $packageDirectory 'docs\user-guide.md')
Copy-RequiredFile `
    (Join-Path $projectRoot 'docs\building.md') `
    (Join-Path $packageDirectory 'docs\building.md')
Copy-RequiredFile `
    (Join-Path $projectRoot 'docs\releasing.md') `
    (Join-Path $packageDirectory 'docs\releasing.md')
Copy-RequiredFile `
    (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') `
    (Join-Path $packageDirectory 'THIRD-PARTY-NOTICES.md')
Copy-RequiredFile `
    (Join-Path $projectRoot 'src\CodexThemeStudio.Desktop\Assets\app-icon.png') `
    (Join-Path $packageDirectory 'assets\app-icon.png')

$licensesRoot = Join-Path $packageDirectory 'LICENSES'
New-Item -ItemType Directory -Force -Path $licensesRoot | Out-Null
Copy-RequiredFile `
    (Join-Path $nodeExtractRoot 'LICENSE') `
    (Join-Path $licensesRoot 'Node.js-LICENSE.txt')
Copy-RequiredFile `
    (Join-Path $dotnetRoot 'LICENSE.txt') `
    (Join-Path $licensesRoot 'dotnet-LICENSE.txt')
Copy-RequiredFile `
    (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') `
    (Join-Path $licensesRoot 'dotnet-THIRD-PARTY-NOTICES.txt')

$globalPackages = if ($env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES
}
else {
    Join-Path $env:USERPROFILE '.nuget\packages'
}
Copy-RequiredFile `
    (Join-Path $globalPackages 'sourcegear.sqlite3\3.50.4.5\LICENSE.txt') `
    (Join-Path $licensesRoot 'SourceGear.sqlite3-LICENSE.txt')
Copy-RequiredFile `
    (Join-Path $globalPackages 'skiasharp\4.150.1\LICENSE.txt') `
    (Join-Path $licensesRoot 'SkiaSharp-LICENSE.txt')
Copy-RequiredFile `
    (Join-Path $globalPackages 'skiasharp.nativeassets.win32\4.150.1\THIRD-PARTY-NOTICES.txt') `
    (Join-Path $licensesRoot 'SkiaSharp-THIRD-PARTY-NOTICES.txt')
Copy-RequiredFile `
    (Join-Path $projectRoot 'third_party\Lucide\LICENSE.txt') `
    (Join-Path $licensesRoot 'Lucide-LICENSE.txt')

$licenseCacheRoot = Join-Path $cacheRoot 'licenses'
New-Item -ItemType Directory -Force -Path $licenseCacheRoot | Out-Null
$mitLicenseCache = Join-Path $licenseCacheRoot 'MIT.txt'
$apacheLicenseCache = Join-Path $licenseCacheRoot 'Apache-2.0.txt'
if (-not (Test-Path -LiteralPath $mitLicenseCache -PathType Leaf)) {
    Invoke-WebRequest -Uri 'https://licenses.nuget.org/MIT' `
        -OutFile $mitLicenseCache
}
if (-not (Test-Path -LiteralPath $apacheLicenseCache -PathType Leaf)) {
    Invoke-WebRequest -Uri 'https://www.apache.org/licenses/LICENSE-2.0.txt' `
        -OutFile $apacheLicenseCache
}
Copy-RequiredFile $mitLicenseCache (Join-Path $licensesRoot 'MIT.txt')
Copy-RequiredFile $apacheLicenseCache (Join-Path $licensesRoot 'Apache-2.0.txt')

$mainExecutable = $renamedDesktopExecutable
$agentExecutable = Join-Path $packageDirectory 'agent\CodexThemeStudio.Agent.exe'
if (-not (Test-Path -LiteralPath $mainExecutable -PathType Leaf)) {
    throw "Published main executable is missing: $mainExecutable"
}
if (-not (Test-Path -LiteralPath $agentExecutable -PathType Leaf)) {
    throw "Published Agent executable is missing: $agentExecutable"
}

$buildInfo = @"
# Build information

- Product: Codex Theme Studio
- Version: $Version
- Target: Windows x64
- Configuration: Release
- .NET: self-contained, runtime 8.0.29
- Node.js: v$nodeVersion
- Node.js archive SHA-256: $nodeArchiveSha256
- Signing: unsigned
"@
[IO.File]::WriteAllText(
    (Join-Path $packageDirectory 'BUILD-INFO.md'),
    $buildInfo,
    [Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal

$packageFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File)
$packageBytes = ($packageFiles | Measure-Object -Property Length -Sum).Sum
$zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$mainHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $mainExecutable).Hash.ToLowerInvariant()
$agentHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $agentExecutable).Hash.ToLowerInvariant()
$checksums = @(
    "$zipHash *$([IO.Path]::GetFileName($zipPath))",
    "$mainHash *$packageName/CodexThemeManager.exe",
    "$agentHash *$packageName/agent/CodexThemeStudio.Agent.exe"
)
[IO.File]::WriteAllLines(
    (Join-Path $releaseRoot 'SHA256SUMS.txt'),
    $checksums,
    [Text.UTF8Encoding]::new($false))

$metadata = [ordered]@{
    schemaVersion = 1
    product = 'Codex Theme Studio'
    version = $Version
    packageName = $packageName
    runtimeIdentifier = $runtimeIdentifier
    nodeVersion = "v$nodeVersion"
    fileCount = $packageFiles.Count
    uncompressedBytes = $packageBytes
    zipBytes = (Get-Item -LiteralPath $zipPath).Length
    zipSha256 = $zipHash
    mainExecutableSha256 = $mainHash
    agentExecutableSha256 = $agentHash
    signed = $false
}
[IO.File]::WriteAllText(
    (Join-Path $releaseRoot 'release-manifest.json'),
    ($metadata | ConvertTo-Json -Depth 4),
    [Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $finalReleaseRoot) |
    Out-Null
Move-Item -LiteralPath $releaseRoot -Destination $finalReleaseRoot
$packageCompleted = $true

Write-Host "Portable package: $(Join-Path $finalReleaseRoot $packageName)"
Write-Host "ZIP: $(Join-Path $finalReleaseRoot "$packageName.zip")"
Write-Host "Files: $($packageFiles.Count)"
Write-Host "Uncompressed bytes: $packageBytes"
Write-Host "ZIP SHA-256: $zipHash"
}
finally {
    if ($packageCompleted) {
        if (Test-Path -LiteralPath $workRoot) {
            Remove-Item -LiteralPath $workRoot -Recurse -Force
        }
    }
    else {
        Write-Warning "Packaging did not complete. Diagnostic work directory retained: $workRoot"
    }
}
