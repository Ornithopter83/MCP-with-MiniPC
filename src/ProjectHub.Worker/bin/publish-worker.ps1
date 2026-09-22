[CmdletBinding()]
param(
    [switch]$NoRestore
)

function Find-ProjectHubRepositoryRoot {
    $directory = [IO.DirectoryInfo]$PSScriptRoot
    while ($null -ne $directory) {
        if (Test-Path -LiteralPath (Join-Path $directory.FullName '.git')) {
            return $directory.FullName
        }

        $repositoryCandidates = @(Get-ChildItem -LiteralPath $directory.FullName -Directory -Force -ErrorAction SilentlyContinue | Where-Object {
            (Test-Path -LiteralPath (Join-Path $_.FullName '.git')) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'src\ProjectHub.Worker\ProjectHub.Worker.csproj') -PathType Leaf)
        })
        if ($repositoryCandidates.Count -eq 1) {
            return $repositoryCandidates[0].FullName
        }

        $directory = $directory.Parent
    }

    throw "ProjectHub 저장소 루트를 찾을 수 없습니다. 저장소 루트와 Worker 배포 폴더의 위치를 확인하세요."
}

$repositoryRoot = Find-ProjectHubRepositoryRoot
$projectDirectory = Join-Path $repositoryRoot 'src\ProjectHub.Worker'
$projectFile = Join-Path $projectDirectory 'ProjectHub.Worker.csproj'
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
