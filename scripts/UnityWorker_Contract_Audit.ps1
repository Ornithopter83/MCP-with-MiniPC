# UnityWorker contract/document audit (read-only).
# powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\UnityWorker_Contract_Audit.ps1
# Optional: -FailOnReview or -Json
[CmdletBinding()]
param([string]$RepositoryRoot, [switch]$FailOnReview, [switch]$Json)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}
$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$script:Findings = New-Object 'System.Collections.Generic.List[object]'

function Add-Finding {
    param([string]$Severity, [string]$Rule, [string]$Path, [int]$Line,
          [string]$Explanation, [string]$Evidence = '')
    $script:Findings.Add([pscustomobject]@{
        Severity = $Severity
        Rule = $Rule
        Location = $Path + ':' + $Line
        Explanation = $Explanation
        Evidence = $Evidence.Trim()
    })
}

function Read-Text {
    param([string]$RelativePath)
    $p = Join-Path $RepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return '' }
    return [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
}

function Scan-Lines {
    param([string]$Path, [string]$Pattern, [string]$Severity,
          [string]$Rule, [string]$Explanation, [int]$MaxHits = 5)
    $p = Join-Path $RepositoryRoot $Path
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return }
    $lines = [System.IO.File]::ReadAllLines($p, [System.Text.Encoding]::UTF8)
    $count = 0
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if (-not [regex]::IsMatch($lines[$i], $Pattern)) { continue }
        $evidence = $lines[$i].Trim()
        if ($evidence.Length -gt 180) {
            $evidence = $evidence.Substring(0, 180) + '...'
        }
        Add-Finding $Severity $Rule $Path ($i + 1) $Explanation $evidence
        $count++
        if ($count -ge $MaxHits) { break }
    }
}

$folder = 'src/ProjectHub.Worker/Contracts'
foreach ($role in @('HQ', 'WORK', 'HIGH', 'DESIGN', 'ASSET-MANAGER')) {
    $path = $folder + '/' + $role + '-ROUTING-CONTRACT.md'
    $text = Read-Text $path
    if ([string]::IsNullOrWhiteSpace($text)) {
        Add-Finding 'ERROR' 'MISSING_ACTIVE_CONTRACT' $path 0 '활성 역할 계약 파일이 없습니다.'
        continue
    }
    if (-not [regex]::IsMatch($text, '(?m)^\s*\[GOTO\s*=\s*[A-Z]+\]\s*$')) {
        Add-Finding 'ERROR' 'GOTO_EXAMPLE_MISSING' $path 0 '독립된 GOTO 출력 예시가 없습니다.'
    }
    if (-not [regex]::IsMatch($text, '(?m)^\s*\[RESPONSE\s*=\s*OK\]\s*$')) {
        Add-Finding 'ERROR' 'RESPONSE_EXAMPLE_MISSING' $path 0 '독립된 RESPONSE=OK 종료 예시가 없습니다.'
    }
    if ($role -ne 'HQ') {
        if (-not [regex]::IsMatch($text, '(?m)^\s*@@REPORT\s*$')) {
            Add-Finding 'ERROR' 'REPORT_EXAMPLE_MISSING' $path 0 '독립된 역할 @@REPORT 예시가 없습니다.'
        }
        Scan-Lines $path '^\s*\[ACTION\s*[:=]' 'ERROR' 'ROLE_OUTPUT_ACTION' 'ACTION은 HQ 응답에서만 사용합니다.'
    }
    Scan-Lines $path '^\s*\[(?:KEY|GOTO|ACTION|RESPONSE)[^\]]*\]\s+\[(?:KEY|GOTO|ACTION|RESPONSE)' 'ERROR' 'JOINED_CONTROL_MARKERS' '출력 예시에 둘 이상의 제어 표식이 한 줄에 있습니다.'
    Scan-Lines $path '^\s*@@(?:QA|RESOURCE|WORKITEM|GRAPHICS|TEST|VALIDATE)\b' 'ERROR' 'UNSUPPORTED_MAJOR_TAG' '현행 역할 계약에 없는 대분류입니다.'
    Scan-Lines $path '^\s*\[(?:ROUTE|TARGET|HANDOFF|NEXT|RESULT|STATUS)\s*[:=]' 'ERROR' 'UNSUPPORTED_BRACKET_TAG' '현행 4종 대괄호 제어 표식 외의 형식입니다.'
}

foreach ($doc in @('README.md', 'UnityWorker-ROADMAP.md')) {
    Scan-Lines $doc '(?i)(예약된 QA|QA \+ Unity CLI|Unity QA|QA/HIGH|WORK/QA/HIGH|QA: EditMode|검증.*QA)' 'REVIEW' 'LEGACY_QA_REFERENCE' 'QA 독립 역할 삭제와 일치하는지 검토하세요.'
    Scan-Lines $doc '(?i)(RESOURCE #0|RESOURCE는 이미지 생성|HQ와 RESOURCE|#0 IMAGE|RESOURCE 이미지)' 'REVIEW' 'LEGACY_RESOURCE_REFERENCE' '현재 DESIGN/ASSET MANAGER 및 Web 설정과 비교하세요.'
    Scan-Lines $doc '(?i)WORK\s*#?8(?![0-9])' 'REVIEW' 'OLD_BUILD_REPAIR_ID' '빌드 복구의 WORKITEM ID 정책과 비교하세요.'
    Scan-Lines $doc '(?i)(HQ 전달 예약|HQ 메시지 예약|HQ 예약)' 'REVIEW' 'OLD_MESSAGE_ROUTING' '메시지 수신자는 고정 HQ가 아닌 다음 AI 역할입니다.'
}

$qaPath = $folder + '/QA-ROUTING-CONTRACT.md'
if (-not [string]::IsNullOrWhiteSpace((Read-Text $qaPath))) {
    Add-Finding 'REVIEW' 'LEGACY_QA_CONTRACT' $qaPath 1 '구형 QA 계약이 호환용인지 활성 호출 경로가 있는지 확인하세요.'
}

$pauseSource = 'src/ProjectHub.Worker/UnityUserPausePolicy.cs'
$flowSource = 'src/ProjectHub.Worker/MainWindow.MilestoneFlow.cs'
if ((Read-Text $pauseSource) -match '\bIsPauseRequest\s*\(' -and
    (Read-Text $flowSource) -notmatch '\bUnityUserPausePolicy\.IsPauseRequest\s*\(') {
    Scan-Lines $pauseSource '\bIsPauseRequest\s*\(' 'REVIEW' 'STALE_PAUSE_TEXT_PARSER' 'Worker 자연어 중단 판정의 구형 메서드가 미사용으로 남았습니다.' 1
}

$workContract = Read-Text ($folder + '/WORK-ROUTING-CONTRACT.md')
$roleRunner = Read-Text 'src/ProjectHub.Worker/MainWindow.xaml.cs'
if ($workContract -match '(?i)FULL_AUTONOMY.*(?:Git|빌드)' -and
    $roleRunner -match 'BuildExecutionAllowed:\s*roleName\s*!=\s*"WORK"') {
    Add-Finding 'REVIEW' 'WORK_BUILD_AUTHORITY' ($folder + '/WORK-ROUTING-CONTRACT.md') 0 'WORK의 FULL_AUTONOMY 문서와 실제 CLI 실행기 권한을 대조하세요.'
}

$errors = @($script:Findings | Where-Object { $_.Severity -eq 'ERROR' }).Count
$reviews = @($script:Findings | Where-Object { $_.Severity -eq 'REVIEW' }).Count
if ($Json) {
    [pscustomobject]@{
        Root = $RepositoryRoot
        Errors = $errors
        Reviews = $reviews
        Findings = @($script:Findings)
    } | ConvertTo-Json -Depth 7
} else {
    Write-Output ('UnityWorker contract audit (READ-ONLY): ' + $RepositoryRoot)
    foreach ($f in $script:Findings) {
        Write-Output ('[' + $f.Severity + '][' + $f.Rule + '] ' + $f.Location + ' - ' + $f.Explanation)
        if ($f.Evidence) { Write-Output ('    ' + $f.Evidence) }
    }
    Write-Output ('Summary: ERROR=' + $errors + ' REVIEW=' + $reviews)
    Write-Output 'REVIEW는 확인 후보입니다. 프로젝트 파일은 변경되지 않았습니다.'
}
if ($errors -gt 0) { exit 1 }
if ($FailOnReview -and $reviews -gt 0) { exit 2 }
exit 0
