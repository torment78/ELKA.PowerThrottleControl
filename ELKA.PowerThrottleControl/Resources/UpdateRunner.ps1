param(
    [Parameter(Mandatory)][int]$ParentProcessId,
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$ExpectedHash,
    [Parameter(Mandatory)][string]$AppExecutable
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $PSScriptRoot 'update.log'

function Get-InstalledExecutable {
    $machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64')
    try {
        $key = $machine.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A3D6B7E9-4A78-4C46-91D7-8EF52F76D2F1}_is1')
        if ($null -eq $key) { return $null }
        try {
            $directory = $key.GetValue('InstallLocation')
            if ($directory) {
                $candidate = Join-Path $directory 'ELKA.PowerThrottleControl.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
            }
        } finally { $key.Dispose() }
    } finally { $machine.Dispose() }
    return $null
}

function Test-UacCancellation($failure) {
    $exception = $failure.Exception
    while ($null -ne $exception) {
        if ($exception -is [System.ComponentModel.Win32Exception] -and $exception.NativeErrorCode -eq 1223) { return $true }
        $exception = $exception.InnerException
    }
    return $false
}

try {
    # Limit the installer handoff to the file in this private update attempt folder.
    $installerFullPath = [IO.Path]::GetFullPath($InstallerPath)
    if ([IO.Path]::GetDirectoryName($installerFullPath) -ne $PSScriptRoot -or
        [IO.Path]::GetFileName($installerFullPath) -notmatch '^ELKA_Power_Throttle_Control_Setup_\d+\.\d+\.\d+\.exe$') {
        throw 'Unexpected installer path.'
    }
    if ((Get-FileHash -LiteralPath $installerFullPath -Algorithm SHA256).Hash -ne $ExpectedHash) { throw 'Installer verification failed.' }
    $parent = [Diagnostics.Process]::GetProcessById($ParentProcessId)
    # Force opening the process handle before announcing readiness, avoiding PID reuse.
    $null = $parent.Handle
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ready.txt'), 'ready')
    if (-not $parent.WaitForExit(60000)) { throw 'The application did not close. Installation was not started.' }
    $parent.Dispose()
} catch {
    $_ | Out-String | Add-Content -LiteralPath $logPath
    exit 1
}

$result = 'failed'
try {
    # Recheck after the app has exited and before asking Windows to elevate Setup.
    if ((Get-FileHash -LiteralPath $installerFullPath -Algorithm SHA256).Hash -ne $ExpectedHash) { throw 'Installer verification failed.' }
    $arguments = @('/SP-', '/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/RESTARTEXITCODE=3010',
        '/CLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', ('/LOG="' + (Join-Path $PSScriptRoot 'setup.log') + '"'))
    # Only the installer is elevated; this helper remains the original desktop user.
    $installer = Start-Process -FilePath $installerFullPath -ArgumentList $arguments -Verb RunAs -WindowStyle Normal -Wait -PassThru
    if ($installer.ExitCode -eq 0) { $result = 'success' }
    elseif ($installer.ExitCode -eq 3010) { $result = 'restart' }
    elseif ($installer.ExitCode -in 2, 5) { $result = 'cancelled' }
    else { throw ('Setup failed with exit code ' + $installer.ExitCode) }
} catch {
    if (Test-UacCancellation $_) { $result = 'cancelled' }
    $_ | Out-String | Add-Content -LiteralPath $logPath
}

try {
    # Prefer the registered new path after installation, including folder migration.
    $installedExecutable = Get-InstalledExecutable
    $restartPath = $AppExecutable
    if ($result -in 'success', 'restart' -or -not (Test-Path -LiteralPath $restartPath -PathType Leaf)) {
        if ($installedExecutable) { $restartPath = $installedExecutable }
    }
    if (-not (Test-Path -LiteralPath $restartPath -PathType Leaf)) { throw 'Could not locate the application to reopen.' }
    Start-Process -FilePath $restartPath -ArgumentList ('--update-result=' + $result) -WorkingDirectory (Split-Path -Parent $restartPath) -WindowStyle Normal
} catch {
    $_ | Out-String | Add-Content -LiteralPath $logPath
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show('Setup has finished, but the application could not be reopened. Please open ELKA Power Throttle Control from the Start menu.', 'ELKA Power Throttle Control') | Out-Null
}
