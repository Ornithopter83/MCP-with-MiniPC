param(
    [string]$ProjectRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$CheckpointSha
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$config = Get-Content -Raw (Join-Path $ProjectRoot '.projecthub\project.json') | ConvertFrom-Json
$sha256 = [Security.Cryptography.SHA256]::Create()

function Get-ApiArray($value) {
    if ($null -eq $value) { return @() }
    if ($value -is [Array]) { return @($value) }
    return @($value)
}
function Get-Sha256([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    try { return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose() }
}
function Confirm-DeleteCandidates($candidates) {
    Add-Type -AssemblyName System.Windows.Forms
    $approved = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $candidates.Count; $index++) {
        $candidate = $candidates[$index]
        $form = New-Object System.Windows.Forms.Form
        $form.Text = 'ProjectHub - local file deletion'; $form.Width = 560; $form.Height = 220; $form.StartPosition = 'CenterScreen'; $form.TopMost = $true
        $label = New-Object System.Windows.Forms.Label; $label.Left = 16; $label.Top = 16; $label.Width = 510; $label.Height = 70
        $label.Text = "This managed file is not in the latest ProjectHub state:`r`n$($candidate.RelativePath)`r`n$([math]::Round($candidate.SizeBytes / 1MB, 2)) MB`r`nDelete it locally?"
        $form.Controls.Add($label)
        $buttons = @(@('All','All'),@('Yes','Yes'),@('No','No'),@('Cancel','Cancel')); $left = 16
        foreach ($buttonInfo in $buttons) { $button = New-Object System.Windows.Forms.Button; $button.Text=$buttonInfo[0]; $button.Tag=$buttonInfo[1]; $button.Left=$left; $button.Top=115; $button.Width=110; $button.Add_Click({ $form.Tag=$this.Tag; $form.Close() }); $form.Controls.Add($button); $left += 125 }
        [void]$form.ShowDialog(); $choice=[string]$form.Tag; $form.Dispose()
        if ($choice -eq 'Cancel' -or [string]::IsNullOrWhiteSpace($choice)) { throw 'Restore cancelled by user.' }
        if ($choice -eq 'All') { for ($remaining=$index; $remaining -lt $candidates.Count; $remaining++) { $approved.Add($candidates[$remaining]) }; break }
        if ($choice -eq 'Yes') { $approved.Add($candidate) }
    }
    return @($approved)
}

$sets = Get-ApiArray (Invoke-RestMethod (($config.serverBaseUrl.TrimEnd('/') + '/api/large-data/checkpoints/' + [Uri]::EscapeDataString($config.projectId))) -TimeoutSec 30)
if ($sets.Count -eq 0) { throw 'No checkpoints found.' }
$set = if ($CheckpointSha) { $sets | Where-Object { $_.commitSha -eq $CheckpointSha } | Select-Object -First 1 } else { $sets | Select-Object -First 1 }
if (-not $set) { throw 'Checkpoint not found.' }
$managed = Get-ApiArray (Invoke-RestMethod (($config.serverBaseUrl.TrimEnd('/') + '/api/large-data/files/' + [Uri]::EscapeDataString($config.projectId))) -TimeoutSec 30)
$projectUri = [Uri]::new(($ProjectRoot.TrimEnd('\') + '\')); $local = @{}
Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Force | Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|node_modules|Library|Temp|Logs)(\\|$)' } | ForEach-Object { $relative=[Uri]::UnescapeDataString($projectUri.MakeRelativeUri([Uri]::new($_.FullName)).ToString()); $local[$relative]=$_.FullName }
$downloadItems = Get-ApiArray $set.items; $deleteCandidates=[System.Collections.Generic.List[object]]::new()
foreach ($file in $managed | Where-Object { [string]$_.lifecycle -eq 'Removed' }) { if ($local.ContainsKey([string]$file.relativePath)) { $deleteCandidates.Add([pscustomobject]@{RelativePath=[string]$file.relativePath;SizeBytes=[int64]$file.object.sizeBytes}) } }
$downloadRoot=Join-Path ([IO.Path]::GetTempPath()) ('projecthub-restore-'+[guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
$downloaded=0; $updated=0; $skipped=0; $failed=0
try {
    $approvedDeletes=if($deleteCandidates.Count -gt 0){Confirm-DeleteCandidates @($deleteCandidates)}else{@()}
    foreach($item in $downloadItems){
        $target=Join-Path $downloadRoot $item.relativePath; New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        $body=@{projectId=$config.projectId;workstationId=$config.workstationId;objectHash=$item.object.sha256;sizeBytes=$item.object.sizeBytes;operation=2;uploadSessionId=([guid]::NewGuid().ToString('N'));storageScope='projecthub';relativePath=$item.relativePath}|ConvertTo-Json
        $token=(Invoke-RestMethod ($config.serverBaseUrl.TrimEnd('/')+'/api/large-data/assertions') -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30).assertion
        Invoke-WebRequest ($config.gatewayUrl.TrimEnd('/')+'/download.php') -Headers @{Authorization='Bearer '+$token} -OutFile $target -TimeoutSec 600 -UseBasicParsing
        $restored=Get-Item -LiteralPath $target
        if([int64]$restored.Length -ne [int64]$item.object.sizeBytes){throw "RESTORE_SIZE_MISMATCH: $($item.relativePath)"}
        if((Get-Sha256 $target) -ne ([string]$item.object.sha256).ToLowerInvariant()){throw "RESTORE_HASH_MISMATCH: $($item.relativePath)"}
    }
    foreach($item in $downloadItems){$source=Join-Path $downloadRoot $item.relativePath;$target=Join-Path $ProjectRoot $item.relativePath;New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null;$existing=$local[$item.relativePath];if($existing -and [int64](Get-Item $existing).Length -eq [int64]$item.object.sizeBytes -and (Get-Sha256 $existing) -eq ([string]$item.object.sha256).ToLowerInvariant()){$skipped++;continue};Copy-Item -LiteralPath $source -Destination $target -Force;if($existing){$updated++}else{$downloaded++}}
    foreach($item in $approvedDeletes){Remove-Item -LiteralPath (Join-Path $ProjectRoot $item.RelativePath) -Force;$updated++}
    $result=[pscustomobject]@{checkpoint=$set.commitSha;downloaded=$downloaded;updated=$updated;deleted=$approvedDeletes.Count;skipped=$skipped;localOnly=0;failed=$failed;deletedPaths=@($approvedDeletes.RelativePath)}; $result|ConvertTo-Json -Depth 8|Set-Content (Join-Path $ProjectRoot '.projecthub\restore-result.json') -Encoding UTF8
    Write-Host 'RESTORE COMPLETE' -ForegroundColor Green; Write-Host ("Downloaded : {0}`nUpdated    : {1}`nDeleted    : {2}`nSkipped    : {3}`nLocal-only : {4}`nFailed     : {5}" -f $downloaded,$updated,$approvedDeletes.Count,$skipped,0,$failed)
} finally { $sha256.Dispose(); Remove-Item -LiteralPath $downloadRoot -Recurse -Force -ErrorAction SilentlyContinue }
