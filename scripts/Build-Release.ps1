param(
    [string]$Version = "1.2.0",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "ELKA.PowerThrottleControl.sln"
$projectPath = Join-Path $repositoryRoot "ELKA.PowerThrottleControl\ELKA.PowerThrottleControl.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
$installerDirectory = Join-Path $repositoryRoot "artifacts\installer"
$portableZip = Join-Path $installerDirectory "ELKA_Power_Throttle_Control_Portable_$Version.zip"
$innoCompiler = Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Inno Setup 6\ISCC.exe"
$installerScript = Join-Path $repositoryRoot "Installer\ELKA.PowerThrottleControl.iss"

if (-not (Test-Path -LiteralPath $innoCompiler)) {
    throw "Inno Setup 6 was not found at $innoCompiler."
}

dotnet restore $solutionPath
dotnet publish $projectPath --configuration $Configuration --runtime win-x64 --self-contained true --output $publishDirectory -p:Version=$Version -p:FileVersion="$Version.0" -p:AssemblyVersion="$Version.0" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null
if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $portableZip -CompressionLevel Optimal

& $innoCompiler "/DAppVersion=$Version" "/DSourcePublishDir=$publishDirectory" "/DInstallerOutputDir=$installerDirectory" $installerScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$releaseFiles = Get-ChildItem -LiteralPath $installerDirectory -File |
    Where-Object { $_.Extension -in ".exe", ".zip" -and $_.Name -like "*$Version*" }
$checksumPath = Join-Path $installerDirectory "SHA256SUMS.txt"
$checksumLines = foreach ($file in $releaseFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ascii

Get-ChildItem -LiteralPath $installerDirectory -File | Select-Object Name, Length, LastWriteTime
