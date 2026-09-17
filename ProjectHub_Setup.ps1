param([string]$ProjectRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path), [string]$ServerBaseUrl = 'https://projecthub.ornithopter.bid', [string]$GatewayUrl = 'https://dfblackbox-nas.duckdns.org:8443/projecthub/', [string]$WorkstationId = $env:COMPUTERNAME, [string]$ProjectHubSource = (Split-Path -Parent $MyInvocation.MyCommand.Path), [string]$TemplateBaseUrl = 'https://raw.githubusercontent.com/Ornithopter83/MCP-with-MiniPC/main/')
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$gitRoot = (git -C $ProjectRoot rev-parse --show-toplevel).Trim()
if (-not $gitRoot) { throw 'ProjectRoot must be inside a Git repository.' }
$origin = (git -C $gitRoot remote get-url origin 2>$null).Trim()
$branch = (git -C $gitRoot branch --show-current 2>$null).Trim(); if (-not $branch) { $branch = (git -C $gitRoot symbolic-ref --short HEAD 2>$null).Trim() }; if (-not $branch) { $branch = 'main' }
$head = $null
try { $headOutput = & git -C $gitRoot rev-parse --verify HEAD 2>$null; if ($LASTEXITCODE -eq 0) { $head = ([string]$headOutput).Trim() } } catch { $head = $null }
$projectId = [IO.Path]::GetFileName(($origin -replace '\.git$','').TrimEnd('/','\'))
if (-not $projectId) { $projectId = Split-Path -Leaf $gitRoot }
$configDir = Join-Path $gitRoot '.projecthub'; New-Item -ItemType Directory -Path $configDir -Force | Out-Null
$configPath = Join-Path $configDir 'project.json'
if (-not (Test-Path -LiteralPath $configPath)) { @{projectId=$projectId;serverBaseUrl=$ServerBaseUrl.TrimEnd('/');gatewayUrl=$GatewayUrl.TrimEnd('/')+'/';workstationId=$WorkstationId;repositoryUrl=$origin} | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8 }
else { $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json; $projectId=[string]$config.projectId; $ServerBaseUrl=[string]$config.serverBaseUrl; $GatewayUrl=[string]$config.gatewayUrl; $WorkstationId=[string]$config.workstationId }
$state = @{workstationId=$WorkstationId;displayName=$projectId;repositoryUrl=$origin;branch=$branch;headSha=$head;dirty=(@(git -C $gitRoot status --porcelain=v1 --untracked-files=all).Count -gt 0);changedCount=0;untrackedCount=0;deletedCount=0;diffFingerprint='setup';lastFileActivity=$null} | ConvertTo-Json
Invoke-RestMethod ($ServerBaseUrl.TrimEnd('/')+'/api/projects/'+[Uri]::EscapeDataString($projectId)+'/state') -Method Post -ContentType 'application/json' -Body $state -TimeoutSec 30 | Out-Null
foreach ($name in 'ProjectHub_Sync.ps1','ProjectHub_LargeData_Uploader.ps1','ProjectHub_Restore.ps1','ProjectHub_Sync.cmd','ProjectHub_Restore.cmd') { $source=Join-Path $ProjectHubSource $name; $target=Join-Path $gitRoot $name; if (-not (Test-Path $target)) { if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination $target } else { Invoke-WebRequest ($TemplateBaseUrl.TrimEnd('/')+'/'+$name) -OutFile $target -TimeoutSec 60 } } }
if (-not (Test-Path (Join-Path $gitRoot 'ProjectHub_Sync.cmd'))) { "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0ProjectHub_Sync.ps1`" -ProjectPath `"%~dp0`" %*" | Set-Content (Join-Path $gitRoot 'ProjectHub_Sync.cmd') -Encoding ASCII }
if (-not (Test-Path (Join-Path $gitRoot 'ProjectHub_Restore.cmd'))) { "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0ProjectHub_Restore.ps1`" %*" | Set-Content (Join-Path $gitRoot 'ProjectHub_Restore.cmd') -Encoding ASCII }
foreach ($file in @('AGENTS.md','CurrentWork.md')) { $target=Join-Path $gitRoot $file; if (-not (Test-Path $target)) { "# $projectId`r`n`r`nProjectHub 관리 프로젝트입니다.`r`n" | Set-Content $target -Encoding UTF8 } }
if (-not (Test-Path (Join-Path $gitRoot 'tasks'))) { New-Item -ItemType Directory (Join-Path $gitRoot 'tasks') | Out-Null }
Write-Host "SETUP_COMPLETE: $projectId ($branch/$head)"
Write-Host "Config: $configPath"
