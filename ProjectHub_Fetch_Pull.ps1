[CmdletBinding()]
param(
    [string]$ProjectPath = (Get-Location).Path,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RestoreArguments
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([string[]]$Arguments)
    & git -C $script:GitRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed ($LASTEXITCODE): git $($Arguments -join ' ')"
    }
}

$script:GitRoot = (git -C $ProjectPath rev-parse --show-toplevel 2>$null).Trim()
if (-not $script:GitRoot) { throw 'ProjectPath must be inside a Git repository.' }
$branch = (git -C $script:GitRoot branch --show-current 2>$null).Trim()
if (-not $branch) { throw 'Detached HEAD detected. Fetch/Pull stopped.' }
if (-not (git -C $script:GitRoot remote get-url origin 2>$null).Trim()) { throw 'Git remote origin is not configured.' }
if (git -C $script:GitRoot rev-parse -q --verify MERGE_HEAD 2>$null) { throw 'Merge in progress. Resolve it before using Fetch_Pull.' }
if (git -C $script:GitRoot rev-parse -q --verify REBASE_HEAD 2>$null) { throw 'Rebase in progress. Resolve it before using Fetch_Pull.' }

$dirty = @(git -C $script:GitRoot status --porcelain=v1 --untracked-files=all)
if ($dirty.Count -gt 0) {
    Write-Host 'Local changes detected; Fetch/Pull stopped without modifying the repository.'
    $dirty | ForEach-Object { Write-Host "  $_" }
    exit 2
}

Write-Host "Git root: $script:GitRoot"
Write-Host "Branch: $branch"
Invoke-Git @('fetch', '--all', '--prune')
Invoke-Git @('pull', '--rebase')

$restoreScript = Join-Path $PSScriptRoot 'ProjectHub_Restore.ps1'
if (-not (Test-Path -LiteralPath $restoreScript)) { throw "Restore engine was not found: $restoreScript" }
Write-Host 'Running ProjectHub Restore.'
& $restoreScript -ProjectRoot $script:GitRoot @RestoreArguments
if ($LASTEXITCODE -ne 0) { throw "ProjectHub Restore failed ($LASTEXITCODE)." }
Write-Host 'FETCH_PULL_COMPLETE'
