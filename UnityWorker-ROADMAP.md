# ProjectHub UnityWorker — 개발 상태 및 단계별 기준


- 기준: 범용 ProjectHub `v1.0.0` / `aea8279917801b1bff0c1f45d4e956389a98d625`
- 개발 브랜치: `UnityWorker` (범용 `main`과 별도)
- `Master-Polish.md`는 읽기 전용으로 유지한다.
- UnityWorker 브랜치는 **Worker 제품 소스**의 브랜치이며 사용자의 게임 저장소 Git 브랜치와 다르다.

## 책임 분리

| 역할 | 현재 실행 주체 | 표시 카드 | 목표 산출물 |
|---|---|---|---|
| 프로젝트 조정 | HQ | Unity HQ | 요구·완료 기준 및 WORKITEM 설계 |
| #1 에셋 전문 역할 | ASSET MANAGER (첫 구현) | #1 Asset Manager | 검색→획득/Import→씬 적용, 단계별 체크포인트·차단 정책 |
| 도구·플러그인 점검 | Worker Unity 사전 점검 (부분 구현) | 실행 이력 | 프로젝트/Pipeline 및 CLI 응답; MCP 인증·세션은 미검증 |
| Editor 연결·독점 | Worker 연결 Probe + 프로젝트별 공통 게이트 | 실행 이력 | 명령 목록 확인; Editor WORK, CLI BUILD/Import/Probe, ASSET MANAGER INTEGRATE는 같은 프로젝트 게이트 공유 |
| 게임플레이 구현 | WORK #10+ | Unity WORK | C# 코드, 컴포넌트 구현 |
| 씬 구성 | DESIGN #0 / WORK #10+ / ASSET MANAGER INTEGRATE | Unity DESIGN/WORK | 공통 Editor 게이트 아래 Scene 열기·수정·저장, SaveScene 결과 검증 |
| 기계 BUILD/TEST | Worker + Unity CLI | 실행 이력 | 컴파일·테스트 증거를 HIGH에 전달, 미검증 분리 |
| 통합·릴리스 | HIGH + Worker Git | Unity HIGH | 통합 검토 및 Commit/Push |
| 시각 제작 | DESIGN #0 | Unity DESIGN | HQ가 배정한 UI·씬·그래픽 제작 |

## 1차 구현 당시 이력 (현행 활성 역할 정의가 아님)

- WORKITEM ID, 세션, 체크포인트, HQ/WORK/DESIGN/HIGH 결과 문법 유지
- Unity 역할별 작업·검증 계약과 Worker-Polish 확장
- 화면 파이프라인/모델 설정/이력 카드 이름을 Unity용으로 수정
- 초기에는 Unity WORK 전체를 직렬화했으나, 후속 개선에서 독립 C# 소스 WORK는 사용자 설정 상한까지 병렬 실행하고 씬·프리팹·AssetDatabase/Editor 작업은 별도 독점 레인으로 제한
- UnityWorker 브랜치 대상으로 .NET CI 실행 및 직렬 정책 회귀 테스트 추가
- Unity 프로젝트로 인식되면 Worker BUILD 단계에서 Unity CLI `recompile`을 호출하고, 컴파일 실패(exit 6)와 연결·환경 실패를 구분 (Windows 실증 전)
- HQ 실행 전에 프로젝트 구조·Pipeline 의존성과 `unity command --project-path ... --format json` 읽기 전용 Probe를 확인했다. 현재는 Editor 연결 장애를 HQ에 전달하며 코드 전용 WORK까지 일괄 차단하지 않는다. Editor 의존 동작은 실제 명령 응답으로 개별 판정한다.

- #1 ASSET MANAGER 역할 계약·HQ 우선 계획 문법·직렬 3단계 실행·독립 세션·WORK 선행 실행·카드 표시 및 체크포인트 반영 (Windows Editor 실증 전)

## 활성 실행 정책 (작업별 실제 검증 결과와 구분)

- 해상도 요구가 사용자 지시나 해당 프로젝트 명세에 명시되지 않은 UnityWorker 신규 제작 프로그램의 기본 해상도는 **1920×1080(16:9)**이다. 명시된 해상도 요구가 있으면 그 요구를 우선한다. 이 제작 기본값을 Game View 실제 렌더 크기, 캡처 파일 크기 또는 모든 창 크기의 강제 검증 조건으로 오인하지 않는다.

- HQ(웹/CLI)는 설계·재판단, #1 ASSET MANAGER는 검색·획득·적용, #0 DESIGN은 시각 제작·수정, #10+ WORK는 구현, HIGH는 최종 검증·보완을 담당한다.
- 이미지 추출용 RESOURCE sidecar를 사용하지 않는다. 역할별 실행 문법은 전용 계약과 파서에서 확인한다.
- HQ는 직접 지정한 WORK #10+만 계획·추적한다. DESIGN·HIGH·ASSET MANAGER의 임시 WORK 위임은 그 부모 세션 내부에서만 이루어지며 HQ WORKITEM 번호나 마일스톤으로 유출하지 않는다.
- DESIGN 산출물에 의존하는 WORK만 `<DEPENDS_ON_DESIGN>true</>`로 표시한다(기존 `DESIGN 종료 후` 지시도 동일 취급). DESIGN 미완료 시 해당 WORK를 실제 실행하지 않고 BLOCKED로 기록한다. 독립 WORK는 계속한다.
- WORK에는 할당된 과업 수행에 필요한 셸·Unity CLI/MCP·Editor·빌드·테스트 실행 권한을 부여하되 작업·파일 경로는 좁고 명시적으로 유지한다. 임시 WORK는 자체 MCP 명령만 사용하며 재위임할 수 없다. 절대 경로·프로젝트 루트 전체를 위임 범위로 지정하지 않는다.
- 독립 C# 소스 WORK는 병렬이고 Editor·Scene·Import는 프로젝트 공통 게이트로 조정한다. 직접 Editor WORK가 이미 보유한 게이트는 같은 WORK의 중첩 MCP 명령에만 재사용한다. HIGH는 이전 유효 증거를 재사용하며 독립 판단한다. 추가 DESIGN은 HQ가 새로운 마일스톤에서 배정한다.
- 사용자 추가 원문 메시지는 다음 제어권의 AI에 전달하며 Worker는 자연어 중단 판정을 하지 않는다.
- 새 HQ CLI 작업은 새 세션에서 시작하고 `.projecthub/last-handoff.md`를 참고한다. 중단 재개는 기존 세션과 `.projecthub/session-state.json`을 쓴다.
- Unity Editor와 이 프로젝트에서 실행한 Windows Player의 포커스 보호는 최선의 복구이며 운영체제 수준의 보장이 아니다.

## 추가 검증 대상 (완료 여부는 최신 실행 로그 기준)

1. Unity Toolchain 실증·확장: 현재 Probe를 실제 Windows Unity CLI에서 검증하고 Codex WORK 샌드박스의 MCP 등록·호출/ACL까지 확인 (HQ 시작 전 Probe가 MCP 사용 가능성을 보장하지는 않음)
2. Editor 게이트·Scene 흐름 실증: 구현된 프로젝트 공통 게이트/Scene Open·Save를 실제 Windows Unity Editor에서 검증하고, 외부 셸 CLI/MCP 호출을 강제 격리할지 결정
3. 기계 BUILD 실증·보강: Unity CLI `recompile`의 실제 Windows 응답/로그 검증, Editor Console 및 컴파일 상태 세부 파싱
4. HIGH: EditMode/PlayMode·프레임·입력·카메라 증거를 개별 검증하고 미검증 범위를 분리
5. Unity Asset/Git: .meta, 프리팹 및 Packages/ProjectSettings 승인 범위, 캐시/생성물 제외
6. Windows 실증과 회귀 검증을 통과한 후 UnityWorker 베타 프리릴리스 발행

## 병렬 WORK 재개 정책

- `WRITE_PATH`가 서로 겹치는 WORK는 기존처럼 실행 대상에서 제외하고 HQ에 BLOCKED 근거를 보낸다.
- 일반 C# 소스 파일(`Assets/**/*.cs`, `Editor` 경로 제외)만 소스 레인으로 처리한다. 그 외 파일·폴더 또는 Editor 관련 실행 지시가 있으면 Editor 독점 레인으로 분류한다.
- Editor 대기 작업은 동시 WORK 슬롯을 선점하지 않으며, WORK별 체크포인트와 세션을 따로 기록한다.
- `in_progress` 상태여도 완료로 둔갑시키지 않고 HIGH/HQ에 전달한다.
- `CONTINUE`는 앞선 체크포인트의 보고서를 전달하며 이미 수행한 작업을 반복하지 않도록 지시한다. `rg` 등이 없다면 기존 Git/PowerShell 검색으로 대체한다.
- 프로젝트별 `UnityEditorOperationGate`를 Editor WORK 전체, CLI BUILD/Import/Probe와 ASSET MANAGER INTEGRATE에서 공유한다. Editor 대기 중에도 독립 소스 WORK 슬롯은 유지한다.
- `UnitySceneCliWorkflow`는 연결된 Editor의 `eval`로 기존 Scene을 Additive 방식으로 열고 활성화하며, 작업 후 `EditorSceneManager.SaveScene` 반환값을 확인한다. 이미 열린 Scene은 다시 열지 않으며 Batch Mode/수동 YAML 편집을 기본 경로로 사용하지 않는다.
- AI는 실제 Editor 명령 카탈로그에서 `open_scene`, `create_gameobject`, `set_transform`, `add_component`, `save_scene` 등의 지원 여부/인자를 조회한 후 사용한다. 명령 이름과 스키마는 설치된 Pipeline 버전별로 확인한다.
- 공통 게이트는 **Worker 프로세스가 관리하는 호출 및 Editor WORK의 협력적 독점**이다. 외부 프로그램·독립 셸에서 직접 호출하는 CLI/MCP 명령을 OS 수준에서 가로채지는 못한다. Windows Unity Editor 실환경에서 Additive 씬 열기/저장과 FULL_AUTONOMY 동시성 검증은 아직 필요하다.
- Asset Import CLI 시간초과는 성공 여부 미확인으로 보고하고 무조건 재시도하지 않는다. HQ가 기존 결과 검증과 대체 경로를 결정한다.

## FULL_AUTONOMY 프로젝트 변경 허용 정책 (2026-10-13)

- Unity 프로젝트의 `Assets`, `ProjectSettings`, `Packages`, Scene, Prefab, .meta, Editor/C# 코드, 소재 및 구성 파일의 필요한 변경을 모두 허용한다.
- WORK의 `WRITE_PATH`, ASSET MANAGER의 `TARGET_PATH`는 작업 범위 추적·병렬 충돌 조정용이다. 선언 범위 밖 파일 변경은 작업 중단이나 Git 제외 근거가 아니다.
- `ProjectSettings/URPProjectSettings.asset` 같은 Unity 자동 갱신은 `ADDITIONAL_PROJECT_CHANGES`로 기록하고 계속 진행한다.
- 기존 미커밋 파일은 중복 충돌이 아니다. 서로 다른 WORK가 동일 파일에 동시 쓰기를 수행할 때만 충돌 대상으로 조정한다.
- ASSET MANAGER의 실제 실패는 HQ에 전달하면서 독립 WORK·HIGH 검토를 수행한다. 완료·미완료 상태는 각각 명확히 기록한다.
- 새로 발생한 프로젝트 변경은 선언 `WRITE_PATH`와 무관하게 Git 최종화 후보에 포함하며, 기존 사용자 미커밋 변경은 별도로 보존한다.
- 프로젝트 루트 밖 경로, 파서·신원 무결성, 파일 해시·실제 테스트 성공 검증은 변경 범위에 대한 별도 허가 요청과 구분한다.

## 안전 기준

- `BUILD_SKIPPED`는 Unity 컴파일 통과를 뜻하지 않는다. Editor 또는 MCP와의 연결이 없으면 실제 플레이테스트 성공을 보고하지 않는다.
- 연결·인증 장애는 환경 문제로 즉시 보고하며 구현 WORK를 무의미하게 반복하지 않는다.
- Editor 기능은 공식 CLI/Pipeline을 우선 재사용하되 명령 탐색 장애 시 기존 Editor 내부 공식 API를 사용하는 **옵션형 로컬 파일 큐 브리지**를 사용할 수 있다. 별도 포트·GUI Editor 프로세스·권한 상승은 만들지 않는다.
- UnityWorker 제품 개발과 실제 Unity 게임 프로젝트의 Git 브랜치·최종화는 별개의 계약이다.

### #1 ASSET MANAGER 이전 NEW 작업 Git 경계 (UnityWorker)

- NEW Unity 작업은 HQ/Git 사전 점검보다 먼저 프로젝트 Editor를 종료·재실행 또는 최초 실행하고 GUI 동작을 확인한다. 실패하면 HQ에 전달하지 않는다.
- 브랜치 전환을 방해하는 충돌 경로만 강제로 삭제·교체한다. 백업·stash 생성 없음, 무관한 미스테이징/스테이징 파일 보존.
- Unity 캐시/IDE 출력용 ignore 규칙을 사전에 적용하며 Git 메타데이터 및 ProjectHub 지속 상태는 보호한다.
- Git 사전 점검 실패는 HIGH의 소스 수정 없이 즉시 중단한다. 실제 Unity 씬 실행은 별도 실기 검증이 필요하다.

### Unity CLI 실행 계층 및 증거

- `UnityCliProcessRunner`는 사전 연결 점검·Editor 명령 탐색·씬 명령·재컴파일 등 CLI 전송을 공유한다. 각 호출의 종료 코드, 시작·종료 시각, stdout/stderr(각 200만 문자 한도, 초과 시 명시)를 프로젝트 Git 외부의 로컬 `ProjectHub/UnityWorker/unity-cli-evidence`에 보존한다. 파일 저장 실패는 실행 결과를 바꾸지 않는다.
- 연결 결과(`UNITY_EDITOR_CONNECTED_NO_CATALOG` 가능), 명령 목록 존재, 개별 메서드 반환 성공을 별도 취급한다. `data.commands` 및 Unity CLI가 실제 반환한 배열 형태를 판독하며, `eval`이 목록에 없으면 기존 Import 경로에서 사용하는 `--query eval --detail full` 읽기 전용 조회를 수행한다. 명령 이름이 확증되지 않으면 `editor_eval`을 호출하지 않는다.
- WORK의 MCP 실행 권한은 위임 범위 내에서 제공한다. 현재 FULL_AUTONOMY와 Git 스냅샷 감사는 OS 수준의 사전 파일 접근 차단이 아니라 실행 후 범위 위반 탐지다. 병렬 작업의 변경 귀속은 불확실할 수 있어 결과를 보안 보장으로 해석하지 않는다.
- HIGH는 CLI 연결 확인, 명령 지원 확인, 실제 컴파일·Scene·Player BuildReport를 서로 다른 증거로 판정한다. 결과 원본이 없으면 과거 로그 재수집을 반복하지 않고 미검증 사유만 남긴다.

### 명령 접수 기반 Unity Editor 연결 정책

- '＋ 새 작업' 화면 전환은 Editor 실행 및 연결 점검과 무관하다.
- 실제 명령 제출(`▶ 실행`) 및 작업 추가(`＋ 작업 추가`)에서 기존 Unity CLI/Editor 연결 상태만 점검한다.
- NEW 작업은 Git 준비 후 연결을 확인한다. 연결이 없더라도 GUI Editor를 자동 실행하지 않으며 HQ가 코드 전용 작업 또는 중단을 판단한다.
- 프로젝트 잠금·Pipeline 오류·Safe Mode는 새 Editor 실행으로 우회하지 않는다. 작업 중 연결 감시는 읽기 전용으로 운영하며 자동 재시작·재연결·사용자 포커스 전환을 수행하지 않는다.
- 역할 AI가 실제 명령을 선택하고, Worker의 `worker_command`는 실시간 카탈로그에 등록된 `eval`을 `editor_eval`로 전달한다. `expected_result`와 실제 Editor 반환값이 일치해야 명령 성공으로 인정하며, 재컴파일 또는 메서드 반환 성공만으로 Player BuildReport·산출물 검증 완료를 주장하지 않는다.
- 실제 Windows Editor의 명령 전달 및 Player 빌드 결과는 별도 실행 증거가 필요하다.

### ASSET MANAGER DISCOVER 오차단 방지

- Worker가 `Assets/`의 모델·Prefab 후보 파일을 읽기 전용으로 조사하고 `LOCAL_ASSET_INVENTORY`를 DISCOVER에 주입한다. 디스크 파일 후보는 Import 완료의 증거가 아니다.
- 외부 `rg` 설치와 Unity AssetDatabase/Search 호출은 DISCOVER의 필수 조건이 아니다. `Assets/ThirdParty/Forest` 등 대상 폴더가 아직 없어도 정상이다.
- CC0_AUTO는 공식 배포처의 CC0 표시·다운로드 안내·모델 형식으로 후보를 선정할 수 있다. 직접 파일 URL·용량·버전 미공개는 ACQUIRE에서 확인하고 검증하지 않은 다운로드·Import를 성공이라 보고하지 않는다.
- 실제 Unity Import/Importer/.meta/GUID와 씬 배치/저장 검증은 ACQUIRE/INTEGRATE에서 여전히 필수다. 동일 프로젝트의 추가 Unity Editor 배치 실행을 피한다.

### 단일 EXE 위치 독립형 설정 및 앱 데이터

- 전역 Worker 설정·실행 임시 폴더·로그의 기준은 EXE 경로가 아닌 `%LOCALAPPDATA%\ProjectHub\Worker`다. 설정은 `config/target-settings.json`, `worker-selection.json`, `window-placement.json`으로 유지한다.
- 새 빌드나 배포판을 어느 폴더에서 실행하든 같은 Windows 사용자 계정에서는 동일 설정을 참조한다. 다른 사용자·PC에는 자동 공유되지 않는다.
- 첫 실행에서 구형 `<EXE폴더>\Worker\config`와 지원되는 repo/bin·인접 배포 경로를 조사해 누락된 설정만 이전한다. 이미 존재하는 중앙 설정은 절대 덮어쓰지 않으며 기존 파일도 삭제하지 않는다.
- 배포 스크립트는 기존 bin을 비우기 전에 구형 bin/deployment 설정을 중앙 설정 폴더로 복사한다. 파일을 사용할 수 없으면 빌드 과정에서 잃지 않도록 중단한다.
- 프로젝트별 체크포인트·이벤트는 계속 `<프로젝트 루트>\.projecthub`를, 프로젝트별 임시 산출물은 `<프로젝트 루트>\temp`를 사용한다.
- 기존 EXE가 완전히 무관한 폴더에 보관돼 자동 검색할 수 없는 경우, 중앙 설정 폴더로 사용자가 기존 JSON 파일을 복사해야 한다.


### 기존 Editor 직접 실행 브리지

- 최초 `worker_command.editor_bridge_install`은 `Assets/Editor/ProjectHub/ProjectHubEditorBridge.cs`에 내장 스크립트를 설치한다. 이미 다른 내용이면 덮어쓰지 않는다. 기존 Editor가 자동 Import한 뒤 `editor_bridge_ping`에서 실제 메인 스레드 응답을 받아야 한다.
- `editor_bridge_invoke`는 public static 메서드 반환값을 정확히 검증한다. `editor_bridge_menu`는 메뉴가 실제 호출됐는지(반환 True)만 확인하고 BuildReport Succeeded·산출물 확인과 분리한다. `expected_result=Succeeded`를 메뉴에 지정해도 빌드 성공 증거로 간주하지 않는다.
- 요청·응답은 같은 Windows 사용자 계정의 `%LOCALAPPDATA%`에 저장되며 관리자 권한 승격 및 별도 네트워크 서버는 없다. WORK의 최초 설치는 브리지 소스·.meta가 포함된 좁은 경로가 필요하다.
- 실환경 Unity Editor 컴파일·Player BuildReport 검증이 별도로 필요하며, CI가 성공했다고 Editor의 명령 실행까지 검증된 것으로 간주하지 않는다.

### 명시적 캡처·현장 Scene 진단·토큰 인계 (2026-10-18)

- DESIGN 종료 후 Worker의 무조건 Native/Offscreen 자동 캡처를 제거했다. DESIGN/HIGH는 실제 PlayMode를 확인한 뒤 직접 `worker_command.native_capture`를 요청한다. PlayMode는 명령을 시작한 역할이 원래 상태로 복원한다.
- `editor_bridge_scene_status`는 실제 Editor의 활성 Scene 경로, 각 로드된 Scene의 루트 수와 이름, PlayMode/컴파일 상태를 읽기 전용으로 반환한다. 원격 저장소의 QuizScene에는 루트가 있어도 M113 런타임 0개 보고만으로 씬 파일을 재생성해서는 안 된다.
- ASSET DISCOVER의 `docs/projecthub/` 증거 문서는 제품 에셋 변경과 별도 보고한다. 위임 WORK는 보고서·증거 파일 경로를 `paths`에 직접 명시하며 검사 범위를 자동 확대하지 않는다.
- 기존 `UsageTelemetryStore` 호출 기록을 역할·모델별로 합산해 다음 부모 역할·HQ 인계에 포함한다. 일회성 임시 WORK 프롬프트에는 누적 사용량 표와 전체 Editor 지침을 매번 재주입하지 않고 과업·경로·핵심 실행 규칙만 보낸다. 웹 모델의 토큰 미제공은 UNKNOWN으로 표기한다.
- Editor CLI·직접 브리지 모두 증거 파일을 Git 외부에 저장한다. 완전히 단일화되지 않은 내부 Import/Scene/Build CLI 호출은 기존 성공 경로를 보존하며, 브리지 대응 API가 없는 작업을 강제로 옮기지 않는다.

### 새 Unity 작업 시작 시 Editor 실행 (2026-10-18)

- HQ/Git 전처리 이전에 Windows 프로세스와 프로젝트 제목으로 해당 Editor 창을 찾는다. 실행 중인 Unity Editor 창이 하나 있고 해당 프로젝트 UnityLockfile이 유지되는 경우에만 정상 종료를 요청한 뒤 종료 확인 후 다시 연다.
- 실행 중인 Editor가 없고 프로젝트 잠금이 없다면 프로젝트 버전 또는 이전에 저장한 Unity.exe 경로로 한 번 실행한다. 다른 프로젝트·여러 창·잠금 불일치에는 강제 종료나 중복 실행을 하지 않는다.
- `Unity.exe -projectPath`로 GUI를 시작하고 새 프로젝트 Editor 창·실행 파일·PID를 확인한다. GUI 확인만으로 HQ에는 전달하지 않는다. Git 사전 준비 후 브리지 설치·Import를 기다리고 메인 스레드 Ping 및 scene_status가 성공하고 Active Scene 로드·컴파일/Import 미진행을 확인한 뒤에만 HQ에 `WORKER_EDITOR_COMMAND_READY`를 보낸다. GUI 대기 120초, 명령 준비 대기 5분. 실패 시 HQ를 호출하지 않는다.
- 마일스톤·PAUSE/CONTINUE에서는 재시작하지 않는다. 단 CONTINUE와 다음 마일스톤의 HQ 전달 전에도 새 브리지 명령 응답을 확인하며 실패 시 BLOCKED로 중단한다. CLI/Pipeline 불능과 브리지 실명령 준비 상태는 분리한다. FocusGuard는 기존처럼 작업 종료 시점의 포커스 복구만 수행한다.
