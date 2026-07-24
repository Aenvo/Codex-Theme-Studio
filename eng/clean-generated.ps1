[CmdletBinding()]
param(
    [switch]$Execute,

    [string]$ConfirmCleanup,

    [ValidateSet('ArchivesOnly')]
    [string]$ReleaseRetention,

    [string]$EvidenceBackupRoot,

    [switch]$Resume
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$confirmationPhrase = 'RECYCLE_CODEX_THEME_STUDIO_GENERATED_OUTPUTS'
$projectRoot = ([IO.Path]::GetFullPath(
        (Split-Path -Parent $PSScriptRoot))).TrimEnd(
            [IO.Path]::DirectorySeparatorChar)
$evidenceExtensions = @('.png', '.jpg', '.jpeg', '.webp', '.trx', '.log', '.md')
$recycleReserveBytes = [int64](2GB)
$backupReserveBytes = [int64](100MB)

function Format-ByteSize {
    param([Parameter(Mandatory = $true)][int64]$Bytes)

    if ($Bytes -ge 1GB) {
        return '{0:N3} GiB' -f ($Bytes / 1GB)
    }

    if ($Bytes -ge 1MB) {
        return '{0:N2} MiB' -f ($Bytes / 1MB)
    }

    if ($Bytes -ge 1KB) {
        return '{0:N2} KiB' -f ($Bytes / 1KB)
    }

    return "$Bytes bytes"
}

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return ([IO.Path]::GetFullPath($Path)).TrimEnd(
        [IO.Path]::DirectorySeparatorChar)
}

function Get-RelativeProjectPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = Get-NormalizedPath $Path
    $prefix = $projectRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $normalized.StartsWith(
            $prefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the project root: $normalized"
    }

    return $normalized.Substring($prefix.Length)
}

function Test-PathIsWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $normalizedPath = Get-NormalizedPath $Path
    $normalizedParent = Get-NormalizedPath $Parent
    $prefix = $normalizedParent + [IO.Path]::DirectorySeparatorChar
    return $normalizedPath.StartsWith(
        $prefix,
        [StringComparison]::OrdinalIgnoreCase)
}

function Get-DirectorySummary {
    param([Parameter(Mandatory = $true)][string]$Path)

    $files = @(Get-ChildItem -LiteralPath $Path -Force -File -Recurse)
    $bytes = [int64]0
    foreach ($file in $files) {
        $bytes += [int64]$file.Length
    }

    return [pscustomobject]@{
        Files = [int64]$files.Count
        Bytes = $bytes
    }
}

function Assert-CleanupTarget {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = Get-NormalizedPath $Path
    $relative = Get-RelativeProjectPath $normalized

    if (-not (Test-Path -LiteralPath $normalized -PathType Container)) {
        throw "Cleanup target is not an existing directory: $normalized"
    }

    $item = Get-Item -LiteralPath $normalized -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Cleanup target is a reparse point: $normalized"
    }

    $reparsePoints = @(
        Get-ChildItem -LiteralPath $normalized -Force -Recurse |
            Where-Object {
                ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
            })
    if ($reparsePoints.Count -gt 0) {
        throw "Cleanup target contains a reparse point: $($reparsePoints[0].FullName)"
    }

    & git -C $projectRoot check-ignore --quiet -- $relative
    if ($LASTEXITCODE -ne 0) {
        throw "Cleanup target is not ignored by Git: $relative"
    }

    $tracked = @(& git -C $projectRoot ls-files -- $relative)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed for cleanup target: $relative"
    }

    if ($tracked.Count -gt 0) {
        throw "Cleanup target contains tracked files: $relative"
    }
}

function New-CleanupTarget {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    Assert-CleanupTarget $Path
    $summary = Get-DirectorySummary $Path
    return [pscustomobject]@{
        Kind = $Kind
        Path = Get-NormalizedPath $Path
        RelativePath = Get-RelativeProjectPath $Path
        Files = $summary.Files
        Bytes = $summary.Bytes
    }
}

function Get-ArchiveVerification {
    param([Parameter(Mandatory = $true)][string]$VersionDirectory)

    $versionName = Split-Path -Leaf $VersionDirectory
    $checksumPath = Join-Path $VersionDirectory 'SHA256SUMS.txt'
    $manifestPath = Join-Path $VersionDirectory 'release-manifest.json'
    $portableDirectories = @(
        Get-ChildItem -LiteralPath $VersionDirectory -Directory |
            Where-Object Name -Like 'CodexThemeManager-*-win-x64-portable')

    $result = [ordered]@{
        Version = $versionName
        IsValid = $false
        Reason = $null
        ZipPath = $null
        PortablePath = $null
    }

    if ($portableDirectories.Count -eq 1) {
        $result.PortablePath = $portableDirectories[0].FullName
    }
    elseif ($portableDirectories.Count -gt 1) {
        $result.Reason = 'More than one expanded portable directory exists.'
        return [pscustomobject]$result
    }

    if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        $result.Reason = 'SHA256SUMS.txt is missing.'
        return [pscustomobject]$result
    }

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $result.Reason = 'release-manifest.json is missing.'
        return [pscustomobject]$result
    }

    try {
        $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath |
            ConvertFrom-Json
    }
    catch {
        $result.Reason = "Release manifest is invalid JSON: $($_.Exception.Message)"
        return [pscustomobject]$result
    }

    if ([string]$manifest.version -ne $versionName) {
        $result.Reason = 'Release manifest version does not match its directory.'
        return [pscustomobject]$result
    }

    $zipName = "$([string]$manifest.packageName).zip"
    $zipPath = Join-Path $VersionDirectory $zipName
    $result.ZipPath = $zipPath
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        $result.Reason = "Release ZIP is missing: $zipName"
        return [pscustomobject]$result
    }

    $zipChecksum = $null
    foreach ($line in Get-Content -Encoding UTF8 -LiteralPath $checksumPath) {
        if ($line -match '^([0-9A-Fa-f]{64}) \*(.+\.zip)$' -and
            $Matches[2] -eq $zipName) {
            $zipChecksum = $Matches[1].ToLowerInvariant()
            break
        }
    }

    if (-not $zipChecksum) {
        $result.Reason = 'The ZIP checksum entry is missing.'
        return [pscustomobject]$result
    }

    $actualZip = Get-Item -LiteralPath $zipPath
    if ([int64]$manifest.zipBytes -ne [int64]$actualZip.Length) {
        $result.Reason = 'The ZIP byte count does not match the release manifest.'
        return [pscustomobject]$result
    }

    $actualHash = (Get-FileHash `
            -Algorithm SHA256 `
            -LiteralPath $zipPath).Hash.ToLowerInvariant()
    if ($actualHash -ne $zipChecksum -or
        $actualHash -ne ([string]$manifest.zipSha256).ToLowerInvariant()) {
        $result.Reason = 'The ZIP SHA-256 does not match its metadata.'
        return [pscustomobject]$result
    }

    if ($result.PortablePath) {
        $portableName = Split-Path -Leaf $result.PortablePath
        if ($portableName -ne [string]$manifest.packageName) {
            $result.Reason = 'Expanded portable directory does not match the manifest.'
            return [pscustomobject]$result
        }
    }

    $result.IsValid = $true
    $result.Reason = 'ZIP byte count and SHA-256 verified.'
    return [pscustomobject]$result
}

function Get-EvidenceFiles {
    $evidence = @{}
    $validationRoot = Join-Path $projectRoot 'artifacts\validation'
    $workRoot = Join-Path $projectRoot 'artifacts\work'

    if (Test-Path -LiteralPath $validationRoot -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $validationRoot -Force -File -Recurse) {
            $relative = Get-RelativeProjectPath $file.FullName
            $evidence[$relative.ToLowerInvariant()] = [pscustomobject]@{
                SourcePath = $file.FullName
                RelativePath = $relative
                Bytes = [int64]$file.Length
            }
        }
    }

    if (Test-Path -LiteralPath $workRoot -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $workRoot -Force -File -Recurse) {
            if ($evidenceExtensions -notcontains $file.Extension.ToLowerInvariant()) {
                continue
            }

            $relative = Get-RelativeProjectPath $file.FullName
            $evidence[$relative.ToLowerInvariant()] = [pscustomobject]@{
                SourcePath = $file.FullName
                RelativePath = $relative
                Bytes = [int64]$file.Length
            }
        }
    }

    return @($evidence.Values | Sort-Object RelativePath)
}

function Backup-Evidence {
    param(
        [Parameter(Mandatory = $true)][object[]]$Evidence,
        [Parameter(Mandatory = $true)][string]$BackupRoot
    )

    $normalizedBackup = Get-NormalizedPath $BackupRoot
    if (Test-PathIsWithin -Path $normalizedBackup -Parent $projectRoot) {
        throw 'Evidence backup root must be outside the project.'
    }

    if (Test-Path -LiteralPath $normalizedBackup) {
        throw "Evidence backup root already exists: $normalizedBackup"
    }

    $backupParent = Split-Path -Parent $normalizedBackup
    if (-not (Test-Path -LiteralPath $backupParent -PathType Container)) {
        throw "Evidence backup parent does not exist: $backupParent"
    }

    $qualifier = Split-Path -Qualifier $normalizedBackup
    if (-not $qualifier) {
        throw 'Evidence backup root must use an absolute drive path.'
    }

    $driveName = ($qualifier.TrimEnd(
            [IO.Path]::DirectorySeparatorChar)).TrimEnd([char]':')
    $drive = Get-PSDrive -Name $driveName -ErrorAction Stop
    $evidenceBytes = [int64]0
    foreach ($item in $Evidence) {
        $evidenceBytes += [int64]$item.Bytes
    }

    if ([int64]$drive.Free -lt ($evidenceBytes + $backupReserveBytes)) {
        throw "Evidence backup drive does not have sufficient free space: $qualifier"
    }

    Write-Host "Evidence backup: $normalizedBackup"
    Write-Host "Evidence files: $($Evidence.Count)"
    Write-Host "Evidence bytes: $evidenceBytes ($(Format-ByteSize $evidenceBytes))"
    Write-Host "Backup drive free: $([int64]$drive.Free) ($(Format-ByteSize $drive.Free))"

    New-Item -ItemType Directory -Path $normalizedBackup | Out-Null
    $entries = @()
    foreach ($item in $Evidence) {
        $destination = Join-Path $normalizedBackup $item.RelativePath
        $destinationParent = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
        }

        Copy-Item -LiteralPath $item.SourcePath -Destination $destination
        $sourceHash = (Get-FileHash `
                -Algorithm SHA256 `
                -LiteralPath $item.SourcePath).Hash.ToLowerInvariant()
        $destinationInfo = Get-Item -LiteralPath $destination
        $destinationHash = (Get-FileHash `
                -Algorithm SHA256 `
                -LiteralPath $destination).Hash.ToLowerInvariant()
        if ([int64]$destinationInfo.Length -ne [int64]$item.Bytes -or
            $destinationHash -ne $sourceHash) {
            throw "Evidence backup verification failed: $($item.RelativePath)"
        }

        $entries += [ordered]@{
            path = $item.RelativePath.Replace('\', '/')
            bytes = [int64]$item.Bytes
            sha256 = $sourceHash
        }
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        createdUtc = [DateTime]::UtcNow.ToString('o')
        projectName = 'Codex Theme Studio'
        projectRoot = $projectRoot
        fileCount = [int64]$entries.Count
        totalBytes = $evidenceBytes
        files = $entries
    }
    $manifestPath = Join-Path $normalizedBackup 'cleanup-evidence-manifest.json'
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))

    $verifiedFiles = @(
        Get-ChildItem -LiteralPath $normalizedBackup -Force -File -Recurse |
            Where-Object FullName -ne $manifestPath)
    $verifiedBytes = [int64]0
    foreach ($file in $verifiedFiles) {
        $verifiedBytes += [int64]$file.Length
    }

    if ($verifiedFiles.Count -ne $Evidence.Count -or
        $verifiedBytes -ne $evidenceBytes) {
        throw 'Evidence backup aggregate verification failed.'
    }

    return [pscustomobject]@{
        Root = $normalizedBackup
        ManifestPath = $manifestPath
        Files = [int64]$verifiedFiles.Count
        Bytes = $verifiedBytes
    }
}

function Assert-EvidenceBackup {
    param([Parameter(Mandatory = $true)][string]$BackupRoot)

    $normalizedBackup = Get-NormalizedPath $BackupRoot
    $manifestPath = Join-Path $normalizedBackup 'cleanup-evidence-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Existing evidence manifest is missing: $manifestPath"
    }

    try {
        $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath |
            ConvertFrom-Json
    }
    catch {
        throw "Existing evidence manifest is invalid: $($_.Exception.Message)"
    }

    if ([string]$manifest.projectRoot -ne $projectRoot) {
        throw 'Existing evidence manifest belongs to a different project root.'
    }

    $verifiedBytes = [int64]0
    $verifiedPaths = @{}
    foreach ($entry in @($manifest.files)) {
        $relative = ([string]$entry.path).Replace('/', '\')
        $destination = Get-NormalizedPath (Join-Path $normalizedBackup $relative)
        if (-not (Test-PathIsWithin -Path $destination -Parent $normalizedBackup)) {
            throw "Existing evidence manifest path escapes the backup root: $relative"
        }

        if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
            throw "Existing evidence file is missing: $relative"
        }

        $destinationInfo = Get-Item -LiteralPath $destination
        $destinationHash = (Get-FileHash `
                -Algorithm SHA256 `
                -LiteralPath $destination).Hash.ToLowerInvariant()
        if ([int64]$destinationInfo.Length -ne [int64]$entry.bytes -or
            $destinationHash -ne ([string]$entry.sha256).ToLowerInvariant()) {
            throw "Existing evidence file verification failed: $relative"
        }

        $verifiedBytes += [int64]$destinationInfo.Length
        $verifiedPaths[$destination.ToLowerInvariant()] = $true
    }

    $actualFiles = @(
        Get-ChildItem -LiteralPath $normalizedBackup -Force -File -Recurse |
            Where-Object FullName -ne $manifestPath)
    foreach ($file in $actualFiles) {
        if (-not $verifiedPaths.ContainsKey($file.FullName.ToLowerInvariant())) {
            throw "Unexpected file exists in the evidence backup: $($file.FullName)"
        }
    }

    if ($actualFiles.Count -ne [int64]$manifest.fileCount -or
        $verifiedBytes -ne [int64]$manifest.totalBytes) {
        throw 'Existing evidence backup aggregate verification failed.'
    }

    return [pscustomobject]@{
        Root = $normalizedBackup
        ManifestPath = $manifestPath
        Files = [int64]$actualFiles.Count
        Bytes = $verifiedBytes
    }
}

function Get-RecycleContext {
    $driveRoot = [IO.Path]::GetPathRoot($projectRoot)
    $mountPoint = $driveRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $volumeOutput = @(& mountvol $mountPoint /L)
    $volumePath = @(
        $volumeOutput |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -match '^\\\\\?\\Volume\{[0-9A-Fa-f-]+\}\\$' } |
            Select-Object -First 1)
    if ($volumePath.Count -ne 1) {
        throw "Could not resolve the volume GUID for $driveRoot"
    }

    if ($volumePath[0] -notmatch 'Volume(\{[0-9A-Fa-f-]+\})') {
        throw "Could not parse the volume GUID for $driveRoot"
    }

    $volumeGuid = $Matches[1]
    $registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\$volumeGuid"
    if (-not (Test-Path -LiteralPath $registryPath)) {
        throw "Recycle Bin capacity settings are missing for $volumeGuid"
    }

    $settings = Get-ItemProperty -LiteralPath $registryPath
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $recycleRoot = Join-Path $driveRoot "`$Recycle.Bin\$sid"
    if (-not (Test-Path -LiteralPath $recycleRoot -PathType Container)) {
        throw "Current-user Recycle Bin directory is missing: $recycleRoot"
    }

    $recycleFiles = @(Get-ChildItem -LiteralPath $recycleRoot -Force -File -Recurse)
    $currentBytes = [int64]0
    foreach ($file in $recycleFiles) {
        $currentBytes += [int64]$file.Length
    }

    return [pscustomobject]@{
        DriveRoot = $driveRoot
        VolumeGuid = $volumeGuid
        RecycleRoot = $recycleRoot
        CurrentFiles = [int64]$recycleFiles.Count
        CurrentBytes = $currentBytes
        MaxBytes = [int64]$settings.MaxCapacity * 1MB
        NukeOnDelete = [int]$settings.NukeOnDelete
    }
}

function Get-RecycleMetadata {
    param([Parameter(Mandatory = $true)][string]$MetadataPath)

    $bytes = [IO.File]::ReadAllBytes($MetadataPath)
    if ($bytes.Length -lt 24) {
        throw "Recycle metadata is too short: $MetadataPath"
    }

    $version = [BitConverter]::ToInt64($bytes, 0)
    $originalPath = $null
    if ($version -eq 2 -and $bytes.Length -ge 28) {
        $characterCount = [BitConverter]::ToInt32($bytes, 24)
        $availableCharacters = [Math]::Floor(($bytes.Length - 28) / 2)
        if ($characterCount -lt 1 -or $characterCount -gt $availableCharacters) {
            throw "Recycle metadata path length is invalid: $MetadataPath"
        }

        $originalPath = [Text.Encoding]::Unicode.GetString(
            $bytes,
            28,
            $characterCount * 2).TrimEnd([char]0)
    }
    elseif ($version -eq 1 -and $bytes.Length -gt 24) {
        $originalPath = [Text.Encoding]::Unicode.GetString(
            $bytes,
            24,
            $bytes.Length - 24).TrimEnd([char]0)
    }
    else {
        throw "Unsupported Recycle Bin metadata version $version in $MetadataPath"
    }

    return [pscustomobject]@{
        MetadataPath = $MetadataPath
        OriginalPath = $originalPath
        OriginalBytes = [BitConverter]::ToInt64($bytes, 8)
        DeletedFileTime = [BitConverter]::ToInt64($bytes, 16)
    }
}

function Move-DirectoryToRecycleBin {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RecycleRoot
    )

    $before = @{}
    foreach ($file in Get-ChildItem -LiteralPath $RecycleRoot -Force -File -Filter '$I*') {
        $before[$file.Name.ToLowerInvariant()] = $true
    }

    Add-Type -AssemblyName Microsoft.VisualBasic
    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory(
        $Path,
        [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
        [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,
        [Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)

    if (Test-Path -LiteralPath $Path) {
        throw "Recycle operation did not remove the source path: $Path"
    }

    $matchedMetadata = $null
    $matchedDataPath = $null
    for ($attempt = 0; $attempt -lt 25 -and -not $matchedMetadata; $attempt++) {
        foreach ($file in Get-ChildItem -LiteralPath $RecycleRoot -Force -File -Filter '$I*') {
            if ($before.ContainsKey($file.Name.ToLowerInvariant())) {
                continue
            }

            try {
                $metadata = Get-RecycleMetadata $file.FullName
            }
            catch {
                # Explorer may briefly hold a newly created $I file. A matching,
                # readable metadata/data pair is still required before success.
                continue
            }
            if ((Get-NormalizedPath $metadata.OriginalPath) -ne
                (Get-NormalizedPath $Path)) {
                continue
            }

            $dataName = '$R' + $file.Name.Substring(2)
            $dataPath = Join-Path $RecycleRoot $dataName
            if (Test-Path -LiteralPath $dataPath) {
                $matchedMetadata = $metadata
                $matchedDataPath = $dataPath
                break
            }
        }

        if (-not $matchedMetadata) {
            Start-Sleep -Milliseconds 200
        }
    }

    if (-not $matchedMetadata -or -not $matchedDataPath) {
        throw "Independent Recycle Bin verification failed for: $Path"
    }

    return [pscustomobject]@{
        OriginalPath = $matchedMetadata.OriginalPath
        MetadataPath = $matchedMetadata.MetadataPath
        DataPath = $matchedDataPath
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.git') -PathType Container)) {
    throw "Project root is not a Git worktree: $projectRoot"
}

$effectiveRetention = if ($ReleaseRetention) {
    $ReleaseRetention
}
else {
    'ArchivesOnly'
}

if ($Execute) {
    if ($ConfirmCleanup -ne $confirmationPhrase) {
        throw "Execution requires -ConfirmCleanup $confirmationPhrase"
    }

    if ($ReleaseRetention -ne 'ArchivesOnly') {
        throw 'Execution requires -ReleaseRetention ArchivesOnly.'
    }

    if ([string]::IsNullOrWhiteSpace($EvidenceBackupRoot)) {
        throw 'Execution requires -EvidenceBackupRoot.'
    }
}
elseif ($Resume) {
    throw '-Resume requires -Execute.'
}

$archiveResults = @()
$targets = @()
$workPath = Join-Path $projectRoot 'artifacts\work'
$validationPath = Join-Path $projectRoot 'artifacts\validation'

if (Test-Path -LiteralPath $workPath -PathType Container) {
    $targets += New-CleanupTarget -Path $workPath -Kind 'temporary-work'
}

if (Test-Path -LiteralPath $validationPath -PathType Container) {
    $targets += New-CleanupTarget -Path $validationPath -Kind 'validation-evidence'
}

foreach ($parentName in @('src', 'tests')) {
    $parentPath = Join-Path $projectRoot $parentName
    foreach ($project in Get-ChildItem -LiteralPath $parentPath -Directory) {
        foreach ($generatedName in @('bin', 'obj')) {
            $generatedPath = Join-Path $project.FullName $generatedName
            if (Test-Path -LiteralPath $generatedPath -PathType Container) {
                $targets += New-CleanupTarget `
                    -Path $generatedPath `
                    -Kind "$parentName-$generatedName"
            }
        }
    }
}

$releaseRoot = Join-Path $projectRoot 'artifacts\release'
if ((Test-Path -LiteralPath $releaseRoot -PathType Container) -and
    $effectiveRetention -eq 'ArchivesOnly') {
    foreach ($versionDirectory in Get-ChildItem -LiteralPath $releaseRoot -Directory) {
        $verification = Get-ArchiveVerification $versionDirectory.FullName
        $archiveResults += $verification
        if ($verification.IsValid -and $verification.PortablePath) {
            $targets += New-CleanupTarget `
                -Path $verification.PortablePath `
                -Kind 'expanded-release'
        }
    }
}

$orderedTargets = @($targets | Sort-Object RelativePath -Unique)
$plannedBytes = [int64]0
$plannedFiles = [int64]0
foreach ($target in $orderedTargets) {
    $plannedBytes += [int64]$target.Bytes
    $plannedFiles += [int64]$target.Files
}

$evidence = @(Get-EvidenceFiles)
$evidenceBytes = [int64]0
foreach ($item in $evidence) {
    $evidenceBytes += [int64]$item.Bytes
}

$recycle = Get-RecycleContext
$capacityReady = (
    $recycle.NukeOnDelete -eq 0 -and
    $plannedBytes -le ($recycle.MaxBytes - $recycle.CurrentBytes - $recycleReserveBytes))

$mode = if ($Execute -and $Resume) {
    'EXECUTE-RESUME'
}
elseif ($Execute) {
    'EXECUTE'
}
else {
    'PREVIEW'
}
Write-Host "Mode: $mode"
Write-Host "Project root: $projectRoot"
Write-Host "Release retention: $effectiveRetention"
Write-Host "Cleanup targets: $($orderedTargets.Count)"
Write-Host "Cleanup files: $plannedFiles"
Write-Host "Cleanup bytes: $plannedBytes ($(Format-ByteSize $plannedBytes))"
Write-Host "Evidence files: $($evidence.Count)"
Write-Host "Evidence bytes: $evidenceBytes ($(Format-ByteSize $evidenceBytes))"
Write-Host ''

$orderedTargets |
    Select-Object Kind, RelativePath, Files, Bytes |
    Format-Table -AutoSize

Write-Host 'Release archive verification:'
$archiveResults |
    Select-Object Version, IsValid, Reason |
    Sort-Object Version |
    Format-Table -AutoSize

Write-Host "Recycle root: $($recycle.RecycleRoot)"
Write-Host "Recycle current: $($recycle.CurrentBytes) ($(Format-ByteSize $recycle.CurrentBytes))"
Write-Host "Recycle maximum: $($recycle.MaxBytes) ($(Format-ByteSize $recycle.MaxBytes))"
Write-Host "Recycle reserve: $recycleReserveBytes ($(Format-ByteSize $recycleReserveBytes))"
Write-Host "Recycle NukeOnDelete: $($recycle.NukeOnDelete)"
Write-Host "Recycle capacity ready: $capacityReady"

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Preview only. No files were backed up or recycled.'
    Write-Host "Execution confirmation phrase: $confirmationPhrase"
    exit 0
}

if (-not $capacityReady) {
    throw 'Recycle Bin capacity precondition is not satisfied. No backup or recycle action was started.'
}

if ($Resume) {
    $backup = Assert-EvidenceBackup -BackupRoot $EvidenceBackupRoot
    Write-Host "Existing evidence backup reverified: $($backup.ManifestPath)"
}
else {
    $backup = Backup-Evidence -Evidence $evidence -BackupRoot $EvidenceBackupRoot
    Write-Host "Evidence backup verified: $($backup.ManifestPath)"
}

$recycled = @()
foreach ($target in $orderedTargets) {
    Write-Host "Recycling: $($target.RelativePath)"
    $verification = Move-DirectoryToRecycleBin `
        -Path $target.Path `
        -RecycleRoot $recycle.RecycleRoot
    $recycled += [pscustomobject]@{
        RelativePath = $target.RelativePath
        MetadataPath = $verification.MetadataPath
        DataPath = $verification.DataPath
    }
}

Write-Host ''
Write-Host "Recycled targets verified: $($recycled.Count)"
Write-Host "Evidence backup root: $($backup.Root)"
Write-Host 'The Recycle Bin still consumes space on the source volume.'
Write-Host 'This script does not empty the Recycle Bin.'
