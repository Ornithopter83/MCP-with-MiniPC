[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

function Find-ProjectHubRepositoryRoot {
    $directory = [IO.DirectoryInfo]$PSScriptRoot
    while ($null -ne $directory) {
        if ((Test-Path -LiteralPath (Join-Path $directory.FullName '.git')) -and
            (Test-Path -LiteralPath (Join-Path $directory.FullName 'src\ProjectHub.Worker\ProjectHub.Worker.csproj') -PathType Leaf)) {
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

    throw "ProjectHub 저장소 루트를 찾을 수 없습니다. 저장소 루트 또는 Worker 배포 폴더에서 실행하세요."
}

$repositoryRoot = Find-ProjectHubRepositoryRoot
$projectFile = Join-Path $repositoryRoot 'src\ProjectHub.Worker\ProjectHub.Worker.csproj'
$publishDirectory = Join-Path $repositoryRoot 'bin'
$publishedExecutable = Join-Path $publishDirectory 'ProjectHub.Worker.exe'
$deploymentDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Worker'))

Write-Host "Repository : $repositoryRoot"
Write-Host "Project    : $projectFile"
Write-Host "Publish    : $publishDirectory"

# The previous EXE stored user configuration below its own directory.
# Copy these settings before deleting bin/, otherwise an old bin-only
# configuration would be lost before the new EXE can perform migration.
$canonicalConfig = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ProjectHub\Worker\config'
$legacyConfigs = @(
    (Join-Path $publishDirectory 'Worker\config'),
    (Join-Path $deploymentDirectory 'Worker\config')
)
foreach ($fileName in @('target-settings.json', 'worker-selection.json', 'window-placement.json')) {
    $destination = Join-Path $canonicalConfig $fileName
    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        continue
    }

    $candidates = @($legacyConfigs |
        ForEach-Object { Join-Path $_ $fileName } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Sort-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc } -Descending)
    if ($candidates.Count -eq 0) {
        continue
    }

    $imported = $false
    foreach ($candidate in $candidates) {
        try {
            $parsed = Get-Content -LiteralPath $candidate -Raw -Encoding UTF8 |
                ConvertFrom-Json -ErrorAction Stop
            if ($null -eq $parsed -or $parsed -isnot [pscustomobject]) {
                continue
            }
        }
        catch {
            Write-Warning "Skipping malformed legacy Worker configuration: $candidate"
            continue
        }

        New-Item -ItemType Directory -Path $canonicalConfig -Force | Out-Null
        $temporary = "$destination.$([guid]::NewGuid().ToString('N')).tmp"
        try {
            [System.IO.File]::Copy($candidate, $temporary)
            if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
                [System.IO.File]::Move($temporary, $destination)
                Write-Host "Imported legacy setting: $fileName"
            }
            $imported = $true
            break
        }
        finally {
            if (Test-Path -LiteralPath $temporary) {
                Remove-Item -LiteralPath $temporary -Force
            }
        }
    }

    if (-not $imported) {
        throw "Legacy Worker settings could not be migrated: $fileName. bin/ was not removed."
    }
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$arguments = @(
    'publish',
    $projectFile,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $publishDirectory,
    '-p:PublishSingleFile=true'
)
if ($NoRestore) {
    $arguments += '--no-restore'
}

Write-Host ('dotnet ' + ($arguments -join ' '))
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    $publishedFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File -Force -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Name)
    throw "Worker executable was not created: $publishedExecutable`nPublished files: $($publishedFiles -join ', ')"
}

$publishedInfo = Get-Item -LiteralPath $publishedExecutable
Write-Host "Created    : $($publishedInfo.FullName)"
Write-Host "Size       : $($publishedInfo.Length) bytes"

New-Item -ItemType Directory -Path $deploymentDirectory -Force | Out-Null
Copy-Item -LiteralPath $publishedExecutable -Destination (Join-Path $deploymentDirectory 'ProjectHub.Worker.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish-worker.cmd') -Destination (Join-Path $deploymentDirectory 'publish-worker.cmd') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'publish-worker.ps1') -Destination (Join-Path $deploymentDirectory 'publish-worker.ps1') -Force

Write-Host "Deployment : $deploymentDirectory"
Write-Host "Worker publish completed."
exit 0
