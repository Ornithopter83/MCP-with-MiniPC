$ErrorActionPreference = "Stop"

$ProjectPath = "C:\AI-Server\ProjectHub"
$SolutionFile = "ProjectHub.sln"
$ServerProject = ".\src\ProjectHub.Server\ProjectHub.Server.csproj"
$PrivateKeyPath = Join-Path $ProjectPath "src\ProjectHub.Server\projecthub-private.pem"

function Write-Step($text) {
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host $text -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
}

try {
    Write-Step "0. 기존 ProjectHub.Server 종료"

    $targets = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
        Where-Object {
            $_.CommandLine -and (
                $_.CommandLine -like "*ProjectHub.Server*" -or
                $_.CommandLine -like "*ProjectHub.Server.csproj*"
            )
        }

    if ($targets) {
        foreach ($p in $targets) {
            Write-Host "Stopping PID $($p.ProcessId): $($p.CommandLine)"
            Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 1
    }
    else {
        Write-Host "실행 중인 ProjectHub.Server가 없습니다."
    }

    Write-Step "1. 프로젝트 폴더 확인"
    if (-not (Test-Path $ProjectPath)) {
        throw "프로젝트 경로가 없습니다: $ProjectPath"
    }

    Set-Location $ProjectPath
    Write-Host "Working Directory: $(Get-Location)"

    Write-Step "2. Server assertion private key 환경 변수 설정"
    if (-not (Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf)) {
        throw "private key 파일이 없습니다: $PrivateKeyPath"
    }

    # PEM 개행을 보존하고 키 내용은 출력하지 않는다.
    $pem = [IO.File]::ReadAllText($PrivateKeyPath)
    if ([string]::IsNullOrWhiteSpace($pem)) {
        throw "private key 파일이 비어 있습니다: $PrivateKeyPath"
    }
    $env:PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM = $pem
    Write-Host "PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM 설정 완료 (내용은 출력하지 않음)"

    Write-Step "3. Git 상태 확인"
    git status --short
    if ($LASTEXITCODE -ne 0) {
        throw "git status 실패"
    }

    Write-Step "4. GitHub 최신본 받기 (git pull --ff-only)"
    git pull --ff-only
    if ($LASTEXITCODE -ne 0) {
        throw "git pull 실패. 로컬 변경 또는 브랜치 충돌 여부를 확인하세요."
    }

    Write-Step "5. NuGet restore"
    dotnet restore $SolutionFile
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore 실패"
    }

    Write-Step "6. 전체 빌드"
    dotnet build $SolutionFile --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build 실패"
    }

    Write-Step "7. ProjectHub.Server 실행"
    Write-Host "서버를 종료하려면 Ctrl+C 를 누르세요." -ForegroundColor Yellow
    Write-Host ""

    dotnet run --project $ServerProject --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "ProjectHub.Server 실행 실패"
    }
}
catch {
    Write-Host ""
    Write-Host "실패: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Read-Host "Enter를 누르면 창이 닫힙니다"
    exit 1
}
