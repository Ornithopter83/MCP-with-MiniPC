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
$sha256 = [Security.Cryptography.SHA256]::Create()
function Get-Sha256([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    try { return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose() }
}
function Get-ApiArray($value) {
    if ($null -eq $value) { return @() }
    if ($value -is [Array]) { return @($value) }
    return @($value)
}
function Test-RemovedLifecycle($lifecycle) {
    $value = [string]$lifecycle
    return $value -eq 'Removed' -or $value -eq 'REMOVED' -or $value -eq '7'
}
function Confirm-RemovalCandidates($candidates) {
    Add-Type -AssemblyName System.Windows.Forms
    $approved = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $candidates.Count; $index++) {
        $candidate = $candidates[$index]
        $form = New-Object System.Windows.Forms.Form; $form.FormBorderStyle='None'; $form.Width=620; $form.Height=285; $form.StartPosition='CenterScreen'; $form.TopMost=$true; $form.BackColor=[Drawing.Color]::White
        $titleBar=New-Object System.Windows.Forms.Panel; $titleBar.Left=0; $titleBar.Top=0; $titleBar.Width=620; $titleBar.Height=48; $titleBar.BackColor=[Drawing.Color]::FromArgb(31,78,121)
        $title=New-Object System.Windows.Forms.Label; $title.Left=18; $title.Top=11; $title.Width=580; $title.Height=30; $title.ForeColor=[Drawing.Color]::White; $title.Font=New-Object Drawing.Font('Malgun Gothic',14,[Drawing.FontStyle]::Bold); $title.Text='ProjectHub - 삭제 확인'; $titleBar.Controls.Add($title); $form.Controls.Add($titleBar)
        $icon=New-Object System.Windows.Forms.PictureBox; $icon.Left=22; $icon.Top=70; $icon.Width=44; $icon.Height=44; $icon.SizeMode='StretchImage'; $icon.Image=[Drawing.SystemIcons]::Warning.ToBitmap(); $form.Controls.Add($icon)
        $label = New-Object System.Windows.Forms.Label; $label.Left=82; $label.Top=68; $label.Width=510; $label.Height=100; $label.Font=New-Object Drawing.Font('Malgun Gothic',11); $label.Text="다음 관리 파일이 로컬에서 삭제되었습니다:`r`n$($candidate.RelativePath)`r`n$([math]::Round($candidate.SizeBytes / 1MB, 2)) MB`r`nProjectHub에서도 삭제 상태로 확정하시겠습니까?"; $form.Controls.Add($label)
        $buttons = @(@('모두(A)','All'),@('예(Y)','Yes'),@('아니오(N)','No'),@('취소(C)','Cancel')); $left = 24
        foreach ($buttonInfo in $buttons) { $button = New-Object System.Windows.Forms.Button; $button.Text=$buttonInfo[0]; $button.Tag=$buttonInfo[1]; $button.Left=$left; $button.Top=205; $button.Width=130; $button.Height=36; $button.Font=New-Object Drawing.Font('Malgun Gothic',10); $button.Add_Click({ $form.Tag=$this.Tag; $form.Close() }); $form.Controls.Add($button); $left += 145 }
        [void]$form.ShowDialog(); $choice=[string]$form.Tag; $form.Dispose()
        if ($choice -eq 'Cancel' -or [string]::IsNullOrWhiteSpace($choice)) { throw 'Sync cancelled by user.' }
        if ($choice -eq 'All') { for ($remaining=$index; $remaining -lt $candidates.Count; $remaining++) { $approved.Add($candidates[$remaining]) }; break }
        if ($choice -eq 'Yes') { $approved.Add($candidate) }
    }
    return @($approved)
}
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
$currentItems = @(Get-ChildItem -LiteralPath $ProjectPath -Recurse -File -Force | Where-Object { $_.Length -ge $threshold -and $_.FullName -notmatch '\\(\.git|bin|obj|node_modules|Library|Temp|Logs)(\\|$)' } | ForEach-Object { $relative = [Uri]::UnescapeDataString($projectUri.MakeRelativeUri([Uri]::new($_.FullName)).ToString()); [pscustomobject]@{ ProjectId=$ProjectId; RelativePath=$relative; FullPath=$_.FullName; SizeBytes=[int64]$_.Length; LastWriteTimeUtc=$_.LastWriteTimeUtc.ToUniversalTime().ToString('O'); LastWriteTimeUtcTicks=$_.LastWriteTimeUtc.Ticks; Sha256=(Get-Sha256 $_.FullName) } })
$managedResponse = Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/') + '/api/large-data/files/' + [Uri]::EscapeDataString($ProjectId)) -TimeoutSec 30
$managed = Get-ApiArray $managedResponse
$currentByPath = @{}; foreach ($item in $currentItems) { $currentByPath[$item.RelativePath] = $item }
$diff = [System.Collections.Generic.List[object]]::new(); $removedCandidates = [System.Collections.Generic.List[object]]::new(); $uploadItems = [System.Collections.Generic.List[object]]::new()
foreach ($item in $currentItems) {
    $previous = @($managed | Where-Object { $_.relativePath -eq $item.RelativePath } | Select-Object -First 1)
    if ($previous.Count -eq 0) { $diff.Add([pscustomobject]@{RelativePath=$item.RelativePath;Status='ADDED';SizeBytes=$item.SizeBytes}); $uploadItems.Add($item); continue }
    $old = $previous[0]
    if (Test-RemovedLifecycle $old.lifecycle) { $diff.Add([pscustomobject]@{RelativePath=$item.RelativePath;Status='FAILED';Reason='SERVER_REMOVED_REQUIRES_EXPLICIT_READD';SizeBytes=$item.SizeBytes}); continue }
    if ([string]$old.object.sha256 -ne $item.Sha256 -or [int64]$old.object.sizeBytes -ne $item.SizeBytes) { $diff.Add([pscustomobject]@{RelativePath=$item.RelativePath;Status='CHANGED';SizeBytes=$item.SizeBytes}); $uploadItems.Add($item) }
    else { $diff.Add([pscustomobject]@{RelativePath=$item.RelativePath;Status='UNCHANGED';SizeBytes=$item.SizeBytes}); $uploadItems.Add($item) }
}
foreach ($old in $managed | Where-Object { -not (Test-RemovedLifecycle $_.lifecycle) -and -not $currentByPath.ContainsKey([string]$_.relativePath) }) {
    $candidate = [pscustomobject]@{RelativePath=[string]$old.relativePath;SizeBytes=[int64]$old.object.sizeBytes;Sha256=[string]$old.object.sha256}
    $removedCandidates.Add($candidate); $diff.Add([pscustomobject]@{RelativePath=$candidate.RelativePath;Status='REMOVED';SizeBytes=$candidate.SizeBytes})
}
$approvedRemovals = @()
if ($removedCandidates.Count -gt 0) {
    $latestResponse = Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/') + '/api/large-data/checkpoints/' + [Uri]::EscapeDataString($ProjectId)) -TimeoutSec 30
    $latestSet = @(Get-ApiArray $latestResponse)[0]
    $checkpointSha = if ($latestSet) { [string]$latestSet.commitSha } else { '' }
    if (-not $head -or -not $latestSet -or $checkpointSha -ne [string]$head) {
        foreach ($candidate in $removedCandidates) { Write-Warning ("Deletion blocked by stale checkpoint: {0}" -f $candidate.RelativePath); $diff.Add([pscustomobject]@{RelativePath=$candidate.RelativePath;Status='FAILED';Reason='STALE_CHECKPOINT';SizeBytes=$candidate.SizeBytes}) }
    } else {
        $approvedRemovals = Confirm-RemovalCandidates @($removedCandidates)
        if ($approvedRemovals.Count -gt 0) {
            $removalBody = @{workstationId=$WorkstationId;localHeadSha=[string]$head;baseCheckpointSha=$checkpointSha;files=@($approvedRemovals | ForEach-Object { @{relativePath=$_.RelativePath} })} | ConvertTo-Json -Depth 6
            Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/') + '/api/large-data/removals/' + [Uri]::EscapeDataString($ProjectId)) -Method Post -ContentType 'application/json' -Body $removalBody -TimeoutSec 30 | Out-Null
        }
    }
}
$items = @($uploadItems)
$diffPath = "$manifestPath.diff.json"; $diff | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $diffPath -Encoding UTF8
$sha256.Dispose()
$manifest = [pscustomobject]@{ BatchId=$batchId; ProjectId=$ProjectId; WorkstationId=$WorkstationId; CapturedHeadSha=$head; CapturedBranch=$branch; CapturedDirty=$dirty; ServerBaseUrl=$ServerBaseUrl.TrimEnd('/'); GatewayUrl=$GatewayUrl.TrimEnd('/') + '/'; ProjectPath=$ProjectPath; Items=$items }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Batch snapshot: $($items.Count) large files; HEAD $head" -ForegroundColor Cyan
Write-Host "Manifest: $manifestPath"
Write-Host ("SYNC DIFF: Added={0}, Changed={1}, Unchanged={2}, Removed={3}, Failed={4}" -f @($diff | Where-Object Status -eq 'ADDED').Count,@($diff | Where-Object Status -eq 'CHANGED').Count,@($diff | Where-Object Status -eq 'UNCHANGED').Count,@($diff | Where-Object Status -eq 'REMOVED').Count,@($diff | Where-Object Status -eq 'FAILED').Count) -ForegroundColor Cyan
$state = @{ workstationId=$WorkstationId; displayName=$ProjectId; repositoryUrl=$null; branch=$branch; headSha=$head; dirty=$dirty; changedCount=$changedCount; untrackedCount=$untrackedCount; deletedCount=$deletedCount; diffFingerprint=("batch:{0};large_files:{1}" -f $batchId, $items.Count); lastFileActivity=$null } | ConvertTo-Json
Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/') + '/api/projects/' + [Uri]::EscapeDataString($ProjectId) + '/state') -Method Post -ContentType 'application/json' -Body $state -TimeoutSec 30 | Out-Null
Write-Host "Control-plane metadata synced. Starting separate uploader process; Git is not modified."
Start-Process powershell.exe -WindowStyle Normal -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $root 'ProjectHub_LargeData_Uploader.ps1'),'-ManifestPath',$manifestPath)
