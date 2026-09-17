param(
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [Parameter(Mandatory=$true)][string]$ProjectId,
    [Parameter(Mandatory=$true)][string]$WorkstationId,
    [string]$ServerBaseUrl = "https://projecthub.ornithopter.bid",
    [string]$GatewayUrl = "https://dfblackbox-nas.duckdns.org:8443/projecthub/",
    [long]$ThresholdBytes = 1GB
)

$ErrorActionPreference = "Stop"
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path ([IO.Path]::GetTempPath()) ("projecthub-batch-{0}.json" -f [guid]::NewGuid().ToString('N'))
$batchId = [guid]::NewGuid().ToString('N')
$branch = (git -C $ProjectPath branch --show-current).Trim()
$head = (git -C $ProjectPath rev-parse HEAD).Trim()
$statusLines = @(git -C $ProjectPath status --porcelain=v1 --untracked-files=all)
$dirty = $statusLines.Count -gt 0
$changedCount = @($statusLines | Where-Object { $_ -and $_.Substring(0, 2) -ne '??' }).Count
$untrackedCount = @($statusLines | Where-Object { $_ -and $_.Substring(0, 2) -eq '??' }).Count
$deletedCount = @($statusLines | Where-Object { $_ -and $_.Length -ge 2 -and $_.Substring(0, 2).Contains('D') }).Count
$items = @(Get-ChildItem -LiteralPath $ProjectPath -Recurse -File -Force | Where-Object { $_.Length -ge $ThresholdBytes -and $_.FullName -notmatch '\\(\.git|bin|obj|node_modules|Library|Temp|Logs)(\\|$)' } | ForEach-Object { [pscustomobject]@{ ProjectId=$ProjectId; RelativePath=[IO.Path]::GetRelativePath($ProjectPath, $_.FullName).Replace('\','/'); FullPath=$_.FullName; SizeBytes=[int64]$_.Length; LastWriteTimeUtc=$_.LastWriteTimeUtc.ToUniversalTime().ToString('O'); LastWriteTimeUtcTicks=$_.LastWriteTimeUtc.Ticks } })
$manifest = [pscustomobject]@{ BatchId=$batchId; ProjectId=$ProjectId; WorkstationId=$WorkstationId; CapturedHeadSha=$head; CapturedBranch=$branch; CapturedDirty=$dirty; ServerBaseUrl=$ServerBaseUrl.TrimEnd('/'); GatewayUrl=$GatewayUrl.TrimEnd('/') + '/'; ProjectPath=$ProjectPath; Items=$items }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Batch snapshot: $($items.Count) large files; HEAD $head" -ForegroundColor Cyan
Write-Host "Manifest: $manifestPath"
$state = @{ workstationId=$WorkstationId; displayName=$ProjectId; repositoryUrl=$null; branch=$branch; headSha=$head; dirty=$dirty; changedCount=$changedCount; untrackedCount=$untrackedCount; deletedCount=$deletedCount; diffFingerprint=("batch:{0};large_files:{1}" -f $batchId, $items.Count); lastFileActivity=$null } | ConvertTo-Json
Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/') + '/api/projects/' + [Uri]::EscapeDataString($ProjectId) + '/state') -Method Post -ContentType 'application/json' -Body $state -TimeoutSec 30 | Out-Null
Write-Host "Control-plane metadata synced. Starting separate uploader process; Git is not modified."
Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root 'ProjectHub_LargeData_Uploader.ps1'),'-ManifestPath',$manifestPath)
