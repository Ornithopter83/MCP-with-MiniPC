param(
    [string]$ProjectRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$CheckpointSha
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$configPath = Join-Path $ProjectRoot 'ProjectHub\config\project.json'; if (-not (Test-Path -LiteralPath $configPath)) { $configPath = Join-Path $ProjectRoot '.projecthub\project.json' }
$config = Get-Content -Raw $configPath | ConvertFrom-Json
$stateDir = if ($configPath -match '\\ProjectHub\\config\\') { Join-Path $ProjectRoot 'ProjectHub\state' } else { Join-Path $ProjectRoot '.projecthub' }
New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
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
function Download-WithProgress([string]$uri, [string]$token, [string]$target, [string]$relativePath, [int64]$expectedBytes) {
    $client = [System.Net.Http.HttpClient]::new()
    $response = $null; $input = $null; $output = $null
    try {
        $client.Timeout = [TimeSpan]::FromMinutes(10)
        $client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
        $response = $client.GetAsync($uri, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            throw "Download failed: HTTP $([int]$response.StatusCode) $body"
        }
        $contentLength = $response.Content.Headers.ContentLength
        $totalBytes = if ($contentLength) { [int64]$contentLength } else { $expectedBytes }
        $input = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $output = [IO.File]::Create($target)
        $buffer = New-Object byte[] (1MB); $received = 0L; $lastReport = [DateTime]::UtcNow
        while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $output.Write($buffer, 0, $read); $received += $read
            $now = [DateTime]::UtcNow
            if (($now - $lastReport).TotalMilliseconds -ge 150 -or ($totalBytes -gt 0 -and $received -ge $totalBytes)) {
                $percent = if ($totalBytes -gt 0) { [Math]::Min(100, [int](($received * 100) / $totalBytes)) } else { 0 }
                Write-Progress -Activity 'ProjectHub NAS download' -Status ("{0} {1}% ({2:N0}/{3:N0} bytes)" -f $relativePath,$percent,$received,$totalBytes) -PercentComplete $percent
                Write-Host ("  download {0}% - {1:N0}/{2:N0} bytes" -f $percent,$received,$totalBytes)
                $lastReport = $now
            }
        }
        Write-Progress -Activity 'ProjectHub NAS download' -Completed
        Write-Host ("  download complete - {0:N0} bytes" -f $received) -ForegroundColor Green
    } finally {
        if ($output) { $output.Dispose() }; if ($input) { $input.Dispose() }; if ($response) { $response.Dispose() }; $client.Dispose()
    }
}
function Confirm-DeleteCandidates($candidates) {
    Add-Type -AssemblyName System.Windows.Forms
    $approved = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $candidates.Count; $index++) {
        $candidate = $candidates[$index]
        $form = New-Object System.Windows.Forms.Form; $form.FormBorderStyle='None'; $form.Width=620; $form.Height=285; $form.StartPosition='CenterScreen'; $form.TopMost=$true; $form.BackColor=[Drawing.Color]::White
        $titleBar=New-Object System.Windows.Forms.Panel; $titleBar.Left=0; $titleBar.Top=0; $titleBar.Width=620; $titleBar.Height=48; $titleBar.BackColor=[Drawing.Color]::FromArgb(31,78,121)
        $title=New-Object System.Windows.Forms.Label; $title.Left=18; $title.Top=11; $title.Width=580; $title.Height=30; $title.ForeColor=[Drawing.Color]::White; $title.Font=New-Object Drawing.Font('Malgun Gothic',14,[Drawing.FontStyle]::Bold); $title.Text='ProjectHub - 로컬 파일 삭제 확인'; $titleBar.Controls.Add($title); $form.Controls.Add($titleBar)
        $icon=New-Object System.Windows.Forms.PictureBox; $icon.Left=22; $icon.Top=70; $icon.Width=44; $icon.Height=44; $icon.SizeMode='StretchImage'; $icon.Image=[Drawing.SystemIcons]::Warning.ToBitmap(); $form.Controls.Add($icon)
        $label = New-Object System.Windows.Forms.Label; $label.Left=82; $label.Top=68; $label.Width=510; $label.Height=100; $label.Font=New-Object Drawing.Font('Malgun Gothic',11); $label.Text="ProjectHub 최신 상태에는 없는 관리 파일입니다:`r`n$($candidate.RelativePath)`r`n$([math]::Round($candidate.SizeBytes / 1MB, 2)) MB`r`n로컬에서도 삭제하시겠습니까?"; $form.Controls.Add($label)
        $buttons = @(@('모두(A)','All'),@('예(Y)','Yes'),@('아니오(N)','No'),@('취소(C)','Cancel')); $left = 24
        foreach ($buttonInfo in $buttons) { $button = New-Object System.Windows.Forms.Button; $button.Text=$buttonInfo[0]; $button.Tag=$buttonInfo[1]; $button.Left=$left; $button.Top=205; $button.Width=130; $button.Height=36; $button.Font=New-Object Drawing.Font('Malgun Gothic',10); $button.Add_Click({ $form.Tag=$this.Tag; $form.Close() }); $form.Controls.Add($button); $left += 145 }
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
    # PREPARE: download and verify every managed object before changing the project.
    foreach($item in $downloadItems){
        $target=Join-Path $downloadRoot $item.relativePath; New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        $body=@{projectId=$config.projectId;workstationId=$config.workstationId;objectHash=$item.object.sha256;sizeBytes=$item.object.sizeBytes;operation=2;uploadSessionId=([guid]::NewGuid().ToString('N'));storageScope='projecthub';relativePath=$item.relativePath}|ConvertTo-Json
        $token=(Invoke-RestMethod ($config.serverBaseUrl.TrimEnd('/')+'/api/large-data/assertions') -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30).assertion
        Write-Host ("DOWNLOAD: {0} ({1:N0} bytes)" -f $item.relativePath,[int64]$item.object.sizeBytes) -ForegroundColor Cyan
        Download-WithProgress ($config.gatewayUrl.TrimEnd('/')+'/download.php') $token $target $item.relativePath ([int64]$item.object.sizeBytes)
        $restored=Get-Item -LiteralPath $target
        if([int64]$restored.Length -ne [int64]$item.object.sizeBytes){throw "RESTORE_SIZE_MISMATCH: $($item.relativePath)"}
        if((Get-Sha256 $target) -ne ([string]$item.object.sha256).ToLowerInvariant()){throw "RESTORE_HASH_MISMATCH: $($item.relativePath)"}
    }
    # APPLY: all downloads are already verified, so only now touch project files.
    foreach($item in $downloadItems){$source=Join-Path $downloadRoot $item.relativePath;$target=Join-Path $ProjectRoot $item.relativePath;New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null;$existing=$local[$item.relativePath];if($existing -and [int64](Get-Item $existing).Length -eq [int64]$item.object.sizeBytes -and (Get-Sha256 $existing) -eq ([string]$item.object.sha256).ToLowerInvariant()){$skipped++;continue};Copy-Item -LiteralPath $source -Destination $target -Force;if($existing){$updated++}else{$downloaded++}}
    foreach($item in $approvedDeletes){Remove-Item -LiteralPath (Join-Path $ProjectRoot $item.RelativePath) -Force;$updated++}
    $matched=0; $mismatched=0; $missing=0
    foreach($item in $downloadItems){$target=Join-Path $ProjectRoot $item.relativePath;if(-not (Test-Path -LiteralPath $target -PathType Leaf)){$missing++;continue};$actual=Get-Item -LiteralPath $target;if([int64]$actual.Length -ne [int64]$item.object.sizeBytes -or (Get-Sha256 $target) -ne ([string]$item.object.sha256).ToLowerInvariant()){$mismatched++}else{$matched++}}
    Write-Host 'RESTORE_VERIFY' -ForegroundColor Cyan
    $expectedCount=@($downloadItems).Count
    Write-Host ("expected   : {0}`nmatched    : {1}`nmismatched : {2}`nmissing    : {3}" -f $expectedCount,$matched,$mismatched,$missing)
    if($mismatched -gt 0 -or $missing -gt 0){throw 'RESTORE_VERIFY_FAILED'}
    $deletedPaths=@($approvedDeletes | ForEach-Object { $_.RelativePath }); $result=[pscustomobject]@{checkpoint=$set.commitSha;downloaded=$downloaded;updated=$updated;deleted=@($approvedDeletes).Count;skipped=$skipped;localOnly=0;failed=$failed;expected=$expectedCount;matched=$matched;mismatched=$mismatched;missing=$missing;deletedPaths=$deletedPaths}; $result|ConvertTo-Json -Depth 8|Set-Content (Join-Path $stateDir 'restore-result.json') -Encoding UTF8
    Write-Host 'RESTORE COMPLETE' -ForegroundColor Green; Write-Host ("Downloaded : {0}`nUpdated    : {1}`nDeleted    : {2}`nSkipped    : {3}`nLocal-only : {4}`nFailed     : {5}" -f $downloaded,$updated,$approvedDeletes.Count,$skipped,0,$failed)
} finally { $sha256.Dispose(); Remove-Item -LiteralPath $downloadRoot -Recurse -Force -ErrorAction SilentlyContinue }
