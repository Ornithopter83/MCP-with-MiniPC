param(
    [string]$ProjectPath,
    [string]$ProjectId,
    [string]$WorkstationId,
    [string]$ServerBaseUrl = "https://projecthub.ornithopter.bid",
    [string]$GatewayUrl = "https://dfblackbox-nas.duckdns.org:8443/projecthub/",
    [string]$ThresholdBytes = '100MB'
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ProjectPath) { $ProjectPath = $scriptRoot }
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$projectConfigPath = Join-Path $ProjectPath '.projecthub\project.json'
if (Test-Path -LiteralPath $projectConfigPath) {
    $projectConfig = Get-Content -Raw -LiteralPath $projectConfigPath | ConvertFrom-Json
    if (-not $ProjectId) { $ProjectId = [string]$projectConfig.projectId }
    if (-not $WorkstationId) { $WorkstationId = [string]$projectConfig.workstationId }
    if ($ServerBaseUrl -eq 'https://projecthub.ornithopter.bid' -and $projectConfig.serverBaseUrl) { $ServerBaseUrl = [string]$projectConfig.serverBaseUrl }
    if ($GatewayUrl -eq 'https://dfblackbox-nas.duckdns.org:8443/projecthub/' -and $projectConfig.gatewayUrl) { $GatewayUrl = [string]$projectConfig.gatewayUrl }
}
if (-not $ProjectId -or -not $WorkstationId) { throw 'ProjectId and WorkstationId are required or must be present in .projecthub/project.json.' }
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$thresholdMatch = [regex]::Match($ThresholdBytes.Trim(), '^(\d+)(B|KB|MB|GB|TB)?$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if (-not $thresholdMatch.Success) { throw "ThresholdBytes must be bytes or a value such as 500MB or 1GB." }
$threshold = [decimal]$thresholdMatch.Groups[1].Value
switch ($thresholdMatch.Groups[2].Value.ToUpperInvariant()) {
    'KB' { $threshold *= 1KB }
    'MB' { $threshold *= 1MB }
    'GB' { $threshold *= 1GB }
    'TB' { $threshold *= 1TB }
}
$threshold = [int64]$threshold
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$manifestPath = Join-Path ([IO.Path]::GetTempPath()) ("projecthub-batch-{0}.json" -f [guid]::NewGuid().ToString('N'))
$batchId = [guid]::NewGuid().ToString('N')
$branch = (git -C $ProjectPath branch --show-current).Trim()
$head = $null
try { $headOutput = & git -C $ProjectPath rev-parse --verify HEAD 2>$null; if ($LASTEXITCODE -eq 0) { $head = ([string]$headOutput).Trim() } } catch { $head = $null }
$statusLines = @(git -C $ProjectPath status --porcelain=v1 --untracked-files=all)
$dirty = $statusLines.Count -gt 0
$changedCount = @($statusLines | Where-Object { $_ -and $_.Substring(0, 2) -ne '??' }).Count
$untrackedCount = @($statusLines | Where-Object { $_ -and $_.Substring(0, 2) -eq '??' }).Count
$deletedCount = @($statusLines | Where-Object { $_ -and $_.Length -ge 2 -and $_.Substring(0, 2).Contains('D') }).Count
$projectUri = [Uri]::new(($ProjectPath.TrimEnd('\') + '\'))
$items = @(Get-ChildItem -LiteralPath $ProjectPath -Recurse -File -Force | Where-Object { $_.Length -ge $threshold -and $_.FullName -notmatch '\\(\.git|bin|obj|node_modules|Library|Temp|Logs)(\\|$)' } | ForEach-Object { $relative = [Uri]::UnescapeDataString($projectUri.MakeRelativeUri([Uri]::new($_.FullName)).ToString()); [pscustomobject]@{ ProjectId=$ProjectId; RelativePath=$relative; FullPath=$_.FullName; SizeBytes=[int64]$_.Length; LastWriteTimeUtc=$_.LastWriteTimeUtc.ToUniversalTime().ToString('O'); LastWriteTimeUtcTicks=$_.LastWriteTimeUtc.Ticks } })
$manifest = [pscustomobject]@{ BatchId=$batchId; ProjectId=$ProjectId; WorkstationId=$WorkstationId; CapturedHeadSha=$head; CapturedBranch=$branch; CapturedDirty=$dirty; ServerBaseUrl=$ServerBaseUrl.TrimEnd('/'); GatewayUrl=$GatewayUrl.TrimEnd('/') + '/'; ProjectPath=$ProjectPath; Items=$items }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Batch snapshot: $($items.Count) large files; HEAD $head" -ForegroundColor Cyan
Write-Host "Manifest: $manifestPath"
$state = @{ workstationId=$WorkstationId; displayName=$ProjectId; repositoryUrl=$null; branch=$branch; headSha=$head; dirty=$dirty; changedCount=$changedCount; untrackedCount=$untrackedCount; deletedCount=$deletedCount; diffFingerprint=("batch:{0};large_files:{1}" -f $batchId, $items.Count); lastFileActivity=$null } | ConvertTo-Json
Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/') + '/api/projects/' + [Uri]::EscapeDataString($ProjectId) + '/state') -Method Post -ContentType 'application/json' -Body $state -TimeoutSec 30 | Out-Null
Write-Host "Control-plane metadata synced. Starting separate uploader process; Git is not modified."
Start-Process powershell.exe -WindowStyle Normal -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root 'ProjectHub_LargeData_Uploader.ps1'),'-ManifestPath',$manifestPath)
