[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = 'C:\AI-AGENT\ProjectHub'
$expectedBranch = 'UnityWorker'
$unityWorkerDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$projectFile = Join-Path $repositoryRoot 'src\ProjectHub.Worker\ProjectHub.Worker.csproj'
$publishDirectory = Join-Path $unityWorkerDirectory 'bin'
$publishedExecutable = Join-Path $publishDirectory 'UnityWorker.exe'
$deploymentDirectory = 'C:\AI-AGENT\Worker'
$deployedExecutable = Join-Path $deploymentDirectory 'UnityWorker.exe'

try {
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
        throw "Worker project file was not found: $projectFile"
    }

    $currentBranch = (& git -C $repositoryRoot branch --show-current)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the ProjectHub Git branch.'
    }
    if ($currentBranch.Trim() -ne $expectedBranch) {
        throw "Expected branch '$expectedBranch', found '$($currentBranch.Trim())'. Run the publish-worker.cmd command first."
    }

    if (Get-Process -Name 'UnityWorker', 'ProjectHub.Worker' -ErrorAction SilentlyContinue) {
        throw 'Worker is running. Stop it before publishing.'
    }

    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

    $arguments = @(
        'publish',
        $projectFile,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $publishDirectory,
        '-p:PublishSingleFile=true',
        '-p:AssemblyName=UnityWorker'
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    Write-Host "Branch     : $currentBranch"
    Write-Host "Project    : $projectFile"
    Write-Host "Publish    : $publishDirectory"
    Write-Host ('dotnet ' + ($arguments -join ' '))

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
        throw "Worker executable was not created: $publishedExecutable"
    }

    New-Item -ItemType Directory -Path $deploymentDirectory -Force | Out-Null
    Copy-Item -LiteralPath $publishedExecutable -Destination $deployedExecutable -Force

    $publishedHash = (Get-FileHash -LiteralPath $publishedExecutable -Algorithm SHA256).Hash
    $deployedHash = (Get-FileHash -LiteralPath $deployedExecutable -Algorithm SHA256).Hash
    if ($publishedHash -ne $deployedHash) {
        throw 'Published and deployed executable hashes do not match.'
    }

    Write-Host "Deployment : $deploymentDirectory"
    Write-Host "SHA256     : $deployedHash"
    Write-Host 'Worker publish completed.'
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
