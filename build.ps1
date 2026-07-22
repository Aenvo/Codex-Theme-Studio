[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'CodexThemeStudio.sln'
$requiredSdkVersion = '8.0.423'

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
    $bundledNode = Join-Path $projectRoot 'runtime\node\node.exe'
    if (Test-Path -LiteralPath $bundledNode) {
        return $bundledNode
    }

    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    if ($nodeCommand) {
        return $nodeCommand.Source
    }

    throw 'Node.js was not found. The task 2 self-test requires Node.js 24.x.'
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
    Invoke-Checked $dotnet @('restore', $solutionPath, '--locked-mode')
    Invoke-Checked $dotnet @('build', $solutionPath, '--configuration', $Configuration, '--no-restore')
    Invoke-Checked $dotnet @('test', $solutionPath, '--configuration', $Configuration, '--no-build', '--no-restore')
    Invoke-Checked $dotnet @('format', $solutionPath, '--verify-no-changes', '--no-restore')

    $agentJson = & $dotnet run `
        --project 'src\CodexThemeStudio.Agent\CodexThemeStudio.Agent.csproj' `
        --configuration $Configuration `
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
    if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v24\.') {
        throw "Injector self-test requires Node.js 24.x. Current version: $nodeVersion."
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
        'runtime\injector\renderer-payload.test.mjs',
        'runtime\injector\renderer-runtime.test.mjs',
        'runtime\injector\main-runtime.test.mjs'
    )

    Write-Host 'Build, tests, formatting, Agent self-test, Injector self-test, and Injector tests passed.'
}
finally {
    Pop-Location
}
