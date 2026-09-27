[CmdletBinding()]
param(
    [string]$ProjectPath = (Get-Location).Path,
    [switch]$SkipRestore,
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

function Invoke-GitText {
    param([string[]]$Arguments)

    $output = & git -C $script:GitRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed ($LASTEXITCODE): git $($Arguments -join ' ')"
    }

    return (($output | Out-String).Trim())
}

function Test-GitAncestor {
    param(
        [string]$Ancestor,
        [string]$Descendant
    )

    & git -C $script:GitRoot merge-base --is-ancestor $Ancestor $Descendant *> $null
    if ($LASTEXITCODE -eq 0) { return $true }
    if ($LASTEXITCODE -eq 1) { return $false }

    throw "Git ancestry check failed ($LASTEXITCODE): $Ancestor -> $Descendant"
}

try {
    if (-not (Get-Command git.exe -ErrorAction SilentlyContinue) -and -not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git executable was not found in PATH.'
    }

    $resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
    $script:GitRoot = (& git -C $resolvedProjectPath rev-parse --show-toplevel 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $script:GitRoot) {
        throw 'ProjectPath must be inside a Git repository.'
    }

    $branch = (Invoke-GitText @('branch', '--show-current')).Trim()
    if (-not $branch) {
        throw 'Detached HEAD detected. Fetch/Pull stopped.'
    }

    $originUrl = (Invoke-GitText @('remote', 'get-url', 'origin')).Trim()
    if (-not $originUrl) {
        throw 'Git remote origin is not configured.'
    }

    foreach ($operationRef in @('MERGE_HEAD', 'REBASE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD')) {
        & git -C $script:GitRoot rev-parse -q --verify $operationRef *> $null
        if ($LASTEXITCODE -eq 0) {
            throw "$operationRef detected. Finish or abort the in-progress Git operation before using Fetch/Pull."
        }
    }

    $dirty = @(git -C $script:GitRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'git status failed.'
    }

    if ($dirty.Count -gt 0) {
        Write-Host 'Local changes detected; Fetch/Pull stopped without modifying the repository.' -ForegroundColor Yellow
        $dirty | ForEach-Object { Write-Host "  $_" }
        exit 2
    }

    Write-Host "Git root : $script:GitRoot"
    Write-Host "Branch   : $branch"
    Write-Host "Remote   : origin"

    Invoke-Git @('fetch', 'origin', '--prune')

    $remoteRef = "refs/remotes/origin/$branch"
    & git -C $script:GitRoot show-ref --verify --quiet $remoteRef
    if ($LASTEXITCODE -ne 0) {
        throw "Remote branch was not found: origin/$branch"
    }

    $localHead = (Invoke-GitText @('rev-parse', 'HEAD')).Trim()
    $remoteHead = (Invoke-GitText @('rev-parse', $remoteRef)).Trim()

    if ($localHead -eq $remoteHead) {
        Write-Host 'Git source is already up to date.' -ForegroundColor Green
    }
    elseif (Test-GitAncestor $localHead $remoteHead) {
        Write-Host "Fast-forwarding $branch to origin/$branch."
        Invoke-Git @('merge', '--ff-only', $remoteRef)
    }
    elseif (Test-GitAncestor $remoteHead $localHead) {
        $aheadCount = (Invoke-GitText @('rev-list', '--count', "$remoteRef..HEAD")).Trim()
        Write-Host "Local branch is already ahead of origin/$branch by $aheadCount commit(s); no history rewrite was performed." -ForegroundColor Yellow
    }
    else {
        throw "Local branch and origin/$branch have diverged. Fetch/Pull will not rebase, reset, or force-update automatically."
    }

    if ($SkipRestore) {
        Write-Host 'ProjectHub Restore skipped by -SkipRestore.' -ForegroundColor Yellow
    }
    else {
        $restoreScript = Join-Path $script:GitRoot 'bin\ProjectHub_Restore.ps1'
        if (-not (Test-Path -LiteralPath $restoreScript -PathType Leaf)) {
            throw "Restore engine was not found: $restoreScript"
        }

        Write-Host 'Running ProjectHub Restore.'
        & $restoreScript -ProjectRoot $script:GitRoot @RestoreArguments
    }

    Write-Host 'FETCH_PULL_COMPLETE' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host ("FETCH_PULL_FAILED: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
