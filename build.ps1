<#
.SYNOPSIS
    Builds Jellyfin ReleaseHub and installs it into a local Jellyfin server.

.DESCRIPTION
    Compiles the plugin in Release configuration and copies the resulting assembly, together with a
    generated meta.json, into the Jellyfin plugin directory.

    Jellyfin loads plugin assemblies at startup and keeps them locked for the lifetime of the process,
    so the server has to be stopped before the DLL can be replaced and restarted before the new build
    takes effect. Pass -RestartJellyfin to have this script do that; otherwise it stops with an
    explanation rather than leaving a half-installed plugin behind.

.PARAMETER PluginRoot
    The Jellyfin plugins directory. Defaults to the Windows service location.

.PARAMETER SkipInstall
    Build only, without touching the Jellyfin installation.

.PARAMETER RestartJellyfin
    Stop Jellyfin before copying and start it again afterwards.

.EXAMPLE
    .\build.ps1 -RestartJellyfin

.EXAMPLE
    .\build.ps1 -SkipInstall
#>
[CmdletBinding()]
param(
    [string] $PluginRoot = "$env:ProgramData\Jellyfin\Server\plugins",
    [switch] $SkipInstall,
    [switch] $RestartJellyfin
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'src\Jellyfin.Plugin.ReleaseHub\Jellyfin.Plugin.ReleaseHub.csproj'
$buildYaml = Join-Path $repoRoot 'build.yaml'

# Single source of truth for name/guid/version: build.yaml, the same file the Jellyfin plugin
# repository tooling reads.
$meta = @{}
foreach ($line in Get-Content $buildYaml) {
    if ($line -match '^\s*(name|guid|version|targetAbi|owner|category|overview)\s*:\s*"?([^"]*)"?\s*$') {
        $meta[$Matches[1]] = $Matches[2].Trim()
    }
}

function Get-JellyfinProcesses {
    @{
        Server = Get-Process -Name 'jellyfin' -ErrorAction SilentlyContinue
        Tray   = Get-Process -Name 'Jellyfin.Windows.Tray' -ErrorAction SilentlyContinue
    }
}

function Stop-Jellyfin {
    $procs = Get-JellyfinProcesses

    # Stop the tray first: it supervises the server and would otherwise restart it mid-copy.
    foreach ($p in @($procs.Tray) + @($procs.Server)) {
        if ($null -eq $p) { continue }
        Write-Host "  stopping $($p.ProcessName) (PID $($p.Id))" -ForegroundColor DarkGray
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }

    # Wait for the file locks to actually be released, not merely for the process to be signalled.
    for ($i = 0; $i -lt 40; $i++) {
        $still = Get-JellyfinProcesses
        if (-not $still.Server -and -not $still.Tray) { return }
        Start-Sleep -Milliseconds 250
    }

    throw 'Jellyfin did not stop within 10 seconds.'
}

function Start-Jellyfin {
    # Launch from Jellyfin's own directory. A child process inherits this script's working directory
    # otherwise, which makes the server look for its wwwroot under the repository.
    $serverDir = Join-Path $env:ProgramFiles 'Jellyfin\Server'

    $tray = Join-Path $serverDir 'jellyfin-windows-tray\Jellyfin.Windows.Tray.exe'
    if (Test-Path $tray) {
        # The tray launches jellyfin.exe with the right --datadir, so starting it restores the exact
        # configuration the machine normally runs.
        Start-Process -FilePath $tray -WorkingDirectory $serverDir | Out-Null
        Write-Host '  started Jellyfin.Windows.Tray' -ForegroundColor DarkGray
        return
    }

    $server = Join-Path $serverDir 'jellyfin.exe'
    if (Test-Path $server) {
        Start-Process -FilePath $server -WorkingDirectory $serverDir `
            -ArgumentList '--datadir', "$env:ProgramData\Jellyfin\Server" | Out-Null
        Write-Host '  started jellyfin.exe' -ForegroundColor DarkGray
        return
    }

    Write-Warning 'Could not find Jellyfin to restart. Start it manually.'
}

Write-Host "Building $($meta.name) $($meta.version) (targetAbi $($meta.targetAbi))" -ForegroundColor Cyan

$outDir = Join-Path $repoRoot 'artifacts'
dotnet build $project -c Release -o $outDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$dll = Join-Path $outDir 'Jellyfin.Plugin.ReleaseHub.dll'
if (-not (Test-Path $dll)) {
    throw "Expected assembly not found at $dll"
}

Write-Host "Built $dll" -ForegroundColor Green

if ($SkipInstall) {
    return
}

if (-not (Test-Path $PluginRoot)) {
    throw "Jellyfin plugin directory not found: $PluginRoot"
}

$running = Get-JellyfinProcesses
$wasRunning = $null -ne $running.Server -or $null -ne $running.Tray

if ($wasRunning -and -not $RestartJellyfin) {
    throw @"
Jellyfin is running and holds a lock on the installed plugin assembly.
Either stop Jellyfin and run this script again, or re-run it with -RestartJellyfin.
"@
}

if ($wasRunning) {
    Write-Host 'Stopping Jellyfin' -ForegroundColor Yellow
    Stop-Jellyfin
}

$target = Join-Path $PluginRoot "ReleaseHub_$($meta.version)"
New-Item -ItemType Directory -Force -Path $target | Out-Null

Copy-Item $dll -Destination $target -Force

# The plugin card in Dashboard > Plugins shows whatever meta.json's imagePath points at. Jellyfin
# writes that itself when installing from a repository (downloading the manifest's imageUrl); a local
# install has to place the file and reference it by hand.
$logoSource = Join-Path $repoRoot 'media\logo.png'
$logoTarget = Join-Path $target 'logo.png'
$imagePath = $null

if (Test-Path $logoSource) {
    Copy-Item $logoSource -Destination $logoTarget -Force
    $imagePath = $logoTarget
}
else {
    Write-Warning "media\logo.png not found; the plugin will show Jellyfin's default icon."
}

# Jellyfin reads meta.json to decide whether the plugin is compatible with the running server.
$metaJson = [ordered]@{
    category    = $meta.category
    guid        = $meta.guid
    name        = $meta.name
    overview    = $meta.overview
    description = $meta.overview
    owner       = $meta.owner
    targetAbi   = $meta.targetAbi
    version     = $meta.version
    timestamp   = (Get-Date).ToUniversalTime().ToString('o')
    status      = 'Active'
    autoUpdate  = $false
    assemblies  = @()
}

if ($imagePath) {
    $metaJson['imagePath'] = $imagePath
}

# Written byte by byte rather than with Set-Content -Encoding UTF8, because that spelling means two
# different things: no BOM under PowerShell 7, a BOM under Windows PowerShell 5.1. Jellyfin parses
# meta.json with System.Text.Json, which rejects a BOM outright ("'0xEF' is an invalid start of a
# value") and logs an error at every startup for a plugin it can then no longer read.
$metaText = $metaJson | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText(
    (Join-Path $target 'meta.json'),
    $metaText,
    (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Installed to $target" -ForegroundColor Green

if ($wasRunning) {
    Write-Host 'Starting Jellyfin' -ForegroundColor Yellow
    Start-Jellyfin
}
else {
    Write-Host 'Start Jellyfin for the plugin to be loaded.' -ForegroundColor Yellow
}
