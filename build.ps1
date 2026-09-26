<#
.SYNOPSIS
    Builds Jellyfin ReleaseHub and installs it into a local Jellyfin server.

.DESCRIPTION
    Compiles the plugin in Release configuration and copies the resulting assembly, together with a
    generated meta.json, into the Jellyfin plugin directory.

    ReleaseHub ships one build per Jellyfin generation (see `targets` in build.yaml): net9.0 for
    Jellyfin 10.11, net10.0 for Jellyfin 12. The one installed is the one the local server can load,
    found from the version of its jellyfin.dll.

    Jellyfin loads plugin assemblies at startup and keeps them locked for the lifetime of the process,
    so the server has to be stopped before the DLL can be replaced and restarted before the new build
    takes effect. Pass -RestartJellyfin to have this script do that; otherwise it stops with an
    explanation rather than leaving a half-installed plugin behind.

.PARAMETER PluginRoot
    The Jellyfin plugins directory. Defaults to the Windows service location.

.PARAMETER JellyfinVersion
    The Jellyfin version to build for, such as 12.1.0 or 10.11.11. Defaults to the version of the
    local server. With -SkipInstall and no server found, every target is built.

.PARAMETER SkipInstall
    Build only, without touching the Jellyfin installation.

.PARAMETER RestartJellyfin
    Stop Jellyfin before copying and start it again afterwards.

.EXAMPLE
    .\build.ps1 -RestartJellyfin

.EXAMPLE
    .\build.ps1 -SkipInstall

.EXAMPLE
    .\build.ps1 -SkipInstall -JellyfinVersion 10.11.11
#>
[CmdletBinding()]
param(
    [string] $PluginRoot = "$env:ProgramData\Jellyfin\Server\plugins",
    [string] $JellyfinVersion,
    [switch] $SkipInstall,
    [switch] $RestartJellyfin
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'src\Jellyfin.Plugin.ReleaseHub\Jellyfin.Plugin.ReleaseHub.csproj'
$buildYaml = Join-Path $repoRoot 'build.yaml'

# Single source of truth for name/guid/version and the build targets: build.yaml, the same file the
# release workflow reads (through scripts/build_targets.py).
$meta = @{}
$targets = @()
$inTargets = $false

foreach ($line in Get-Content $buildYaml) {
    if ($line -match '^\S') {
        $inTargets = $line -match '^targets\s*:'
    }

    if ($line -match '^(name|guid|version|owner|category|overview)\s*:\s*"?([^"]*)"?\s*$') {
        $meta[$Matches[1]] = $Matches[2].Trim()
        continue
    }

    if (-not $inTargets) {
        continue
    }

    # "  - generation: ..." opens a target; the indented keys after it belong to that target.
    if ($line -match '^\s+-\s+(\w+)\s*:\s*"?([^"]*)"?\s*$') {
        $targets += @{ $Matches[1] = $Matches[2].Trim() }
    }
    elseif ($line -match '^\s+(\w+)\s*:\s*"?([^"]*)"?\s*$' -and $targets.Count -gt 0) {
        $targets[-1][$Matches[1]] = $Matches[2].Trim()
    }
}

if ($meta.version -notmatch '^\d+\.\d+\.\d+$') {
    throw "build.yaml must declare a three-part version such as 1.1.0; found '$($meta.version)'."
}

if ($targets.Count -eq 0) {
    throw 'build.yaml declares no targets.'
}

foreach ($t in $targets) {
    # Published as ReleaseHub's version plus the Jellyfin generation, like the release workflow does,
    # so a local install sorts against catalogue installs exactly as a released one would.
    $t.pluginVersion = "$($meta.version).$($t.generation)"
}

function ConvertTo-FullVersion([string] $text) {
    # Four parts, missing ones as zero. [version] treats a missing part as lower than 0, which would
    # rank a 10.11.11 server below a 10.11.11.0 targetAbi and refuse the build made for it.
    $match = [regex]::Match($text, '^\d+(\.\d+){0,3}')
    if (-not $match.Success) {
        return $null
    }

    $parts = @($match.Value.Split('.') | ForEach-Object { [int] $_ })
    while ($parts.Count -lt 4) {
        $parts += 0
    }

    return [version]::new($parts[0], $parts[1], $parts[2], $parts[3])
}

function Get-LocalJellyfinVersion {
    $dll = Join-Path $env:ProgramFiles 'Jellyfin\Server\jellyfin.dll'
    if (Test-Path $dll) {
        return (Get-Item $dll).VersionInfo.ProductVersion
    }

    return $null
}

function Select-Target([string] $serverVersion) {
    # What the plugin catalogue does: the newest build whose targetAbi the server satisfies.
    $server = ConvertTo-FullVersion $serverVersion
    if ($null -eq $server) {
        throw "'$serverVersion' is not a Jellyfin version."
    }

    $compatible = @($targets |
        Where-Object { (ConvertTo-FullVersion $_.targetAbi) -le $server } |
        Sort-Object { ConvertTo-FullVersion $_.targetAbi } -Descending)

    if ($compatible.Count -eq 0) {
        throw "No ReleaseHub build supports Jellyfin $serverVersion. See targets in build.yaml."
    }

    return $compatible[0]
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

function Build-Target($t) {
    Write-Host "Building $($meta.name) $($t.pluginVersion) for Jellyfin $($t.targetAbi)+ ($($t.framework))" -ForegroundColor Cyan

    $outDir = Join-Path $repoRoot "artifacts\$($t.framework)"
    # Stamped from build.yaml rather than from the csproj: the install folder is named after that
    # version, and a folder claiming one version around an assembly claiming another is the kind of
    # mismatch that is only noticed once Jellyfin refuses to update the plugin.
    # To the console, not the pipeline: this function's output is the path it returns.
    dotnet build $project -c Release -f $t.framework -o $outDir --nologo `
        -p:Version=$($t.pluginVersion) `
        -p:AssemblyVersion=$($t.pluginVersion) `
        -p:FileVersion=$($t.pluginVersion) | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }

    $built = Join-Path $outDir 'Jellyfin.Plugin.ReleaseHub.dll'
    if (-not (Test-Path $built)) {
        throw "Expected assembly not found at $built"
    }

    Write-Host "Built $built" -ForegroundColor Green
    return $built
}

if (-not $JellyfinVersion) {
    $JellyfinVersion = Get-LocalJellyfinVersion
}

if ($SkipInstall -and -not $JellyfinVersion) {
    # Nothing to match against, so build them all.
    foreach ($t in $targets) {
        Build-Target $t | Out-Null
    }

    return
}

if (-not $JellyfinVersion) {
    throw @"
Could not find a local Jellyfin server to read its version from.
Pass -JellyfinVersion (for example 12.1.0) to choose the build to install.
"@
}

$selected = Select-Target $JellyfinVersion
Write-Host "Jellyfin $JellyfinVersion takes the build for targetAbi $($selected.targetAbi)" -ForegroundColor DarkGray

$dll = Build-Target $selected

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

$target = Join-Path $PluginRoot "ReleaseHub_$($selected.pluginVersion)"
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
    targetAbi   = $selected.targetAbi
    version     = $selected.pluginVersion
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
