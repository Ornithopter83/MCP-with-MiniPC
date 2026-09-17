param([Parameter(Mandatory=$true)][string]$ManifestPath)

$ErrorActionPreference = "Stop"
$m = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
$chunkSize = 16MB
$chunkRoot = Join-Path ([IO.Path]::GetTempPath()) ("projecthub-upload-{0}" -f $m.BatchId)
New-Item -ItemType Directory -Path $chunkRoot -Force | Out-Null
$staged = @()

function Get-Assertion([object]$item, [string]$session, [string]$hash) {
    $body = @{ projectId=$m.ProjectId; workstationId=$m.WorkstationId; objectHash=$hash; sizeBytes=[int64]$item.SizeBytes; operation=1; uploadSessionId=$session; storageScope='projecthub' } | ConvertTo-Json
    (Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/assertions') -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30).assertion
}

try {
    foreach ($item in $m.Items) {
        $before = Get-Item -LiteralPath $item.FullPath -Force
        $hash = (Get-FileHash -LiteralPath $item.FullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $identityQuery = '?projectId=' + [Uri]::EscapeDataString($m.ProjectId) + '&workstationId=' + [Uri]::EscapeDataString($m.WorkstationId) + '&objectHash=' + $hash + '&sizeBytes=' + $item.SizeBytes
        $resume = $null
        try { $resume = Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/resumable-session' + $identityQuery) -Method Get -TimeoutSec 30 } catch { if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -ne 404) { throw } }
        $session = if ($resume) { [string]$resume.sessionId } else { [guid]::NewGuid().ToString('N') }
        $token = Get-Assertion $item $session $hash
        $headers = @{ Authorization="Bearer $token" }
        $start = Invoke-RestMethod ($m.GatewayUrl + 'upload-start.php') -Method Post -Headers $headers -TimeoutSec 30
        if ($start.state -eq 'complete') {
            if ([int64]$start.size_bytes -ne [int64]$item.SizeBytes -or $start.object_hash.ToLowerInvariant() -ne $hash) { throw "existing NAS object identity mismatch: $($item.RelativePath)" }
            Write-Host "ALREADY_PRESENT: $($item.RelativePath)" -ForegroundColor DarkGreen
        } else {
            $done = @()
            try { $done = @((Invoke-RestMethod ($m.GatewayUrl + 'upload-status.php') -Headers $headers -TimeoutSec 30).completed_chunks) } catch { }
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
                    }
                    $index++
                }
            } finally { $stream.Dispose() }
            $token = Get-Assertion $item $session $hash
            $final = Invoke-RestMethod ($m.GatewayUrl + 'upload-finalize.php') -Method Post -Headers @{Authorization="Bearer $token"} -TimeoutSec 600
            if ($final.state -ne 'complete') { throw "finalize failed: $($item.RelativePath)" }
        }
        Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/resumable-session/' + [Uri]::EscapeDataString($session) + '/complete') -Method Post -TimeoutSec 30 | Out-Null
        $after = Get-Item -LiteralPath $item.FullPath -Force
        if ($after.Length -ne [int64]$item.SizeBytes -or $after.LastWriteTimeUtc.Ticks -ne [int64]$item.LastWriteTimeUtcTicks) { Write-Warning "CHANGED_DURING_UPLOAD: $($item.RelativePath)"; continue }
        $staged += [pscustomobject]@{ projectId=$m.ProjectId; relativePath=$item.RelativePath; object=@{ sha256=$hash; sizeBytes=[int64]$item.SizeBytes }; lifecycle=3; checkpointCommitSha=$null }
        Write-Host "STAGED: $($item.RelativePath)" -ForegroundColor Green
    }
    foreach ($item in $staged) { Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/staged') -Method Post -ContentType 'application/json' -Body ($item | ConvertTo-Json -Depth 5) -TimeoutSec 30 | Out-Null }
    if ($staged.Count -eq $m.Items.Count -and $staged.Count -gt 0) {
        $checkpoint = @{ projectId=$m.ProjectId; commitSha=$m.CapturedHeadSha; items=$staged } | ConvertTo-Json -Depth 8
        Invoke-RestMethod ($m.ServerBaseUrl + '/api/large-data/checkpoint') -Method Post -ContentType 'application/json' -Body $checkpoint -TimeoutSec 30 | Out-Null
        Write-Host "CHECKPOINTED: $($m.CapturedHeadSha)" -ForegroundColor Green
    } else { Write-Warning "Checkpoint skipped: files changed or no large files were staged." }
} finally {
    Remove-Item -LiteralPath $chunkRoot -Recurse -Force -ErrorAction SilentlyContinue
}
