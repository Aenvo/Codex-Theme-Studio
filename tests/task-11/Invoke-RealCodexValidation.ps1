[CmdletBinding()]
param(
    [ValidateRange(1, 50)]
    [int]$SwitchCount = 10
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$injector = Join-Path $projectRoot 'runtime\injector\index.mjs'
$fixture = Join-Path $projectRoot 'runtime\injector\fixtures\verification-theme.json'
$node = if (Test-Path -LiteralPath (Join-Path $projectRoot 'runtime\node\node.exe')) {
    Join-Path $projectRoot 'runtime\node\node.exe'
}
else {
    (Get-Command node -ErrorAction Stop).Source
}

$processId = $null
$cleanupResult = $null
$closeResult = $null

function Invoke-Injector {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Arguments,

        [string]$StandardInput
    )

    $temporaryInput = $null
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    if ($null -eq $StandardInput) {
        $startInfo.FileName = $node
        $startInfo.Arguments = "`"$injector`" $Arguments"
    }
    else {
        $temporaryInput = Join-Path `
            ([System.IO.Path]::GetTempPath()) `
            "cts-task11-$([guid]::NewGuid().ToString('N')).json"
        [System.IO.File]::WriteAllText(
            $temporaryInput,
            $StandardInput,
            [System.Text.UTF8Encoding]::new($false))
        $startInfo.FileName = 'cmd.exe'
        $startInfo.Arguments = (
            '/d /s /c ""{0}" "{1}" {2} < "{3}""' -f
            $node,
            $injector,
            $Arguments,
            $temporaryInput)
    }

    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $standardOutput = $process.StandardOutput.ReadToEnd()
        $standardError = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Injector failed: $standardError"
        }

        return $standardOutput | ConvertFrom-Json
    }
    finally {
        $process.Dispose()
        if ($null -ne $temporaryInput -and
            (Test-Path -LiteralPath $temporaryInput)) {
            Remove-Item -LiteralPath $temporaryInput -Force
        }
    }
}

try {
    $discovery = Invoke-Injector -Arguments 'discover'
    if ($discovery.status -ne 'ok') {
        throw 'Codex discovery failed.'
    }

    $processes = @($discovery.processes)
    if ($processes.Count -ne 1) {
        throw "Expected exactly one trusted Codex main process; found $($processes.Count)."
    }

    $processId = [int]$processes[0].processId
    $probe = Invoke-Injector -Arguments "probe --pid $processId"
    if ($probe.status -ne 'ok') {
        throw 'Codex probe failed.'
    }

    $before = Get-Process -Id $processId -ErrorAction Stop
    $beforeWorkingSet = $before.WorkingSet64
    $durations = [System.Collections.Generic.List[double]]::new()
    $lastRenderer = $null

    for ($index = 0; $index -lt $SwitchCount; $index++) {
        $payload = Get-Content -Raw -Encoding utf8 -LiteralPath $fixture |
            ConvertFrom-Json
        $payload.theme.id = [guid]::NewGuid().ToString('D')
        $payload.theme.name = "Task 11 switch $($index + 1)"
        $json = $payload | ConvertTo-Json -Depth 20 -Compress
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $lastRenderer = Invoke-Injector `
            -Arguments "renderer-apply --pid $processId" `
            -StandardInput $json
        $stopwatch.Stop()
        if ($lastRenderer.status -ne 'ok') {
            throw "Renderer switch $($index + 1) failed."
        }

        $durations.Add($stopwatch.Elapsed.TotalMilliseconds)
    }

    $status = Invoke-Injector -Arguments "renderer-status --pid $processId"
    if ($status.status -ne 'ok') {
        throw 'Renderer status failed.'
    }

    $after = Get-Process -Id $processId -ErrorAction Stop
    [pscustomobject]@{
        schemaVersion = 1
        codexVersion = $discovery.installation.version
        electronVersion = $probe.probe.electronVersion
        processId = $processId
        windowCount = $probe.probe.windowCount
        routeTypes = @($probe.probe.routeTypes)
        switchCount = $SwitchCount
        switchMilliseconds = @($durations)
        averageSwitchMilliseconds = ($durations | Measure-Object -Average).Average
        workingSetBeforeBytes = $beforeWorkingSet
        workingSetAfterBytes = $after.WorkingSet64
        workingSetDeltaBytes = $after.WorkingSet64 - $beforeWorkingSet
        renderer = $status.renderer
    } | ConvertTo-Json -Depth 20
}
finally {
    if ($null -ne $processId) {
        try {
            $cleanupResult = Invoke-Injector `
                -Arguments "renderer-cleanup --pid $processId"
        }
        finally {
            $closeResult = Invoke-Injector `
                -Arguments "close-inspector --pid $processId"
        }
    }

    $listeners = @(
        Get-NetTCPConnection -LocalPort 9229 -State Listen -ErrorAction SilentlyContinue
    )
    if ($listeners.Count -ne 0) {
        throw "Inspector cleanup failed: port 9229 still has $($listeners.Count) listener(s)."
    }
}
