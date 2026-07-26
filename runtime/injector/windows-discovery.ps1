[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Discover', 'Snapshot', 'Port')]
    [string]$Mode,

    [int]$ProcessId = 0,

    [string]$ExecutablePath = '',

    [ValidateRange(1, 65535)]
    [int]$Port = 9229
)

$ErrorActionPreference = 'Stop'
$officialName = 'OpenAI.Codex'
$officialFamily = 'OpenAI.Codex_2p2nqsd0c76g0'
$officialPublisherId = '2p2nqsd0c76g0'
$securityModule = Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1'
if (-not (Test-Path -LiteralPath $securityModule -PathType Leaf)) {
    throw 'The Windows PowerShell security module is unavailable.'
}

Import-Module -Name $securityModule -ErrorAction Stop

function Get-OfficialPackage {
    $packages = @(Get-AppxPackage -Name $officialName | Where-Object {
        $_.PackageFamilyName -eq $officialFamily -and
        $_.PublisherId -eq $officialPublisherId -and
        $_.SignatureKind.ToString() -eq 'Store' -and
        $_.Status.ToString() -eq 'Ok'
    })

    if ($packages.Count -eq 0) {
        return $null
    }

    return $packages |
        Sort-Object Version -Descending |
        Select-Object -First 1
}

function Get-ProcessSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [int]$TargetProcessId,

        [Parameter(Mandatory = $true)]
        [string]$TargetExecutablePath
    )

    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $TargetProcessId"
    if ($null -eq $process -or
        [string]::IsNullOrWhiteSpace([string]$process.ExecutablePath)) {
        return $null
    }

    $executablePath = [IO.Path]::GetFullPath([string]$process.ExecutablePath)
    $expectedExecutable = [IO.Path]::GetFullPath($TargetExecutablePath)
    $isExpectedExecutable = $executablePath.Equals(
        $expectedExecutable,
        [StringComparison]::OrdinalIgnoreCase)
    $isMainProcess = -not ([string]$process.CommandLine -match '(?i)(?:^|\s)--type=')

    [pscustomobject]@{
        processId = [int]$process.ProcessId
        startedAtUtc = ([DateTime]$process.CreationDate).ToUniversalTime().ToString('O')
        executablePath = $executablePath
        commandLineKind = if ($isMainProcess) { 'main' } else { 'child' }
        identityValid = [bool]($isExpectedExecutable -and $isMainProcess)
    }
}

$package = $null
$manualExecutable = $null
if (-not [string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $isDriveAbsolute = $ExecutablePath -match '^[A-Za-z]:[\\/]'
    $isUncAbsolute = $ExecutablePath -match '^\\\\[^\\]+\\[^\\]+(?:\\|$)'
    if (-not ($isDriveAbsolute -or $isUncAbsolute)) {
        throw 'ExecutablePath must be absolute.'
    }

    $manualExecutable = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $manualExecutable -PathType Leaf) -or
        [IO.Path]::GetExtension($manualExecutable) -ine '.exe') {
        throw 'ExecutablePath must identify an existing executable.'
    }
}
else {
    $package = Get-OfficialPackage
}

function Get-TargetPackageInfo {
    if ($null -ne $manualExecutable) {
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($manualExecutable)
        $signature = Get-AuthenticodeSignature -LiteralPath $manualExecutable
        return [pscustomobject]@{
            packageFamilyName = ''
            packageFullName = ''
            version = [string]$versionInfo.FileVersion
            publisherId = ''
            signatureKind = [string]$signature.Status
            installLocation = [IO.Path]::GetDirectoryName($manualExecutable)
            executablePath = $manualExecutable
            source = 'manualExecutable'
        }
    }

    if ($null -eq $package) {
        return $null
    }

    return [pscustomobject]@{
        packageFamilyName = [string]$package.PackageFamilyName
        packageFullName = [string]$package.PackageFullName
        version = [string]$package.Version
        publisherId = [string]$package.PublisherId
        signatureKind = [string]$package.SignatureKind
        installLocation = [IO.Path]::GetFullPath([string]$package.InstallLocation)
        executablePath = Join-Path ([string]$package.InstallLocation) 'app\ChatGPT.exe'
        source = 'storeAutomatic'
    }
}

$target = Get-TargetPackageInfo

switch ($Mode) {
    'Discover' {
        if ($null -eq $target) {
            [pscustomobject]@{
                package = $null
                processes = @()
            } | ConvertTo-Json -Depth 5 -Compress
            exit 0
        }

        $targetName = [IO.Path]::GetFileName([string]$target.executablePath).Replace("'", "''")
        $snapshots = @(
            Get-CimInstance Win32_Process -Filter "Name = '$targetName'" |
                ForEach-Object {
                    Get-ProcessSnapshot `
                        -TargetProcessId ([int]$_.ProcessId) `
                        -TargetExecutablePath ([string]$target.executablePath)
                } |
                Where-Object { $null -ne $_ -and $_.identityValid }
        )

        [pscustomobject]@{
            package = $target
            processes = $snapshots
        } | ConvertTo-Json -Depth 5 -Compress
    }

    'Snapshot' {
        if ($null -eq $target) {
            [pscustomobject]@{
                package = $null
                process = $null
            } | ConvertTo-Json -Depth 5 -Compress
            exit 0
        }

        [pscustomobject]@{
            package = $target
            process = Get-ProcessSnapshot `
                -TargetProcessId $ProcessId `
                -TargetExecutablePath ([string]$target.executablePath)
        } | ConvertTo-Json -Depth 5 -Compress
    }

    'Port' {
        $listeners = @(
            Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue |
                Select-Object @{
                    Name = 'localAddress'
                    Expression = { [string]$_.LocalAddress }
                }, @{
                    Name = 'localPort'
                    Expression = { [int]$_.LocalPort }
                }, @{
                    Name = 'owningProcessId'
                    Expression = { [int]$_.OwningProcess }
                }
        )

        [pscustomobject]@{
            port = $Port
            listeners = $listeners
        } | ConvertTo-Json -Depth 5 -Compress
    }
}
