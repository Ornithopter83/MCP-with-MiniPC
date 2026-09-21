[CmdletBinding()]
param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$CommitMessage = "ProjectHub sync",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$SyncArguments
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
if (-not $branch) { throw 'Detached HEAD detected. Commit/push stopped.' }
if (-not (git -C $script:GitRoot remote get-url origin 2>$null).Trim()) { throw 'Git remote origin is not configured.' }
if (git -C $script:GitRoot rev-parse -q --verify MERGE_HEAD 2>$null) { throw 'Merge in progress. Resolve it before using Commit_Push.' }
if (git -C $script:GitRoot rev-parse -q --verify REBASE_HEAD 2>$null) { throw 'Rebase in progress. Resolve it before using Commit_Push.' }

Write-Host "Git root: $script:GitRoot"
Write-Host "Branch: $branch"
Invoke-Git @('add', '-A')
$stagedCheck = & git -C $script:GitRoot diff --cached --quiet
if ($LASTEXITCODE -eq 1) {
    Invoke-Git @('commit', '-m', $CommitMessage)
} elseif ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect staged changes.'
} else {
    Write-Host 'No local changes to commit.'
}

Invoke-Git @('fetch', '--all', '--prune')
Invoke-Git @('pull', '--rebase')
Invoke-Git @('push', 'origin', $branch)

$syncScript = Join-Path $script:GitRoot 'bin\ProjectHub_Sync.ps1'
if (-not (Test-Path -LiteralPath $syncScript)) { throw "Sync engine was not found: $syncScript" }
Write-Host 'Running ProjectHub Sync and large-data checkpoint.'
& $syncScript -ProjectPath $script:GitRoot @SyncArguments
if ($LASTEXITCODE -ne 0) { throw "ProjectHub Sync failed ($LASTEXITCODE)." }
Write-Host 'COMMIT_PUSH_COMPLETE'
