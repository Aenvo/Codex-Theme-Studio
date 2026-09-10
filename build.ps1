[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'CodexThemeStudio.sln'
$globalJson = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'global.json') |
    ConvertFrom-Json
$runtimeBaseline = Get-Content -Raw -LiteralPath (
    Join-Path $projectRoot 'eng\runtime-baseline.json') |
    ConvertFrom-Json
$requiredSdkVersion = [string]$globalJson.sdk.version
$requiredNodeVersion = [string]$runtimeBaseline.nodeVersion
$runtimeIdentifier = [string]$runtimeBaseline.runtimeIdentifier

function Resolve-DotNet {
    $candidates = @()

    if ($env:DOTNET_ROOT) {
        $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe')
    }

    if ($env:LOCALAPPDATA) {
        $candidates += (Join-Path $env:LOCALAPPDATA 'CodexThemeStudio\devtools\dotnet-8.0.423\dotnet.exe')
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) {
        $candidates += $dotnetCommand.Source
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not (Test-Path -LiteralPath $candidate)) {
            continue
        }

        $sdks = & $candidate --list-sdks
        if ($LASTEXITCODE -eq 0 -and $sdks -match "^$([regex]::Escape($requiredSdkVersion))\s") {
            return $candidate
        }
    }

    throw "Required .NET SDK $requiredSdkVersion was not found. Set DOTNET_ROOT or install the required SDK."
}

function Resolve-Node {
    $candidates = @(
        (Join-Path $projectRoot 'runtime\node\node.exe'),
        (Join-Path $projectRoot (
            "artifacts\cache\node-v$requiredNodeVersion-$runtimeIdentifier\node.exe"))
    )

    $releaseRoot = Join-Path $projectRoot 'artifacts\release'
    if (Test-Path -LiteralPath $releaseRoot -PathType Container) {
        $localReleases = Get-ChildItem -LiteralPath $releaseRoot -Directory |
            Sort-Object Name -Descending
        foreach ($release in $localReleases) {
            $package = Get-ChildItem -LiteralPath $release.FullName -Directory |
                Where-Object Name -Like 'CodexThemeManager-*-win-x64-portable' |
                Select-Object -First 1
            if ($package) {
                $candidates += Join-Path $package.FullName 'runtime\node\node.exe'
            }
        }
    }

    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    if ($nodeCommand) {
        $candidates += $nodeCommand.Source
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        $actualVersion = (& $candidate --version).Trim()
        if ($LASTEXITCODE -eq 0 -and $actualVersion -eq "v$requiredNodeVersion") {
            return $candidate
        }
    }

    throw "Required Node.js v$requiredNodeVersion was not found."
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

$dotnet = Resolve-DotNet
$node = Resolve-Node
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Push-Location $projectRoot
try {
    Write-Host "Using .NET SDK $requiredSdkVersion"
    Write-Host "Using runtime identifier $runtimeIdentifier"
    Invoke-Checked $dotnet @(
        'restore',
        $solutionPath,
        '--runtime', $runtimeIdentifier,
        '--locked-mode'
    )
    Invoke-Checked $dotnet @(
        'build',
        $solutionPath,
        '--configuration', $Configuration,
        "-p:CodexRuntimeIdentifier=$runtimeIdentifier",
        '-p:SelfContained=false',
        '--no-restore'
    )
    Invoke-Checked $dotnet @(
        'test',
        $solutionPath,
        '--configuration', $Configuration,
        "-p:CodexRuntimeIdentifier=$runtimeIdentifier",
        '-p:SelfContained=false',
        '-m:1',
        '--no-build',
        '--no-restore'
    )
    Invoke-Checked $dotnet @('format', $solutionPath, '--verify-no-changes', '--no-restore')

    $agentJson = & $dotnet run `
        --project 'src\CodexThemeStudio.Agent\CodexThemeStudio.Agent.csproj' `
        --configuration $Configuration `
        --runtime $runtimeIdentifier `
        --no-self-contained `
        --no-build `
        --no-restore `
        -- self-test
    if ($LASTEXITCODE -ne 0) {
        throw "Agent self-test failed with exit code $LASTEXITCODE."
    }

    $agentResult = $agentJson | ConvertFrom-Json
    if ($agentResult.status -ne 'ok' -or $agentResult.protocolVersion -ne 1) {
        throw 'Agent self-test returned an invalid JSON status.'
    }

    $nodeVersion = & $node --version
    if ($LASTEXITCODE -ne 0 -or $nodeVersion -ne "v$requiredNodeVersion") {
        throw "Injector self-test requires Node.js v$requiredNodeVersion. Current version: $nodeVersion."
    }

    $injectorJson = & $node 'runtime\injector\index.mjs' 'self-test'
    if ($LASTEXITCODE -ne 0) {
        throw "Injector self-test failed with exit code $LASTEXITCODE."
    }

    $injectorResult = $injectorJson | ConvertFrom-Json
    if ($injectorResult.status -ne 'ok' -or $injectorResult.protocolVersion -ne 1) {
        throw 'Injector self-test returned an invalid JSON status.'
    }

    Invoke-Checked $node @(
        '--test',
        'runtime\injector\security.test.mjs',
        'runtime\injector\inspector-lifecycle.test.mjs',
        'runtime\injector\renderer-payload.test.mjs',
        'runtime\injector\renderer-runtime.test.mjs',
        'runtime\injector\main-runtime.test.mjs',
        'runtime\updater\apply-update.test.mjs'
    )

    Write-Host 'Build, tests, formatting, Agent self-test, Injector self-test, and runtime tests passed.'
}
finally {
    Pop-Location
}
