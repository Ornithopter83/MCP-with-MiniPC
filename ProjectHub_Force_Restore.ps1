[CmdletBinding()]
param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$CheckpointSha,
    [switch]$SkipLargeDataRestore
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$gitRoot = (git -C $projectRoot rev-parse --show-toplevel 2>$null).Trim()
if (-not $gitRoot) { throw 'ProjectPath must be inside a Git repository.' }
$branch = (git -C $gitRoot branch --show-current 2>$null).Trim()
if (-not $branch) { throw 'Detached HEAD detected. Force restore stopped.' }
$origin = (git -C $gitRoot remote get-url origin 2>$null).Trim()
if (-not $origin) { throw 'Git remote origin is not configured.' }
$remoteBranch = "origin/$branch"

Write-Host '!!! PROJECTHUB FORCE RESTORE !!!' -ForegroundColor Red
Write-Host "Project : $gitRoot"
Write-Host "Remote  : $origin"
Write-Host "Branch  : $branch"
Write-Host ''
Write-Host 'The following local state will be discarded:' -ForegroundColor Yellow
Write-Host '- tracked modifications: git reset --hard'
Write-Host '- untracked files and directories: git clean -fd'
Write-Host '- local-only files outside protected ProjectHub settings and launchers'
Write-Host ''
Write-Host 'This action cannot be undone by ProjectHub.' -ForegroundColor Red
$confirmation = Read-Host 'Type FORCE to continue, or anything else to cancel'
if ($confirmation -cne 'FORCE') { Write-Host 'Force restore cancelled. No repository changes were made.'; exit 2 }

$backupRoot = Join-Path ([IO.Path]::GetTempPath()) ('projecthub-force-restore-' + [guid]::NewGuid().ToString('N'))
$backupManaged = Join-Path $backupRoot 'ProjectHub'
$backupConfig = Join-Path $backupRoot '.projecthub'
$backupCmd = Join-Path $backupRoot 'cmd'
$backupBin = Join-Path $backupRoot 'bin'
New-Item -ItemType Directory -Path $backupManaged,$backupConfig,$backupCmd,$backupBin -Force | Out-Null

function Invoke-Git {
    param([string[]]$Arguments)
    & git -C $gitRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git command failed ($LASTEXITCODE): git $($Arguments -join ' ')" }
}

try {
    $configPath = Join-Path $gitRoot '.projecthub\project.json'
    if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination (Join-Path $backupConfig 'project.json') -Force }
    $managedPath = Join-Path $gitRoot 'ProjectHub'
    if (Test-Path -LiteralPath $managedPath) { Copy-Item -LiteralPath $managedPath -Destination $backupRoot -Recurse -Force }
    Get-ChildItem -LiteralPath $gitRoot -Filter 'ProjectHub_*.cmd' -File -Force -ErrorAction SilentlyContinue | Copy-Item -Destination $backupCmd -Force
    $binPath = Join-Path $gitRoot 'bin'
    if (Test-Path -LiteralPath $binPath) { Get-ChildItem -LiteralPath $binPath -Filter 'ProjectHub_*.ps1' -File -Force | Copy-Item -Destination $backupBin -Force }

    Invoke-Git @('fetch', 'origin')
    $remoteExists = git -C $gitRoot rev-parse --verify $remoteBranch 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $remoteExists) { throw "Remote branch not found: $remoteBranch" }
    Invoke-Git @('reset', '--hard', $remoteBranch)
    Invoke-Git @('clean', '-fd', '-e', 'ProjectHub/', '-e', 'ProjectHub_*.cmd')

    $restoredManaged = Join-Path $gitRoot 'ProjectHub'
    if (Test-Path -LiteralPath (Join-Path $backupRoot 'ProjectHub')) { Copy-Item -LiteralPath (Join-Path $backupRoot 'ProjectHub') -Destination $gitRoot -Recurse -Force }
    $restoredConfigDir = Join-Path $gitRoot 'ProjectHub\config'
    if (-not (Test-Path -LiteralPath $restoredConfigDir)) { $restoredConfigDir = Join-Path $gitRoot '.projecthub' }
    New-Item -ItemType Directory -Path $restoredConfigDir -Force | Out-Null
    if (Test-Path -LiteralPath (Join-Path $backupConfig 'project.json')) { Copy-Item -LiteralPath (Join-Path $backupConfig 'project.json') -Destination (Join-Path $restoredConfigDir 'project.json') -Force }
    Get-ChildItem -LiteralPath $backupCmd -File -Force | Copy-Item -Destination $gitRoot -Force
    $restoredBin = Join-Path $gitRoot 'bin'; New-Item -ItemType Directory -Path $restoredBin -Force | Out-Null
    Get-ChildItem -LiteralPath $backupBin -File -Force | Copy-Item -Destination $restoredBin -Force

    if (-not $SkipLargeDataRestore) {
        $restoreScript = Join-Path $restoredBin 'ProjectHub_Restore.ps1'
        if (-not (Test-Path -LiteralPath $restoreScript)) { throw "Restore engine was not found after reset: $restoreScript" }
        if ($CheckpointSha) {
            & $restoreScript -ProjectRoot $gitRoot -CheckpointSha $CheckpointSha
        } else {
            & $restoreScript -ProjectRoot $gitRoot
        }
        if ($LASTEXITCODE -ne 0) { throw "ProjectHub Large Data restore failed ($LASTEXITCODE)." }
        $restoreResultPath = Join-Path $gitRoot '.projecthub\restore-result.json'
        if (-not (Test-Path -LiteralPath $restoreResultPath)) { throw 'ProjectHub Restore verification result was not created.' }
        $restoreResult = Get-Content -Raw -LiteralPath $restoreResultPath | ConvertFrom-Json
        if ([int]$restoreResult.mismatched -ne 0 -or [int]$restoreResult.missing -ne 0) { throw 'ProjectHub Restore verification was not clean.' }
    } else { Write-Host 'Large Data restore skipped by explicit switch.' -ForegroundColor Yellow }

    $finalStatus = @(git -C $gitRoot status --porcelain=v1 --untracked-files=all)
    $head = (git -C $gitRoot rev-parse HEAD).Trim()
    Write-Host ''
    Write-Host 'FORCE_RESTORE_COMPLETE' -ForegroundColor Green
    Write-Host "HEAD: $head"
    Write-Host ("Working tree entries after restore: {0}" -f $finalStatus.Count)
    if ($finalStatus.Count -gt 0) { $finalStatus | ForEach-Object { Write-Host "  $_" } }
} finally {
    Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
}
