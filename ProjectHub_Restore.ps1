param([string]$ProjectRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path), [string]$CheckpointSha, [string]$RestoreRoot)
$ErrorActionPreference='Stop'; $ProjectRoot=(Resolve-Path $ProjectRoot).Path; $config=Get-Content -Raw (Join-Path $ProjectRoot '.projecthub\project.json') | ConvertFrom-Json
$sets=@(Invoke-RestMethod (($config.serverBaseUrl.TrimEnd('/')+'/api/large-data/checkpoints/'+[Uri]::EscapeDataString($config.projectId))) -TimeoutSec 30)
if ($sets.Count -eq 0) { throw 'No checkpoints found.' }
$set = if ($CheckpointSha) { $sets | Where-Object { $_.commitSha -eq $CheckpointSha } | Select-Object -First 1 } else { $sets | Select-Object -First 1 }
if (-not $set) { throw 'Checkpoint not found.' }
if (-not $RestoreRoot) { $RestoreRoot=Join-Path (Split-Path $ProjectRoot -Parent) ('ProjectHub-Restore-'+(Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path $RestoreRoot -Force | Out-Null
$set | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $RestoreRoot 'restore-manifest.json') -Encoding UTF8
foreach ($item in @($set.items)) {
    $body=@{projectId=$config.projectId;workstationId=$config.workstationId;objectHash=$item.object.sha256;sizeBytes=$item.object.sizeBytes;operation=2;uploadSessionId=([guid]::NewGuid().ToString('N'));storageScope='projecthub';relativePath=$item.relativePath}|ConvertTo-Json
    $token=(Invoke-RestMethod ($config.serverBaseUrl.TrimEnd('/')+'/api/large-data/assertions') -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 30).assertion
    $target=Join-Path $RestoreRoot $item.relativePath; New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Invoke-WebRequest ($config.gatewayUrl.TrimEnd('/')+'/download.php') -Headers @{Authorization='Bearer '+$token} -OutFile $target -TimeoutSec 600 -UseBasicParsing
}
Write-Host "RESTORE_COMPLETE: $($set.commitSha) -> $RestoreRoot"
