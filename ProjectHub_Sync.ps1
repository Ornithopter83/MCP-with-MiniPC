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
$branch = (git -C $ProjectPath branch --show-current).Trim()
$head = (git -C $ProjectPath rev-parse HEAD).Trim()
$dirty = [bool](git -C $ProjectPath status --porcelain)
$items = @(Get-ChildItem -LiteralPath $ProjectPath -Recurse -File -Force | Where-Object { $_.Length -ge $ThresholdBytes -and $_.FullName -notmatch '\\(\.git|bin|obj|node_modules|Library|Temp|Logs)(\\|$)' } | ForEach-Object { [pscustomobject]@{ ProjectId=$ProjectId; RelativePath=[IO.Path]::GetRelativePath($ProjectPath, $_.FullName).Replace('\','/'); FullPath=$_.FullName; SizeBytes=[int64]$_.Length; LastWriteTimeUtc=$_.LastWriteTimeUtc.ToUniversalTime().ToString('O'); LastWriteTimeUtcTicks=$_.LastWriteTimeUtc.Ticks } })
$manifest = [pscustomobject]@{ BatchId=[guid]::NewGuid().ToString('N'); ProjectId=$ProjectId; WorkstationId=$WorkstationId; CapturedHeadSha=$head; CapturedBranch=$branch; CapturedDirty=$dirty; ServerBaseUrl=$ServerBaseUrl.TrimEnd('/'); GatewayUrl=$GatewayUrl.TrimEnd('/') + '/'; ProjectPath=$ProjectPath; Items=$items }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Batch snapshot: $($items.Count) large files; HEAD $head" -ForegroundColor Cyan
Write-Host "Manifest: $manifestPath"
Write-Host "Starting separate uploader process. Main sync is complete; Git is not modified."
Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root 'ProjectHub_LargeData_Uploader.ps1'),'-ManifestPath',$manifestPath)
