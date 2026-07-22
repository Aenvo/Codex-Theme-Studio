[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Discover', 'Snapshot', 'Port')]
    [string]$Mode,

    [int]$ProcessId = 0,

    [ValidateRange(1, 65535)]
    [int]$Port = 9229
)

$ErrorActionPreference = 'Stop'
$officialName = 'OpenAI.Codex'
$officialFamily = 'OpenAI.Codex_2p2nqsd0c76g0'
$officialPublisherId = '2p2nqsd0c76g0'

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
        $Package
    )

    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $TargetProcessId"
    if ($null -eq $process) {
        return $null
    }

    $installRoot = [IO.Path]::GetFullPath($Package.InstallLocation).TrimEnd('\') + '\'
    $executablePath = [IO.Path]::GetFullPath([string]$process.ExecutablePath)
    $isInsidePackage = $executablePath.StartsWith(
        $installRoot,
        [StringComparison]::OrdinalIgnoreCase)
    $isExpectedExecutable = [IO.Path]::GetFileName($executablePath) -ieq 'ChatGPT.exe'
    $isMainProcess = -not ([string]$process.CommandLine -match '(?i)(?:^|\s)--type=')

    [pscustomobject]@{
        processId = [int]$process.ProcessId
        startedAtUtc = ([DateTime]$process.CreationDate).ToUniversalTime().ToString('O')
        executablePath = $executablePath
        commandLineKind = if ($isMainProcess) { 'main' } else { 'child' }
        identityValid = [bool]($isInsidePackage -and $isExpectedExecutable -and $isMainProcess)
    }
}

$package = Get-OfficialPackage

switch ($Mode) {
    'Discover' {
        if ($null -eq $package) {
            [pscustomobject]@{
                package = $null
                processes = @()
            } | ConvertTo-Json -Depth 5 -Compress
            exit 0
        }

        $snapshots = @(
            Get-CimInstance Win32_Process -Filter "Name = 'ChatGPT.exe'" |
                ForEach-Object {
                    Get-ProcessSnapshot -TargetProcessId ([int]$_.ProcessId) -Package $package
                } |
                Where-Object { $null -ne $_ -and $_.identityValid }
        )

        [pscustomobject]@{
            package = [pscustomobject]@{
                packageFamilyName = [string]$package.PackageFamilyName
                packageFullName = [string]$package.PackageFullName
                version = [string]$package.Version
                publisherId = [string]$package.PublisherId
                signatureKind = [string]$package.SignatureKind
                installLocation = [IO.Path]::GetFullPath([string]$package.InstallLocation)
                executablePath = Join-Path ([string]$package.InstallLocation) 'app\ChatGPT.exe'
            }
            processes = $snapshots
        } | ConvertTo-Json -Depth 5 -Compress
    }

    'Snapshot' {
        if ($null -eq $package) {
            [pscustomobject]@{
                package = $null
                process = $null
            } | ConvertTo-Json -Depth 5 -Compress
            exit 0
        }

        [pscustomobject]@{
            package = [pscustomobject]@{
                packageFamilyName = [string]$package.PackageFamilyName
                packageFullName = [string]$package.PackageFullName
                version = [string]$package.Version
                publisherId = [string]$package.PublisherId
                signatureKind = [string]$package.SignatureKind
                installLocation = [IO.Path]::GetFullPath([string]$package.InstallLocation)
                executablePath = Join-Path ([string]$package.InstallLocation) 'app\ChatGPT.exe'
            }
            process = Get-ProcessSnapshot -TargetProcessId $ProcessId -Package $package
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
