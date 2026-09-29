$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('ElkaUpdater-RunnerChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$source = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'ELKA.PowerThrottleControl\Resources\UpdateRunner.ps1'

# Replace process launches with recording stubs. These tests cannot run an installer,
# elevate, close the user's application, or change its registered install location.
function Start-Process {
    param($FilePath, $ArgumentList, $Verb, $WindowStyle, [switch]$Wait, [switch]$PassThru, $WorkingDirectory)
    $global:UpdateCheckCalls.Add([PSCustomObject]@{ FilePath = $FilePath; Arguments = $ArgumentList; Verb = $Verb })
    if ($Verb -eq 'RunAs') {
        if (-not $global:UpdateCheckParent.HasExited) { throw 'Installer started before the parent closed.' }
        if ($global:UpdateCheckOutcome -eq 'uac-cancel') { throw [ComponentModel.Win32Exception]::new(1223) }
        $code = switch ($global:UpdateCheckOutcome) { 'success' { 0 }; 'cancelled' { 2 }; 'restart' { 3010 }; default { 1 } }
        return [PSCustomObject]@{ ExitCode = $code }
    }
}

try {
    foreach ($outcome in @('success', 'cancelled', 'failed', 'uac-cancel', 'restart')) {
        $attempt = Join-Path $fixtureRoot $outcome
        New-Item -ItemType Directory -Path $attempt | Out-Null
        $helper = Join-Path $attempt 'UpdateRunner.ps1'
        Copy-Item -LiteralPath $source -Destination $helper
        $installer = Join-Path $attempt 'ELKA_Power_Throttle_Control_Setup_1.3.4.exe'
        [IO.File]::WriteAllText($installer, 'Not an executable. Process launching is mocked.')
        $app = Join-Path $attempt 'ELKA.PowerThrottleControl.exe'
        [IO.File]::WriteAllText($app, 'Not an executable. Process launching is mocked.')
        $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
        $global:UpdateCheckCalls = [Collections.Generic.List[object]]::new()
        $global:UpdateCheckOutcome = $outcome
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = Join-Path $PSHOME 'powershell.exe'
        if (-not (Test-Path -LiteralPath $start.FileName)) { $start.FileName = Join-Path $PSHOME 'pwsh.exe' }
        $start.Arguments = '-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 2"'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $global:UpdateCheckParent = [Diagnostics.Process]::Start($start)
        & $helper -ParentProcessId $global:UpdateCheckParent.Id -InstallerPath $installer -ExpectedHash $hash -AppExecutable $app
        $global:UpdateCheckParent.Dispose()
        $expected = if ($outcome -eq 'uac-cancel') { 'cancelled' } else { $outcome }
        if ($global:UpdateCheckCalls.Count -ne 2) { throw "Unexpected launch count for $outcome" }
        $setup = $global:UpdateCheckCalls[0]
        $reopen = $global:UpdateCheckCalls[1]
        if ($setup.Verb -ne 'RunAs' -or $setup.Arguments -notcontains '/SILENT' -or $setup.Arguments -notcontains '/NORESTART') { throw 'Incorrect installer arguments.' }
        if ($reopen.Verb -or $reopen.Arguments -ne "--update-result=$expected") { throw "Incorrect unelevated restart for $outcome" }
        if ($outcome -in 'cancelled', 'failed', 'uac-cancel' -and $reopen.FilePath -ne $app) { throw 'Failed update did not preserve the original app path.' }
        Write-Output "PASS: $outcome waits for parent exit and reopens the app unelevated"
    }
} finally {
    # Validate the freshly allocated fixture directory before recursive cleanup.
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'ElkaUpdater-RunnerChecks-*') { throw 'Unexpected fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
    Remove-Variable -Name UpdateCheckCalls,UpdateCheckOutcome,UpdateCheckParent -Scope Global -ErrorAction SilentlyContinue
}
