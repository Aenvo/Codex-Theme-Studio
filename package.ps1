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
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw "Release version must use major.minor.patch or a SemVer prerelease format. Actual: $Version"
}
$runtimeBaseline = Get-Content -Raw -LiteralPath (
    Join-Path $projectRoot 'eng\runtime-baseline.json') |
    ConvertFrom-Json
$runtimeIdentifier = [string]$runtimeBaseline.runtimeIdentifier
$nodeVersion = [string]$runtimeBaseline.nodeVersion
$nodeArchiveName = "node-v$nodeVersion-$runtimeIdentifier.zip"
$nodeArchiveSha256 = [string]$runtimeBaseline.nodeArchiveSha256
$targetZipBytes = 100000000L
$maximumZipBytes = 120000000L
$artifactRoot = Join-Path $projectRoot 'artifacts'
$cacheRoot = Join-Path $artifactRoot 'cache'
$finalReleaseRoot = Join-Path $artifactRoot "release\$Version"
$packageName = "Codex-Theme-Studio-$Version-win-x64-portable"

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
            $sourceHash = Get-Sha256Lower $file.FullName
            $destinationHash = Get-Sha256Lower $destinationPath
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

function Get-RelativePublishPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish file escaped its source root: $fullPath"
    }

    return $fullPath.Substring($prefix.Length).Replace('\', '/')
}

function Get-Sha256Lower {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString(
                $algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-NoPrivateThemeAssets {
    param([Parameter(Mandatory = $true)][string]$PackageRoot)

    $blockedThemeNames = @('鸣潮', '生化危机2', '刺客信条')
    $blockedImageHashes = @(
        'ab99c8ac4be85a276393e5a6823ef5986af9551e39822de1a88f000e962a1ca6',
        'ae091d3608b37b098c1e559011c219fc4b82e872b81d3795f3d71cc506dfaca0',
        '20997fa0d25211359dd5f24a0ccaf6778fb47407b74471a4af722cec34f3d607'
    )
    $textExtensions = @(
        '.css', '.js', '.json', '.md', '.mjs', '.ps1', '.txt', '.xaml', '.xml')
    $violations = [Collections.Generic.List[string]]::new()

    foreach ($file in Get-ChildItem -LiteralPath $PackageRoot -Recurse -File) {
        $relativePath = Get-RelativePublishPath -Root $PackageRoot -Path $file.FullName
        if ($file.Name -ieq 'themes.db' -or
            $file.Name -ieq 'theme.json' -or
            $file.Extension -ieq '.cttheme') {
            $violations.Add($relativePath)
            continue
        }

        $hash = Get-Sha256Lower $file.FullName
        if ($blockedImageHashes -contains $hash) {
            $violations.Add("$relativePath (blocked test wallpaper hash)")
            continue
        }

        if ($textExtensions -contains $file.Extension.ToLowerInvariant()) {
            $content = [IO.File]::ReadAllText($file.FullName)
            foreach ($themeName in $blockedThemeNames) {
                if ($content.IndexOf(
                    $themeName,
                    [StringComparison]::Ordinal) -ge 0) {
                    $violations.Add("$relativePath (blocked test theme name)")
                    break
                }
            }
        }
    }

    if ($violations.Count -gt 0) {
        throw "Release contains private theme data or third-party test wallpapers: $(
            $violations -join ', ')"
    }
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

$archiveHash = Get-Sha256Lower $nodeArchive
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
    '-p:SatelliteResourceLanguages=zh-Hans',
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
    '-p:SatelliteResourceLanguages=zh-Hans',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Copy-TreeWithoutOverwriteConflict -Source $desktopPublish -Destination $packageDirectory
$unexpectedSatelliteLanguages = @(
    'cs', 'de', 'es', 'fr', 'it', 'ja', 'ko', 'pl',
    'pt-BR', 'ru', 'tr', 'zh-Hant')
$unexpectedSatelliteDirectories = @(
    Get-ChildItem -LiteralPath $packageDirectory -Directory |
        Where-Object Name -In $unexpectedSatelliteLanguages)
if ($unexpectedSatelliteDirectories.Count -gt 0) {
    throw "Unexpected satellite language directories were published: $(
        ($unexpectedSatelliteDirectories.Name -join ', '))"
}

$agentPackageRoot = Join-Path $packageDirectory 'agent'
New-Item -ItemType Directory -Force -Path $agentPackageRoot | Out-Null
$agentManifestFiles = @()
$sharedAgentRuntimeBytes = [int64]0
$agentUniqueBytes = [int64]0
foreach ($file in Get-ChildItem -LiteralPath $agentPublish -Recurse -File) {
    if ($file.Extension -ieq '.pdb') {
        continue
    }

    $relativePath = Get-RelativePublishPath `
        -Root $agentPublish `
        -Path $file.FullName
    $desktopPeer = Join-Path $desktopPublish $relativePath
    $sourceRelativePath = $null
    $fileHash = Get-Sha256Lower $file.FullName
    if ((Test-Path -LiteralPath $desktopPeer -PathType Leaf) -and
        (Get-Item -LiteralPath $desktopPeer).Length -eq $file.Length -and
        (Get-Sha256Lower $desktopPeer) -eq $fileHash) {
        $sourceRelativePath = $relativePath
        $sharedAgentRuntimeBytes += [int64]$file.Length
    }
    else {
        $sourceRelativePath = "agent/$relativePath"
        Copy-RequiredFile `
            $file.FullName `
            (Join-Path $agentPackageRoot $relativePath)
        $agentUniqueBytes += [int64]$file.Length
    }

    $agentManifestFiles += [pscustomobject][ordered]@{
        source = $sourceRelativePath
        destination = $relativePath
        bytes = [int64]$file.Length
        sha256 = $fileHash
    }
}

$publishedDesktopExecutable = Join-Path $packageDirectory 'CodexThemeStudio.Desktop.exe'
$renamedDesktopExecutable = Join-Path $packageDirectory 'CodexThemeManager.exe'
if (-not (Test-Path -LiteralPath $publishedDesktopExecutable -PathType Leaf)) {
    throw "Published Desktop executable is missing: $publishedDesktopExecutable"
}
Move-Item -LiteralPath $publishedDesktopExecutable -Destination $renamedDesktopExecutable

$runtimeRoot = Join-Path $packageDirectory 'runtime'
$injectorRoot = Join-Path $runtimeRoot 'injector'
$nodeRoot = Join-Path $runtimeRoot 'node'
$updaterRoot = Join-Path $runtimeRoot 'updater'
New-Item -ItemType Directory -Force -Path $injectorRoot, $nodeRoot, $updaterRoot | Out-Null
Copy-RequiredFile $nodeExecutable (Join-Path $nodeRoot 'node.exe')
Copy-RequiredFile `
    (Join-Path $projectRoot 'runtime\updater\apply-update.mjs') `
    (Join-Path $updaterRoot 'apply-update.mjs')

$injectorFiles = @(
    'index.mjs',
    'inspector-lifecycle.mjs',
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

$runtimeAgentFiles = @(
    Get-ChildItem -LiteralPath $nodeRoot, $injectorRoot -Recurse -File)
foreach ($file in $runtimeAgentFiles) {
    $relativePath = Get-RelativePublishPath `
        -Root $packageDirectory `
        -Path $file.FullName
    $agentManifestFiles += [pscustomobject][ordered]@{
        source = $relativePath
        destination = $relativePath
        bytes = [int64]$file.Length
        sha256 = Get-Sha256Lower $file.FullName
    }
}

$duplicateAgentDestinations = @(
    $agentManifestFiles |
        Group-Object destination |
        Where-Object Count -gt 1)
if ($duplicateAgentDestinations.Count -gt 0) {
    throw "Agent bundle manifest contains duplicate destinations: $(
        ($duplicateAgentDestinations.Name -join ', '))"
}

$agentBundleManifest = [ordered]@{
    schemaVersion = 1
    files = @($agentManifestFiles | Sort-Object destination)
}
[IO.File]::WriteAllText(
    (Join-Path $agentPackageRoot 'agent-bundle-manifest.json'),
    ($agentBundleManifest | ConvertTo-Json -Depth 5),
    [Text.UTF8Encoding]::new($false))

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

Assert-NoPrivateThemeAssets -PackageRoot $packageDirectory

$installManifestFiles = @(
    foreach ($file in Get-ChildItem -LiteralPath $packageDirectory -Recurse -File |
        Sort-Object FullName) {
        [pscustomobject][ordered]@{
            path = (Get-RelativePublishPath `
                -Root $packageDirectory `
                -Path $file.FullName).Replace('\', '/')
            bytes = [int64]$file.Length
            sha256 = Get-Sha256Lower $file.FullName
        }
    })
$installManifest = [ordered]@{
    schemaVersion = 1
    product = 'Codex Theme Studio'
    version = $Version
    files = $installManifestFiles
}
$installManifestPath = Join-Path $packageDirectory 'app-install-manifest.json'
[IO.File]::WriteAllText(
    $installManifestPath,
    ($installManifest | ConvertTo-Json -Depth 5),
    [Text.UTF8Encoding]::new($false))
$installManifestHash = Get-Sha256Lower $installManifestPath

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal

$packageFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File)
$packageBytes = [int64](
    ($packageFiles | Measure-Object -Property Length -Sum).Sum)
$zipBytes = (Get-Item -LiteralPath $zipPath).Length
if ($zipBytes -gt $maximumZipBytes) {
    throw "Release ZIP exceeds the hard size limit of $maximumZipBytes bytes. Actual: $zipBytes"
}
if ($zipBytes -gt $targetZipBytes) {
    Write-Warning "Release ZIP exceeds the preferred target of $targetZipBytes bytes. Actual: $zipBytes"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $compressedAreas = @{
        desktopAndSupport = [int64]0
        agentUnique = [int64]0
        node = [int64]0
        injector = [int64]0
    }
    foreach ($entry in $zip.Entries) {
        $relative = $entry.FullName.Replace('\', '/')
        $prefix = "$packageName/"
        if ($relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            $relative = $relative.Substring($prefix.Length)
        }

        if ($relative.StartsWith('agent/', [StringComparison]::OrdinalIgnoreCase)) {
            $compressedAreas.agentUnique += [int64]$entry.CompressedLength
        }
        elseif ($relative.StartsWith(
            'runtime/node/',
            [StringComparison]::OrdinalIgnoreCase)) {
            $compressedAreas.node += [int64]$entry.CompressedLength
        }
        elseif ($relative.StartsWith(
            'runtime/injector/',
            [StringComparison]::OrdinalIgnoreCase)) {
            $compressedAreas.injector += [int64]$entry.CompressedLength
        }
        else {
            $compressedAreas.desktopAndSupport += [int64]$entry.CompressedLength
        }
    }
}
finally {
    $zip.Dispose()
}

$nodeBytes = (
    Get-ChildItem -LiteralPath $nodeRoot -Recurse -File |
        Measure-Object -Property Length -Sum).Sum
$injectorBytes = (
    Get-ChildItem -LiteralPath $injectorRoot -Recurse -File |
        Measure-Object -Property Length -Sum).Sum
$zipHash = Get-Sha256Lower $zipPath
$mainHash = Get-Sha256Lower $mainExecutable
$agentHash = Get-Sha256Lower $agentExecutable
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
    schemaVersion = 3
    product = 'Codex Theme Studio'
    version = $Version
    packageName = $packageName
    runtimeIdentifier = $runtimeIdentifier
    nodeVersion = "v$nodeVersion"
    fileCount = $packageFiles.Count
    uncompressedBytes = $packageBytes
    zipBytes = $zipBytes
    preferredZipBytes = $targetZipBytes
    maximumZipBytes = $maximumZipBytes
    zipSha256 = $zipHash
    installManifestSha256 = $installManifestHash
    mainExecutableSha256 = $mainHash
    agentExecutableSha256 = $agentHash
    components = [ordered]@{
        desktopAndSupportCompressedBytes = $compressedAreas.desktopAndSupport
        agentUniqueBytes = $agentUniqueBytes
        agentUniqueCompressedBytes = $compressedAreas.agentUnique
        sharedAgentRuntimeBytes = $sharedAgentRuntimeBytes
        sharedAgentRuntimeStoredBytes = 0
        nodeBytes = [int64]$nodeBytes
        nodeCompressedBytes = $compressedAreas.node
        injectorBytes = [int64]$injectorBytes
        injectorCompressedBytes = $compressedAreas.injector
    }
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
Write-Host "ZIP bytes: $zipBytes"
Write-Host "Shared Agent runtime bytes removed: $sharedAgentRuntimeBytes"
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
