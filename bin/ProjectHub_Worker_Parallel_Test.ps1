param(
    [string]$ProjectRoot = "",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

$Solution = Join-Path $ProjectRoot "ProjectHub.sln"
$WorkerTests = Join-Path $ProjectRoot "tests\ProjectHub.Worker.Tests\ProjectHub.Worker.Tests.csproj"

function Invoke-Checked {
    param(
        [string]$Label,
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host $Label -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan

    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Label 실패 (exit=$LASTEXITCODE)"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK를 찾을 수 없습니다. dotnet이 PATH에 있는 Windows 개발 환경에서 실행하세요."
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git을 찾을 수 없습니다."
}
if (-not (Test-Path -LiteralPath $Solution -PathType Leaf)) {
    throw "ProjectHub.sln을 찾을 수 없습니다: $Solution"
}
if (-not (Test-Path -LiteralPath $WorkerTests -PathType Leaf)) {
    throw "Worker 테스트 프로젝트를 찾을 수 없습니다: $WorkerTests"
}

Set-Location -LiteralPath $ProjectRoot

$restoreArgs = @()
if ($NoRestore) {
    $restoreArgs += "--no-restore"
}

Invoke-Checked "1. 병렬 WORK 핵심 Worker 테스트" {
    dotnet test $WorkerTests -c Debug @restoreArgs --filter "FullyQualifiedName~WorkGraphTests|FullyQualifiedName~ParallelWorkSchedulerTests|FullyQualifiedName~GitWorktreeManagerTests|FullyQualifiedName~ParallelWorkTransportTests|FullyQualifiedName~CodexWorkItemExecutorTests|FullyQualifiedName~ParallelWorkSupervisorTests|FullyQualifiedName~ParallelResourceWorkItemRouterTests|FullyQualifiedName~ParallelJudgeWorkItemRouterTests|FullyQualifiedName~ParallelWorkSidecarTests|FullyQualifiedName~WorkGraphPersistenceTests|FullyQualifiedName~ParallelWorkUiFormatterTests"
}

Invoke-Checked "2. 전체 solution 테스트" {
    dotnet test $Solution -c Debug @restoreArgs
}

Invoke-Checked "3. Debug 빌드" {
    dotnet build $Solution -c Debug @restoreArgs
}

Invoke-Checked "4. Release 빌드" {
    dotnet build $Solution -c Release @restoreArgs
}

Invoke-Checked "5. Git whitespace 검증" {
    git diff --check
}

Write-Host ""
Write-Host "자동 검증 통과." -ForegroundColor Green
Write-Host ""
Write-Host "남은 Explorer E2E:" -ForegroundColor Yellow
Write-Host "  1) 최대 동시 WORK=1 직렬 회귀"
Write-Host "  2) 최대 동시 WORK=4에서 독립 WorkItem 4개 동시 실행"
Write-Host "  3) 5번째 READY WorkItem이 슬롯 해제 직후 시작"
Write-Host "  4) SPLIT_REQUEST -> HQ GraphPatch -> 동적 WorkItem 추가"
Write-Host "  5) Integration WorkItem -> 주 작업공간 fast-forward landing"
Write-Host "  6) RESOURCE / JUDGE / OBSERVATION 결과가 요청 WorkItem으로 복귀"
Write-Host "  7) 실행 취소 -> 작업 추가 -> 저장 WorkGraph 복구"
Write-Host "  8) 열린 WorkItem이 있을 때 HQ END 거부와 최종 outstanding 종료 확인"
