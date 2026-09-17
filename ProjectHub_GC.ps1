param(
    [string]$ServerBaseUrl = 'https://projecthub.ornithopter.bid',
    [string]$ProjectId,
    [string]$WorkstationId,
    [string]$GatewayUrl = 'https://dfblackbox-nas.duckdns.org:8443/projecthub/',
    [int]$TtlHours = 24,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$sessionsUrl = $ServerBaseUrl.TrimEnd('/') + '/api/large-data/sessions'
if ($ProjectId) { $sessionsUrl += '?projectId=' + [Uri]::EscapeDataString($ProjectId); if ($WorkstationId) { $sessionsUrl += '&workstationId=' + [Uri]::EscapeDataString($WorkstationId) } }
elseif ($WorkstationId) { $sessionsUrl += '?workstationId=' + [Uri]::EscapeDataString($WorkstationId) }
$response = Invoke-RestMethod $sessionsUrl -TimeoutSec 30
$sessions = if ($response -is [Array]) { @($response) } else { @($response) }
$now = [DateTimeOffset]::UtcNow
Write-Host 'ProjectHub Large Data GC'
$safe = @(); $keep = 0; $review = 0; $reclaim = 0L
foreach ($session in $sessions) {
    if ($null -eq $session -or $null -eq $session.sessionId) { continue }
    $lastActivityValue = @($session.lastActivityAt) | Select-Object -First 1
    if (-not $lastActivityValue) { Write-Host ("[REVIEW] {0} missing last activity metadata" -f $session.sessionId); $review++; continue }
    $age = $now - [DateTimeOffset]([string]$lastActivityValue)
    $lifecycle = [int](@($session.lifecycle) | Select-Object -First 1)
    $active = $lifecycle -eq 2 -and $age.TotalHours -lt $TtlHours
    if ($active) { Write-Host ("[KEEP] {0} UPLOADING {1:N0} bytes last activity {2:N1}h ago" -f $session.sessionId,$session.object.sizeBytes,$age.TotalHours); $keep++; continue }
    if ($lifecycle -eq 2 -and $age.TotalHours -ge $TtlHours -and $Apply) { Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/') + '/api/large-data/sessions/' + $session.sessionId + '/abandon') -Method Post | Out-Null }
    $sizeBytes = [int64](@($session.object.sizeBytes) | Select-Object -First 1)
    if (($lifecycle -eq 2 -and $age.TotalHours -ge $TtlHours) -or $lifecycle -in @(8,9,10)) { $safe += $session; $reclaim += $sizeBytes; Write-Host ("[SAFE] {0} {1} {2:N0} bytes age {3:N1}h" -f $session.sessionId,$lifecycle,$sizeBytes,$age.TotalHours) } else { Write-Host ("[REVIEW] {0} lifecycle {1}" -f $session.sessionId,$lifecycle); $review++ }
}
if ($Apply) { foreach ($session in $safe) { $body=@{projectId=$session.projectId;workstationId=$session.workstationId;objectHash=$session.object.sha256;sizeBytes=$session.object.sizeBytes;operation=3;uploadSessionId=$session.sessionId;storageScope=$session.storageScope}|ConvertTo-Json; $token=(Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/')+'/api/large-data/assertions') -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30).assertion; Invoke-RestMethod ($GatewayUrl.TrimEnd('/')+'/cleanup-session.php') -Method Post -Headers @{Authorization="Bearer $token"} -TimeoutSec 30|Out-Null; Write-Host "[DELETED] $($session.sessionId)" -ForegroundColor Green } }
else { Write-Host 'Dry-run only. Use -Apply to clean SAFE sessions.' }
Write-Host ("Summary: safe={0}, keep={1}, review={2}, reclaimable={3:N0} bytes" -f $safe.Count,$keep,$review,$reclaim)
