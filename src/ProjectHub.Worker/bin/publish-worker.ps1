[CmdletBinding()]
param(
    [switch]$NoRestore
)

$projectDirectory = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectDirectory 'ProjectHub.Worker.csproj'
$arguments = @('publish', $projectFile, '--configuration', 'Release')
if ($NoRestore) { $arguments += '--no-restore' }

& dotnet @arguments
exit $LASTEXITCODE