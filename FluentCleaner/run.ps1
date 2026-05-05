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

function Test-WindowsAppRuntime {
    # Check multiple ways to detect Windows App Runtime 2.0 or higher
    
    # Method 1: Check for WindowsAppRuntime DLL in Program Files
    $runtimePaths = @(
        "$env:ProgramFiles\WindowsAppRuntime\*.dll",
        "${env:ProgramFiles(x86)}\WindowsAppRuntime\*.dll",
        "$env:LOCALAPPDATA\Microsoft\WindowsAppRuntime\*.dll"
    )
    
    foreach ($pattern in $runtimePaths) {
        if (Get-Item $pattern -ErrorAction SilentlyContinue) {
            return $true
        }
    }
    
    # Method 2: Check registry for Windows App Runtime installation
    try {
        $regPath = 'HKLM:\SOFTWARE\Microsoft\Windows App Runtime'
        if (Test-Path $regPath) {
            $version = Get-ItemProperty $regPath -ErrorAction SilentlyContinue
            if ($version) {
                return $true
            }
        }
    } catch {
        # Registry check failed, continue to next method
    }
    
    # Method 3: Check for Windows App Runtime via winget
    try {
        $installed = winget list --id Microsoft.WindowsAppRuntime 2>$null | Select-String "Microsoft.WindowsAppRuntime"
        if ($installed) {
            return $true
        }
    } catch {
        # winget query failed, continue
    }
    
    return $false
}

if (-not (Test-WindowsAppRuntime)) {
    Write-Host '  Windows App Runtime ..... NOT FOUND' -ForegroundColor Red
    Write-Host ''
    Write-Host '[FluentCleaner] Windows App Runtime 2 or higher is required to run this application.' -ForegroundColor Yellow
    Write-Host '     Download from: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads' -ForegroundColor Cyan
    Write-Host ''
    Write-Host '     Or use: https://aka.ms/winappsdk (GitHub)' -ForegroundColor Cyan
    Read-Host 'Press Enter to exit'
    exit 1
} else {
    Write-Host '  Windows App Runtime ..... OK' -ForegroundColor Green
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