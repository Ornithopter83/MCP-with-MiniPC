$ErrorActionPreference = "Stop"

$ProjectPath = "C:\Projects\AI-AGENTS\MCP\Server"
$AgentProject = ".\src\ProjectHub.Agent\ProjectHub.Agent.csproj"
$ServerBaseUrl = "https://projecthub.ornithopter.bid"

function Pause-Step([string]$Message) {
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Read-Host "Press Enter to continue"
}

Clear-Host
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " ProjectHub Agent E2E Test" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Server : $ServerBaseUrl"
Write-Host "Project: $ProjectPath"
Write-Host ""

if (-not (Test-Path $ProjectPath)) {
    Write-Host "ERROR: Project path not found:" -ForegroundColor Red
    Write-Host $ProjectPath -ForegroundColor Red
    Pause-Step "Test stopped"
    exit 1
}

Set-Location $ProjectPath

Pause-Step "1. Check remote ProjectHub Server"

try {
    $status = Invoke-RestMethod "$ServerBaseUrl/api/status" -TimeoutSec 10
    Write-Host ""
    Write-Host "SERVER ACCESS SUCCESS" -ForegroundColor Green
    $status | Format-List
}
catch {
    Write-Host ""
    Write-Host "SERVER ACCESS FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Pause-Step "Cannot continue until the server is reachable"
    exit 1
}

Pause-Step "2. Configure this DEV PC Agent"

$defaultWorkstation = "DEV-PC-01"
$workstationId = Read-Host "Workstation ID [$defaultWorkstation]"
if ([string]::IsNullOrWhiteSpace($workstationId)) { $workstationId = $defaultWorkstation }

$displayName = Read-Host "Display Name [$workstationId]"
if ([string]::IsNullOrWhiteSpace($displayName)) { $displayName = $workstationId }

$intervalText = Read-Host "Heartbeat interval seconds [15]"
if ([string]::IsNullOrWhiteSpace($intervalText)) { $intervalText = "15" }

$interval = 15
$tmp = 0
if ([int]::TryParse($intervalText, [ref]$tmp) -and $tmp -ge 1) {
    $interval = $tmp
} else {
    Write-Host "Invalid interval. Using 15 seconds." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Workstation : $workstationId"
Write-Host "Display Name: $displayName"
Write-Host "Hostname    : $env:COMPUTERNAME"
Write-Host "Interval    : $interval sec"

Pause-Step "3. Build ProjectHub.Agent"

dotnet build $AgentProject --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "Agent build FAILED." -ForegroundColor Red
    Pause-Step "Test stopped"
    exit 1
}

Write-Host ""
Write-Host "Agent build SUCCESS" -ForegroundColor Green

Pause-Step "4. Start Agent in a separate window"

$escapedProjectPath = $ProjectPath.Replace("'", "''")
$escapedAgentProject = $AgentProject.Replace("'", "''")
$escapedServer = $ServerBaseUrl.Replace("'", "''")
$escapedWorkstation = $workstationId.Replace("'", "''")
$escapedDisplay = $displayName.Replace("'", "''")

$agentCommand = @"
`$env:PROJECTHUB_AGENT_SERVER_BASE_URL='$escapedServer';
`$env:PROJECTHUB_AGENT_WORKSTATION_ID='$escapedWorkstation';
`$env:PROJECTHUB_AGENT_DISPLAY_NAME='$escapedDisplay';
`$env:PROJECTHUB_AGENT_HEARTBEAT_INTERVAL_SECONDS='$interval';
Set-Location '$escapedProjectPath';
Write-Host 'ProjectHub.Agent TEST - press Ctrl+C to stop' -ForegroundColor Cyan;
dotnet run --project '$escapedAgentProject' --no-build;
Write-Host '';
Read-Host 'Agent stopped. Press Enter to close'
"@

Start-Process powershell.exe -ArgumentList @(
    "-NoExit",
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-Command", $agentCommand
)

$waitSeconds = [Math]::Max(($interval * 2 + 5), 20)
Write-Host ""
Write-Host "Agent window started." -ForegroundColor Green
Write-Host "Waiting $waitSeconds seconds for at least two heartbeats..."
Start-Sleep -Seconds $waitSeconds

Pause-Step "5. Verify workstation through ProjectHub Server"

try {
    $rows = Invoke-RestMethod "$ServerBaseUrl/api/workstations" -TimeoutSec 10
    $match = @($rows) | Where-Object { $_.workstation_id -eq $workstationId }

    if ($match.Count -gt 0) {
        Write-Host ""
        Write-Host "AGENT HEARTBEAT FOUND" -ForegroundColor Green
        $match | Format-List workstation_id, display_name, hostname, last_seen, created_at, updated_at
    }
    else {
        Write-Host ""
        Write-Host "Agent row was not found." -ForegroundColor Red
        Write-Host "Check the separate Agent window for heartbeat errors." -ForegroundColor Yellow
    }
}
catch {
    Write-Host ""
    Write-Host "GET /api/workstations FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Manual recovery test (recommended)" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "1. Keep the Agent window running."
Write-Host "2. Stop ProjectHub.Server on the server PC."
Write-Host "3. Confirm the Agent logs failures but DOES NOT exit."
Write-Host "4. Start ProjectHub.Server again."
Write-Host "5. Confirm the same Agent window resumes heartbeat success."
Write-Host "6. Press Ctrl+C in the Agent window when finished."
Write-Host ""

Pause-Step "Agent E2E test script finished"
