#Requires -Version 5.1
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

if ($IsLinux -or $IsMacOS) {
    Write-Host '[!!] FluentCleaner is only available on Windows.' -ForegroundColor Red
    exit 1
}

# -- Check Windows App Runtime ------------------------------------------------
Write-Host ''
Write-Host '[FluentCleaner] Checking dependencies...' -ForegroundColor Cyan

function Get-WindowsAppRuntimeVersion {
    # Method 1: Check via Get-AppxPackage for Microsoft.WindowsAppRuntime
    try {
        $packages = Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.*' -ErrorAction SilentlyContinue
        if ($packages) {
            $versions = $packages | ForEach-Object {
                try { [version]$_.Version } catch { $null }
            } | Where-Object { $_ } | Sort-Object -Descending
            if ($versions) { return $versions[0] }
        }
    } catch {}

    # Method 2: Check registry subkeys for versioned installations
    try {
        $regBase = 'HKLM:\SOFTWARE\Microsoft\WindowsAppSDK'
        if (Test-Path $regBase) {
            $subkeys = Get-ChildItem $regBase -ErrorAction SilentlyContinue
            $versions = $subkeys | ForEach-Object {
                try { [version]$_.PSChildName } catch { $null }
            } | Where-Object { $_ } | Sort-Object -Descending
            if ($versions) { return $versions[0] }
        }
    } catch {}

    # Method 3: winget fallback
    try {
        $line = winget list --id Microsoft.WindowsAppRuntime 2>$null |
            Select-String 'Microsoft.WindowsAppRuntime' |
            Select-Object -First 1
        if ($line) {
            $match = [regex]::Match($line, '(\d+\.\d+\.\d+)')
            if ($match.Success) { return [version]$match.Value }
        }
    } catch {}

    return $null
}

$minVersion = [version]'1.8'
$runtimeVersion = Get-WindowsAppRuntimeVersion

if (-not $runtimeVersion) {
    Write-Host '  Windows App Runtime ..... NOT FOUND' -ForegroundColor Red
    Write-Host ''
    Write-Host '[FluentCleaner] Windows App Runtime 1.8 or higher is required.' -ForegroundColor Yellow
    Write-Host '     Download from: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads' -ForegroundColor Cyan
    Write-Host '     Or use: https://aka.ms/winappsdk (GitHub)' -ForegroundColor Cyan
    Read-Host 'Press Enter to exit'
    exit 1
} elseif ($runtimeVersion -lt $minVersion) {
    Write-Host "  Windows App Runtime ..... v$runtimeVersion (too old)" -ForegroundColor Red
    Write-Host ''
    Write-Host "[FluentCleaner] Windows App Runtime $minVersion or higher is required (found v$runtimeVersion)." -ForegroundColor Yellow
    Write-Host '     Download from: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads' -ForegroundColor Cyan
    Read-Host 'Press Enter to exit'
    exit 1
} else {
    Write-Host "  Windows App Runtime ..... v$runtimeVersion OK" -ForegroundColor Green
}

$projectRoot = $PSScriptRoot
$projectFile  = Join-Path $projectRoot 'FluentCleaner.csproj'
$projectXml   = [xml](Get-Content $projectFile)

$tfm = $projectXml.Project.PropertyGroup.TargetFramework |
    Where-Object { $_ } |
    Select-Object -First 1

$assemblyName = $projectXml.Project.PropertyGroup.AssemblyName |
    Where-Object { $_ } |
    Select-Object -First 1

if (-not $assemblyName) {
    $assemblyName = 'FluentCleaner'
}

$platform = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq
    [System.Runtime.InteropServices.Architecture]::Arm64) { 'arm64' } else { 'x64' }
$runtimeIdentifier = if ($platform -eq 'arm64') { 'win-arm64' } else { 'win-x64' }
$exePath = Join-Path $projectRoot "bin\$platform\$Configuration\$tfm\$runtimeIdentifier\$assemblyName.exe"

Write-Host ''
Write-Host "[FluentCleaner] Building $Configuration for $runtimeIdentifier..." -ForegroundColor Cyan
Write-Host ''

dotnet build $projectFile -c $Configuration -p:Platform=$platform --nologo -v minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host '[!!] Build failed. See the output above for details.' -ForegroundColor Red
    exit $LASTEXITCODE
}

if (-not (Test-Path $exePath)) {
    Write-Host ''
    Write-Host "[!!] Build succeeded, but the app was not found at:`n     $exePath" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '[FluentCleaner] Launching app...' -ForegroundColor Green
Start-Process -FilePath $exePath
