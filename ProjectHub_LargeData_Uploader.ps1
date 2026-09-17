param([Parameter(Mandatory=$true)][string]$ManifestPath)

$ErrorActionPreference = "Stop"
$m = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
$chunkSize = 16MB
$chunkRoot = Join-Path ([IO.Path]::GetTempPath()) ("projecthub-upload-{0}" -f $m.BatchId)
New-Item -ItemType Directory -Path $chunkRoot -Force | Out-Null
$staged = @()
$completedNormally = $false
$fileIndex = 0
$totalFiles = @($m.Items).Count

function Get-Assertion([object]$item, [string]$session, [string]$hash) {
    $body = @{ projectId=$m.ProjectId; workstationId=$m.WorkstationId; objectHash=$hash; sizeBytes=[int64]$item.SizeBytes; operation=1; uploadSessionId=$session; storageScope='projecthub'; relativePath=$item.RelativePath } | ConvertTo-Json
    (Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/assertions') -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30).assertion
}

function Get-Sha256Hex([string]$path) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = $null
    try {
        $stream = [System.IO.File]::OpenRead($path)
        return (($algorithm.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally {
        if ($stream) { $stream.Dispose() }
        $algorithm.Dispose()
    }
}

try {
    foreach ($item in $m.Items) {
        $fileIndex++
        $before = Get-Item -LiteralPath $item.FullPath -Force
        Write-Host "[$fileIndex/$totalFiles] HASHING: $($item.RelativePath) ($($item.SizeBytes) bytes)" -ForegroundColor Cyan
        $hash = Get-Sha256Hex $item.FullPath
        $identityQuery = '?projectId=' + [Uri]::EscapeDataString($m.ProjectId) + '&workstationId=' + [Uri]::EscapeDataString($m.WorkstationId) + '&objectHash=' + $hash + '&sizeBytes=' + $item.SizeBytes
        $resume = $null
        Write-Host "Checking resumable session: $($item.RelativePath)"
        try { $resume = Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/resumable-session' + $identityQuery) -Method Get -TimeoutSec 30 } catch { if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -ne 404) { throw } }
        $session = if ($resume) { [string]$resume.sessionId } else { [guid]::NewGuid().ToString('N') }
        Write-Host "Using upload session: $session"
        $token = Get-Assertion $item $session $hash
        Write-Host "Assertion issued"
        $headers = @{ Authorization="Bearer $token" }
        Write-Host "Starting NAS upload session"
        $start = Invoke-RestMethod ($m.GatewayUrl + 'upload-start.php') -Method Post -Headers $headers -TimeoutSec 30
        if ($start.state -eq 'complete') {
            if ([int64]$start.size_bytes -ne [int64]$item.SizeBytes -or $start.object_hash.ToLowerInvariant() -ne $hash) { throw "existing NAS object identity mismatch: $($item.RelativePath)" }
            if ($start.PSObject.Properties.Name -contains 'staging_cleaned' -and -not [bool]$start.staging_cleaned) { throw "NAS staging cleanup was not confirmed: $($item.RelativePath)" }
            Write-Host ("ALREADY_PRESENT: {0} ({1} bytes, SHA-256 {2})" -f $item.RelativePath, $start.size_bytes, $start.object_hash) -ForegroundColor DarkGreen
        } else {
            $done = @()
            Write-Host "Reading NAS upload status"
            try { $done = @((Invoke-RestMethod ($m.GatewayUrl + 'upload-status.php') -Headers $headers -TimeoutSec 30).completed_chunks) } catch { }
            $totalChunks = [Math]::Ceiling([double]$item.SizeBytes / $chunkSize)
            $completedBytes = 0L
            foreach ($completedIndex in $done) {
                $remaining = [int64]$item.SizeBytes - ([int64]$completedIndex * $chunkSize)
                $completedBytes += [Math]::Min([int64]$chunkSize, [Math]::Max(0L, $remaining))
            }
            Write-Host ("UPLOAD: {0} ({1}/{2} chunks, {3:N0}/{4:N0} bytes)" -f $item.RelativePath, $done.Count, $totalChunks, $completedBytes, $item.SizeBytes) -ForegroundColor Cyan
            $stream = [IO.File]::OpenRead($item.FullPath)
            try {
                $buffer = New-Object byte[] $chunkSize
                $index = 0
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    if ($done -notcontains $index) {
                        $chunkPath = Join-Path $chunkRoot ("{0:D8}.part" -f $index)
                        $chunkFile = [IO.File]::Create($chunkPath)
                        try { $chunkFile.Write($buffer, 0, $read) } finally { $chunkFile.Dispose() }
                        $token = Get-Assertion $item $session $hash
                        curl.exe -sS --fail --max-time 300 -X PUT -H "Authorization: Bearer $token" -H 'Content-Type: application/octet-stream' --data-binary "@$chunkPath" ($m.GatewayUrl + "upload-chunk.php?chunk_index=$index") | Out-Null
                        if ($LASTEXITCODE -ne 0) { throw "chunk upload failed: $index" }
                        Remove-Item -LiteralPath $chunkPath -Force
                        $completedBytes += $read
                        $percent = [int](($completedBytes * 100) / $item.SizeBytes)
                        Write-Progress -Activity "ProjectHub NAS upload" -Status ("{0} {1}% ({2:N0}/{3:N0} bytes)" -f $item.RelativePath, $percent, $completedBytes, $item.SizeBytes) -PercentComplete $percent
                        Write-Host ("  chunk {0}/{1} complete - {2:N0}/{3:N0} bytes" -f ($index + 1), $totalChunks, $completedBytes, $item.SizeBytes)
                    }
                    $index++
                }
            } finally { $stream.Dispose() }
            Write-Progress -Activity "ProjectHub NAS upload" -Completed
            Write-Host "[$fileIndex/$totalFiles] FINALIZING: $($item.RelativePath)" -ForegroundColor Cyan
            $token = Get-Assertion $item $session $hash
            $final = Invoke-RestMethod ($m.GatewayUrl + 'upload-finalize.php') -Method Post -Headers @{Authorization="Bearer $token"} -TimeoutSec 600
            if ($final.state -ne 'complete') { throw "finalize failed: $($item.RelativePath)" }
            if ($final.PSObject.Properties.Name -notcontains 'staging_cleaned' -or -not [bool]$final.staging_cleaned) { throw "NAS staging cleanup was not confirmed: $($item.RelativePath)" }
        }
        Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/resumable-session/' + [Uri]::EscapeDataString($session) + '/complete') -Method Post -TimeoutSec 30 | Out-Null
        $after = Get-Item -LiteralPath $item.FullPath -Force
        if ($after.Length -ne [int64]$item.SizeBytes -or $after.LastWriteTimeUtc.Ticks -ne [int64]$item.LastWriteTimeUtcTicks) { Write-Warning "CHANGED_DURING_UPLOAD: $($item.RelativePath)"; continue }
        $staged += [pscustomobject]@{ projectId=$m.ProjectId; relativePath=$item.RelativePath; object=@{ sha256=$hash; sizeBytes=[int64]$item.SizeBytes }; lifecycle=3; checkpointCommitSha=$null }
        Write-Host "[$fileIndex/$totalFiles] DONE: STAGED $($item.RelativePath) (100%)" -ForegroundColor Green
    }
    foreach ($item in $staged) { Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/staged') -Method Post -ContentType 'application/json' -Body ($item | ConvertTo-Json -Depth 5) -TimeoutSec 30 | Out-Null }
    if ($staged.Count -eq $m.Items.Count -and $staged.Count -gt 0) {
        $checkpoint = @{ projectId=$m.ProjectId; commitSha=$m.CapturedHeadSha; items=$staged } | ConvertTo-Json -Depth 8
        Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/checkpoint') -Method Post -ContentType 'application/json' -Body $checkpoint -TimeoutSec 30 | Out-Null
        Write-Host "CHECKPOINTED: $($m.CapturedHeadSha)" -ForegroundColor Green
    } else { Write-Warning "Checkpoint skipped: files changed or no large files were staged." }
    $completedNormally = $true
} catch {
    Write-Host ("UPLOAD FAILED: " + $_.Exception.Message) -ForegroundColor Red
    Read-Host "오류를 확인했으면 Enter 키를 눌러 uploader를 종료하세요"
    exit 1
} finally {
    Remove-Item -LiteralPath $chunkRoot -Recurse -Force -ErrorAction SilentlyContinue
}
if ($completedNormally) { Read-Host "Batch uploader가 완료되었습니다. Enter 키를 눌러 종료하세요" }
