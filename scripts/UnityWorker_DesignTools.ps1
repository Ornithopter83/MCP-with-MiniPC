# Windows notebook setup for UnityWorker DESIGN: check by default, opt-in installs.
# No local generative models, ComfyUI, cloud credits, or remote machines.
param(
    [switch]$InstallInkscape,
    [switch]$InstallBlender
)
$ErrorActionPreference = 'Stop'

function Find-DesignTool {
    param([string]$Name)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)) {
        return $cmd.Source
    }

    $roots = @([Environment]::GetFolderPath('ProgramFiles'),
               [Environment]::GetFolderPath('ProgramFilesX86')) |
        Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    foreach ($root in $roots) {
        if ($Name -eq 'inkscape.exe') {
            foreach ($suffix in @('Inkscape\bin\inkscape.exe', 'Inkscape\inkscape.exe')) {
                $candidate = Join-Path $root $suffix
                if (Test-Path -LiteralPath $candidate) { return $candidate }
            }
        }
        if ($Name -eq 'blender.exe') {
            $vendor = Join-Path $root 'Blender Foundation'
            if (Test-Path -LiteralPath $vendor) {
                $folders = @(Get-ChildItem -LiteralPath $vendor -Directory -Filter 'Blender*' -ErrorAction SilentlyContinue |
                    Sort-Object Name -Descending)
                foreach ($folder in $folders) {
                    $candidate = Join-Path $folder.FullName 'blender.exe'
                    if (Test-Path -LiteralPath $candidate) { return $candidate }
                }
            }
        }
    }
    return $null
}

function Install-DesignTool {
    param([string]$Id, [string]$Name)
    $existing = Find-DesignTool $Name
    if ($existing) {
        Write-Host "INSTALLED: $existing"
        return
    }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget not found. Install App Installer manually and retry."
    }
    Write-Host "Installing $Id with user-requested winget operation..."
    & winget install --exact --id $Id --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) { throw "winget install failed for $Id ($LASTEXITCODE)" }
}

if ($InstallInkscape) {
    Install-DesignTool 'Inkscape.Inkscape' 'inkscape.exe'
}
if ($InstallBlender) {
    Install-DesignTool 'BlenderFoundation.Blender' 'blender.exe'
}

foreach ($name in @('inkscape.exe', 'blender.exe')) {
    $found = Find-DesignTool $name
    if ($found) {
        Write-Host "READY: $name = $found"
    } else {
        Write-Host "OPTIONAL_NOT_INSTALLED: $name"
    }
}
Write-Host "NO_LOCAL_MODELS: ComfyUI and local AI models are intentionally excluded."
Write-Host "NOTE: Restart UnityWorker if a tool was just installed; its PATH may be cached."
