param([Parameter(Mandatory=$true)][string]$ManifestPath)

$ErrorActionPreference = "Stop"
$exitCode = 0
$m = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
$chunkSize = 16MB
$chunkTransport = 'curl'
$chunkRoot = Join-Path ([IO.Path]::GetTempPath()) ("projecthub-upload-{0}" -f $m.BatchId)
New-Item -ItemType Directory -Path $chunkRoot -Force | Out-Null
$staged = @(); $failed = @(); $fileIndex = 0; $totalFiles = @($m.Items).Count
$script:assertionToken = $null; $script:assertionExpiresAt = [DateTimeOffset]::MinValue
$script:assertionIssuanceCount = 0; $script:assertionRefreshCount = 0
$resultPath = [IO.Path]::ChangeExtension($ManifestPath, '.result.json')

function Get-Assertion([object]$item, [string]$session, [string]$hash) {
    $body = @{ projectId=$m.ProjectId; workstationId=$m.WorkstationId; objectHash=$hash; sizeBytes=[int64]$item.SizeBytes; operation=1; uploadSessionId=$session; storageScope='projecthub'; relativePath=$item.RelativePath } | ConvertTo-Json
    $script:assertionIssuanceCount++
    return [string](Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/assertions') -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30).assertion
}

function Get-TokenExpiry([string]$token) {
    try {
        $payload = $token.Split('.')[1].Replace('-', '+').Replace('_', '/')
        while (($payload.Length % 4) -ne 0) { $payload += '=' }
        $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        return [DateTimeOffset]::FromUnixTimeSeconds([int64]$claims.exp)
    } catch { return [DateTimeOffset]::UtcNow }
}

function Get-CachedAssertion([object]$item, [string]$session, [string]$hash, [switch]$ForceRefresh) {
    if (-not $ForceRefresh -and $script:assertionToken -and $script:assertionExpiresAt -gt [DateTimeOffset]::UtcNow.AddSeconds(60)) { return $script:assertionToken }
    if ($script:assertionToken) { $script:assertionRefreshCount++ }
    $script:assertionToken = Get-Assertion $item $session $hash
    $script:assertionExpiresAt = Get-TokenExpiry $script:assertionToken
    return $script:assertionToken
}

function Get-HttpStatus([object]$errorRecord) { try { return [int]$errorRecord.Exception.Response.StatusCode.value__ } catch { return 0 } }

function Invoke-GatewayJson([string]$uri, [string]$method, [object]$item, [string]$session, [string]$hash) {
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        $token = Get-CachedAssertion $item $session $hash -ForceRefresh:($attempt -gt 0)
        try {
            $response = Invoke-WebRequest $uri -Method $method -Headers @{ Authorization="Bearer $token" } -TimeoutSec 600 -UseBasicParsing
            return ($response.Content | ConvertFrom-Json)
        } catch { if ($attempt -eq 0 -and (Get-HttpStatus $_) -eq 401) { continue }; throw }
    }
}

function Get-ExceptionDetail([object]$errorRecord) {
    $exception = $errorRecord.Exception
    $inner = if ($exception.InnerException) { $exception.InnerException.GetType().FullName + ': ' + $exception.InnerException.Message } else { '' }
    return [pscustomobject]@{ exceptionType=$exception.GetType().FullName; innerException=$inner; httpStatus=(Get-HttpStatus $errorRecord) }
}

function Send-Chunk([string]$uri, [string]$chunkPath, [object]$item, [string]$session, [string]$hash, [int]$chunkIndex, [int64]$chunkBytes) {
    $refreshed = $false
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        $token = Get-CachedAssertion $item $session $hash -ForceRefresh:($attempt -gt 0)
        try {
            if ($chunkTransport -eq 'curl') {
                $output = @(& curl.exe -sS --fail --max-time 600 -X PUT -H "Authorization: Bearer $token" -H 'Content-Type: application/octet-stream' --data-binary "@$chunkPath" -w "`nHTTP_STATUS:%{http_code}" $uri 2>&1)
                $curlExit = $LASTEXITCODE
                $joined = ($output -join "`n")
                $statusMatch = [regex]::Match($joined, 'HTTP_STATUS:(\d{3})\s*$')
                $httpStatus = if ($statusMatch.Success) { [int]$statusMatch.Groups[1].Value } else { 0 }
                $body = if ($statusMatch.Success) { $joined.Substring(0, $statusMatch.Index).Trim() } else { $joined.Trim() }
                if ($curlExit -ne 0 -or $httpStatus -lt 200 -or $httpStatus -ge 300) {
                    if ($attempt -eq 0 -and $httpStatus -eq 401) { $refreshed = $true; continue }
                    throw ("chunk transport failed: curl_exit={0}; http_status={1}; body={2}" -f $curlExit,$httpStatus,$body)
                }
                return ($body | ConvertFrom-Json)
            }
            $response = Invoke-WebRequest $uri -Method Put -InFile $chunkPath -ContentType 'application/octet-stream' -Headers @{ Authorization="Bearer $token" } -TimeoutSec 600 -UseBasicParsing
            return ($response.Content | ConvertFrom-Json)
        } catch {
            $detail = Get-ExceptionDetail $_
            $status = if ($chunkTransport -eq 'curl') { if ($_.Exception.Message -match 'http_status=(\d{3})') { [int]$Matches[1] } else { 0 } } else { $detail.httpStatus }
            if ($attempt -eq 0 -and $status -eq 401) { $refreshed = $true; continue }
            $diagnostic = "CHUNK_UPLOAD_FAILED file=$($item.RelativePath); session=$session; chunk_index=$chunkIndex; chunk_bytes=$chunkBytes; uri=$uri; http_status=$status; exception_type=$($detail.exceptionType); inner_exception=$($detail.innerException); assertion_refreshed=$refreshed"
            Write-Host $diagnostic -ForegroundColor Red
            throw $diagnostic
        }
    }
}

function Get-Sha256Hex([string]$path) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create(); $stream = $null
    try { $stream = [System.IO.File]::OpenRead($path); return (($algorithm.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join '') }
    finally { if ($stream) { $stream.Dispose() }; $algorithm.Dispose() }
}

try {
    foreach ($item in $m.Items) {
        $fileIndex++; $script:assertionToken = $null; $script:assertionExpiresAt = [DateTimeOffset]::MinValue
        try {
            $before = Get-Item -LiteralPath $item.FullPath -Force
            Write-Host "[$fileIndex/$totalFiles] HASHING: $($item.RelativePath) ($($item.SizeBytes) bytes)" -ForegroundColor Cyan
            $hash = Get-Sha256Hex $item.FullPath
            $identityQuery = '?projectId=' + [Uri]::EscapeDataString($m.ProjectId) + '&workstationId=' + [Uri]::EscapeDataString($m.WorkstationId) + '&objectHash=' + $hash + '&sizeBytes=' + $item.SizeBytes
            $resume = $null
            try { $resume = Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/resumable-session' + $identityQuery) -Method Get -TimeoutSec 30 } catch { if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -ne 404) { throw } }
            $session = if ($resume) { [string]$resume.sessionId } else { [guid]::NewGuid().ToString('N') }
            Write-Host "Using upload session: $session"
            $start = Invoke-GatewayJson ($m.GatewayUrl + 'upload-start.php') 'Post' $item $session $hash
            if ($start.state -eq 'complete') {
                if ([int64]$start.size_bytes -ne [int64]$item.SizeBytes -or $start.object_hash.ToLowerInvariant() -ne $hash) { throw 'existing NAS object identity mismatch' }
                if ($start.PSObject.Properties.Name -notcontains 'staging_cleaned' -or -not [bool]$start.staging_cleaned) { throw 'NAS staging cleanup was not confirmed' }
                Write-Host ("ALREADY_PRESENT: {0} ({1} bytes, SHA-256 {2})" -f $item.RelativePath,$start.size_bytes,$start.object_hash) -ForegroundColor DarkGreen
                Write-Host 'STAGING_CLEANED; OBJECT_RETAINED' -ForegroundColor DarkGreen
            } else {
                if ($resume -and $resume.chunkSizeBytes) { $chunkSize = [int64]$resume.chunkSizeBytes }
                $done = @(); try { $done = @((Invoke-GatewayJson ($m.GatewayUrl + 'upload-status.php') 'Get' $item $session $hash).completed_chunks) } catch { $done = @() }
                $totalChunks = [Math]::Ceiling([double]$item.SizeBytes / $chunkSize); $completedBytes = 0L
                foreach ($completedIndex in $done) { $remaining = [int64]$item.SizeBytes - ([int64]$completedIndex * $chunkSize); $completedBytes += [Math]::Min([int64]$chunkSize,[Math]::Max(0L,$remaining)) }
                Write-Host ("UPLOAD: {0} ({1}/{2} chunks, {3:N0}/{4:N0} bytes)" -f $item.RelativePath,$done.Count,$totalChunks,$completedBytes,$item.SizeBytes) -ForegroundColor Cyan
                $stream = [IO.File]::OpenRead($item.FullPath)
                try {
                    $buffer = New-Object byte[] $chunkSize; $index = 0
                    while (($read = $stream.Read($buffer,0,$buffer.Length)) -gt 0) {
                        if ($done -notcontains $index) {
                            $chunkPath = Join-Path $chunkRoot ("{0:D8}.part" -f $index); $chunkFile = [IO.File]::Create($chunkPath)
                            try { $chunkFile.Write($buffer,0,$read) } finally { $chunkFile.Dispose() }
                            [void](Send-Chunk ($m.GatewayUrl + "upload-chunk.php?chunk_index=$index") $chunkPath $item $session $hash $index $read)
                            Remove-Item -LiteralPath $chunkPath -Force; $completedBytes += $read
                            $percent = [int](($completedBytes * 100) / $item.SizeBytes)
                            Write-Progress -Activity 'ProjectHub NAS upload' -Status ("{0} {1}% ({2:N0}/{3:N0} bytes)" -f $item.RelativePath,$percent,$completedBytes,$item.SizeBytes) -PercentComplete $percent
                            Write-Host ("  chunk {0}/{1} complete - {2:N0}/{3:N0} bytes" -f ($index+1),$totalChunks,$completedBytes,$item.SizeBytes)
                        }; $index++
                    }
                } finally { $stream.Dispose() }
                Write-Progress -Activity 'ProjectHub NAS upload' -Completed; Write-Host "[$fileIndex/$totalFiles] FINALIZING: $($item.RelativePath)" -ForegroundColor Cyan
                $final = Invoke-GatewayJson ($m.GatewayUrl + 'upload-finalize.php') 'Post' $item $session $hash
                if ($final.state -ne 'complete') { throw 'finalize failed' }
                if ($final.PSObject.Properties.Name -notcontains 'staging_cleaned' -or -not [bool]$final.staging_cleaned) { throw 'NAS staging cleanup was not confirmed' }
                Write-Host 'STAGING_CLEANED; OBJECT_RETAINED' -ForegroundColor DarkGreen
            }
            Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/resumable-session/' + [Uri]::EscapeDataString($session) + '/complete') -Method Post -TimeoutSec 30 | Out-Null
            $after = Get-Item -LiteralPath $item.FullPath -Force
            if ($after.Length -ne [int64]$item.SizeBytes -or $after.LastWriteTimeUtc.Ticks -ne [int64]$item.LastWriteTimeUtcTicks) { throw 'CHANGED_DURING_UPLOAD' }
            $staged += [pscustomobject]@{ projectId=$m.ProjectId; relativePath=$item.RelativePath; object=@{sha256=$hash;sizeBytes=[int64]$item.SizeBytes}; lifecycle=3; checkpointCommitSha=$null }
            Write-Host "[$fileIndex/$totalFiles] DONE: STAGED $($item.RelativePath) (100%)" -ForegroundColor Green
        } catch {
            $message = $_.Exception.Message; $status = if ($message -eq 'CHANGED_DURING_UPLOAD') { 'CHANGED_DURING_UPLOAD' } else { 'FAILED' }
            $failed += [pscustomobject]@{relativePath=$item.RelativePath;status=$status;error=$message}; Write-Host "[$fileIndex/$totalFiles] $status`: $($item.RelativePath) - $message" -ForegroundColor Red; continue
        }
    }
    foreach ($item in $staged) { Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/staged') -Method Post -ContentType 'application/json' -Body ($item | ConvertTo-Json -Depth 5) -TimeoutSec 30 | Out-Null }
if ($failed.Count -eq 0 -and $staged.Count -eq $m.Items.Count -and $staged.Count -gt 0 -and $m.CapturedHeadSha -match '^[0-9a-fA-F]{40}$') {
        $checkpoint = @{projectId=$m.ProjectId;commitSha=$m.CapturedHeadSha;items=$staged} | ConvertTo-Json -Depth 8
        Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/checkpoint') -Method Post -ContentType 'application/json' -Body $checkpoint -TimeoutSec 30 | Out-Null; Write-Host "CHECKPOINTED: $($m.CapturedHeadSha)" -ForegroundColor Green
} elseif ($m.CapturedHeadSha -notmatch '^[0-9a-fA-F]{40}$') { Write-Warning 'Checkpoint skipped: a valid Git commit SHA is required.' }
else { Write-Warning ("Checkpoint skipped: {0} failed/changed file(s)." -f $failed.Count) }
$checkpointed = $failed.Count -eq 0 -and $staged.Count -eq $m.Items.Count -and $staged.Count -gt 0 -and $m.CapturedHeadSha -match '^[0-9a-fA-F]{40}$'
    $result = @{batchId=$m.BatchId;projectId=$m.ProjectId;staged=$staged;failed=$failed;checkpointed=$checkpointed;assertionsIssued=$script:assertionIssuanceCount;assertionsRefreshed=$script:assertionRefreshCount} | ConvertTo-Json -Depth 8
    $result | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Host ("ASSERTIONS: issued={0}, refreshed={1}; RESULT: {2}" -f $script:assertionIssuanceCount,$script:assertionRefreshCount,$resultPath) -ForegroundColor Cyan
    if ($failed.Count -gt 0) { Write-Host ("BATCH RESULT: {0} staged, {1} failed" -f $staged.Count,$failed.Count) -ForegroundColor Yellow; $exitCode = 1 }
} catch { Write-Host ("UPLOAD FAILED: " + $_.Exception.Message) -ForegroundColor Red; $exitCode = 1 }
finally { Remove-Item -LiteralPath $chunkRoot -Recurse -Force -ErrorAction SilentlyContinue }
Read-Host 'Batch uploader complete. Press Enter to close'
if ($exitCode -ne 0) { exit $exitCode }
