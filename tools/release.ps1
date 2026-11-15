<#
.SYNOPSIS
  Keeps the version number in one place instead of three.

.DESCRIPTION
  The version lives in FluentCleaner.csproj, in FluentCleaner.Classic.csproj and in
  docs/versions.json, which the update page reads. They drift: on 2026-11-15 the page still
  offered 26.09.01 while the app was 26.11.03, and Classic 1.18.250 while it was 1.19.269.
  Anyone who checked for updates was told they were up to date.

  Run with no arguments to see where things stand. Nothing is written in that case.

.EXAMPLE
  .\tools\release.ps1
  Shows both projects, the page, and whether they agree.

.EXAMPLE
  .\tools\release.ps1 -Modern 26.11.03
  Sets the WinUI version and writes both editions into docs/versions.json.

.EXAMPLE
  .\tools\release.ps1 -Sync
  Leaves the projects alone and copies their versions into docs/versions.json.
#>
[CmdletBinding()]
param(
    # New version for the WinUI edition, date based: YY.MM.DD
    [string]$Modern,
    # New version for the Classic edition: Major.Minor.Build
    [string]$Classic,
    # Write the projects' current versions into docs/versions.json without changing them
    [switch]$Sync
)

$ErrorActionPreference = 'Stop'

$root        = Split-Path $PSScriptRoot -Parent
$modernProj  = Join-Path $root 'FluentCleaner\FluentCleaner.csproj'
$classicProj = Join-Path $root 'FluentCleaner.Classic\FluentCleaner.Classic.csproj'
$versions    = Join-Path $root 'docs\versions.json'

foreach ($f in @($modernProj, $classicProj, $versions)) {
    if (-not (Test-Path $f)) { throw "Not found: $f" }
}

# --- reading ---------------------------------------------------------------

function Get-ProjectVersion([string]$path) {
    $m = [regex]::Match((Get-Content $path -Raw), '<AssemblyVersion>([^<]+)</AssemblyVersion>')
    if (-not $m.Success) { throw "No <AssemblyVersion> in $path" }
    return $m.Groups[1].Value
}

# Classic's csproj carries a four part version, the app shows three: AppInfo formats it as
# Major.Minor(2 digits).Build(3 digits), so 1.19.269.0 is displayed as 1.19.269. The page has
# to compare against what the app reports, not against what the csproj stores.
function Get-ClassicDisplayVersion([string]$raw) {
    $p = $raw.Split('.')
    if ($p.Count -lt 3) { return $raw }
    return '{0}.{1:D2}.{2:D3}' -f [int]$p[0], [int]$p[1], [int]$p[2]
}

# --- writing ---------------------------------------------------------------

# Writes the value into whichever of the three tags the project already has. Adding a tag
# that was not there would change how the project builds, so missing ones stay missing.
function Set-ProjectVersion([string]$path, [string]$version) {
    $text = Get-Content $path -Raw
    $before = $text
    foreach ($tag in 'AssemblyVersion', 'FileVersion', 'Version') {
        $text = [regex]::Replace($text, "<$tag>[^<]+</$tag>", "<$tag>$version</$tag>")
    }
    if ($text -ne $before) {
        # UTF-8 with BOM, which is what both csproj files already are.
        [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $true))
        return $true
    }
    return $false
}

function Test-ModernFormat([string]$v) { return $v -match '^\d{2}\.\d{2}\.\d{2}$' }
function Test-ClassicFormat([string]$v) { return $v -match '^\d+\.\d+\.\d+$' }

# --- apply -----------------------------------------------------------------

$changed = $false

if ($Modern) {
    if (-not (Test-ModernFormat $Modern)) {
        throw "Modern version must look like 26.11.03 (YY.MM.DD), got '$Modern'."
    }
    if (Set-ProjectVersion $modernProj $Modern) {
        Write-Host "FluentCleaner.csproj          -> $Modern" -ForegroundColor Green
        $changed = $true
    }
}

if ($Classic) {
    if (-not (Test-ClassicFormat $Classic)) {
        throw "Classic version must look like 1.19.269 (Major.Minor.Build), got '$Classic'."
    }
    # Four parts, the shape the Classic project already uses; AppInfo shows the first three.
    if (Set-ProjectVersion $classicProj "$Classic.0") {
        Write-Host "FluentCleaner.Classic.csproj  -> $Classic" -ForegroundColor Green
        $changed = $true
    }
}

# Whatever the projects now say is the truth the page has to repeat.
$modernNow  = Get-ProjectVersion $modernProj
$classicNow = Get-ClassicDisplayVersion (Get-ProjectVersion $classicProj)

$json     = Get-Content $versions -Raw | ConvertFrom-Json
$pageMod  = $json.modern.version
$pageCls  = $json.classic.version
$drift    = ($pageMod -ne $modernNow) -or ($pageCls -ne $classicNow)

if ($changed -or $Sync) {
    # Written by hand rather than with ConvertTo-Json, which aligns the colons into a shape
    # nobody would type. The page fetches this file; it should stay readable enough to fix
    # by hand in a pinch.
    $out = @"
{
  "classic": {
    "version": "$classicNow",
    "downloadUrl": "$($json.classic.downloadUrl)"
  },
  "modern": {
    "version": "$modernNow",
    "downloadUrl": "$($json.modern.downloadUrl)"
  }
}

"@
    [IO.File]::WriteAllText($versions, $out, (New-Object Text.UTF8Encoding $false))
    Write-Host "docs\versions.json            -> modern $modernNow, classic $classicNow" -ForegroundColor Green
    Write-Host ""
    Write-Host "Now rebuild, publish the release assets, and push docs\versions.json." -ForegroundColor Yellow
    Write-Host "The update page only tells the truth once that file is live."
    return
}

# --- report ----------------------------------------------------------------

Write-Host ""
Write-Host "  FluentCleaner (WinUI)     $modernNow"
Write-Host "  FluentCleaner Classic     $classicNow"
Write-Host "  docs\versions.json        modern $pageMod, classic $pageCls"
Write-Host ""

if ($drift) {
    Write-Host "  The page disagrees with the projects." -ForegroundColor Red
    Write-Host "  Users checking for updates are being told the wrong thing."
    Write-Host "  Fix with:  .\tools\release.ps1 -Sync" -ForegroundColor Yellow
} else {
    Write-Host "  Everything agrees." -ForegroundColor Green
}
Write-Host ""
