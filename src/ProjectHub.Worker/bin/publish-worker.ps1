[CmdletBinding()]
param(
    [switch]$NoRestore
)

$projectDirectory = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectDirectory 'ProjectHub.Worker.csproj'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $projectDirectory)
$deploymentDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Worker'))
$arguments = @('publish', $projectFile, '--configuration', 'Release')
if ($NoRestore) { $arguments += '--no-restore' }

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishedExecutable = Join-Path $projectDirectory 'bin\ProjectHub.Worker.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    Write-Error "Published Worker executable was not found: $publishedExecutable"
    exit 1
}

New-Item -ItemType Directory -Path $deploymentDirectory -Force | Out-Null
Copy-Item -LiteralPath $publishedExecutable -Destination (Join-Path $deploymentDirectory 'ProjectHub.Worker.exe') -Force
$deploymentCommand = Join-Path $deploymentDirectory 'publish-worker.cmd'
$deploymentScript = Join-Path $deploymentDirectory 'publish-worker.ps1'
if (-not (Test-Path -LiteralPath $deploymentCommand -PathType Leaf)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish-worker.cmd') -Destination $deploymentCommand
    Write-Host "Publish wrapper copied to $deploymentCommand"
}
if (-not (Test-Path -LiteralPath $deploymentScript -PathType Leaf)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish-worker.ps1') -Destination $deploymentScript
    Write-Host "Publish script copied to $deploymentScript"
}
Write-Host "Worker deployment copied to $deploymentDirectory"
exit 0
