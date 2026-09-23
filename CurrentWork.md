# ProjectHub 현재 작업 상태

Updated: 2026-09-24

## 재현 후속 — CODEX_HOME와 CLI 세션 경로 불일치 (2026-09-24)

- 07:45:10 실행 transcript `_20260924_074546.txt`에서 SOL PLAN은 `exit 0`으로 성공했지만 REVIEW용 session ID 복구가 실패해 Luna 전에 다시 차단된 것을 확인했다. 같은 시각 rollout `C:\Users\ornit\.codex\sessions\2026\09\24\rollout-2026-09-24T07-45-10-01a0d071-bff5-7bd2-b0c1-bdeab1c5e9e4.jsonl`에는 정상 `session_meta`와 세션 ID가 있다. 설치 EXE는 이전 수정 게시본 A379…와 일치했으므로 실패 원인은 미배포가 아니다.
- 새 원인: Worker는 `CODEX_HOME`이 있으면 해당 세션 저장소만 조사했지만, 실제 codex.exe가 다른 `USERPROFILE\.codex`에 rollout을 기록할 수 있다. 세 환경값의 세션 경로를 우선순위 단일 선택 대신 모두 검색하고, 중복 경로는 제거하도록 수정했다. 세션 ID가 다른 루트에 있어도 기존 CWD/시각/originator/source 조건 및 후보 유일성은 그대로 적용한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0); `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 36개 통과(Worker 31, Core 1, Agent 3, Server 1); `git diff --check` 통과. Release 게시 성공. 첫 수동 복사 때 `bin\Release\...\ProjectHub.Worker.exe`의 framework-dependent 파일을 복사해 DLL 누락 실행 오류를 만들었다. 이를 단일 파일 게시 결과 `src\ProjectHub.Worker\bin\ProjectHub.Worker.exe`로 교체했고 게시본과 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`의 SHA-256은 `843871D45623C8850090D6B0207C438B29E9D531F04579C5987E2E330A2F492B`로 일치한다. Application 로그의 DLL 오류는 잘못된 파일을 실행했던 시각의 기록이다. Computer Use에서 native 앱 목록/제어를 제공하지 않아 사용자 데스크톱에서 창 표시를 시각 검증하지 못했다. 도구 실행 세션에서 시작한 Worker는 정리했다. `C:\GameProject`는 없어 추가 복사 위치는 생략했다. 다음 실제 실행에서 PLAN→REVIEW 연속과 동일 세션 ID 연결을 재현 확인해야 한다.
- 저장소 인계: `git fetch origin` 성공, 최초 `git pull --rebase`는 unstaged 변경 때문에 정책대로 중단. 요청에 따라 7개 변경 파일을 검토·커밋한 뒤 `git pull --rebase` 재실행 성공(당시 원격과 동기화됨). 동기화 후 최신 `GPT-Web-Feedback.md`를 다시 읽었고 파일의 최신 표기는 2026-09-23이며 이번 세션 수정과 충돌하는 추가 지시는 없었다. 이 수정 및 잔여 확인은 `GPT-6-LUNA-HANDOFF.md`의 이주용 문구에도 기록했다.

## 재현 후속 — 세션 저장소 경로 확인 (2026-09-24)

- 사용자가 올린 07:38 화면의 Worker transcript `_20260924_073844.txt`와 같은 시각 Codex rollout을 대조했다. 이전 복구판(C928… 해시)이 실제 실행되어 계획은 다시 `exit 0`으로 성공했으나 세션 ID 연결에서 막혔다. 해당 rollout 파일은 `C:\Users\ornit\.codex\sessions\2026\09\24`에 실제 생성돼 있었다.
- 복구 코드가 `Environment.GetFolderPath(UserProfile)`를 사용해 `C:\Users\CodexSandboxOffline\.codex`를 찾고 있었고, Codex CLI는 `USERPROFILE=C:\Users\ornit`의 `.codex`를 사용했다. 이 프로필 경로 불일치가 새 rollout 검색 실패의 원인이다. `CODEX_HOME`을 최우선, `USERPROFILE`을 다음 우선으로 사용하고 .NET special-folder 값은 마지막 fallback으로 바꿨다.
- 검증: Debug 빌드 경고 0/오류 0, 전체 35개 테스트 통과(Worker 30, Core 1, Agent 3, Server 1), `git diff --check` 통과. 프로필 경로 우선순위 회귀 테스트를 추가했다. Release 게시 성공, 게시 EXE SHA-256 `A379242F7027B5DA0443ADF5D20E5D3C22174B81F73B03B175E032B5AF1892C3`. 기존 `C:\AI-AGENT\Worker` 파일은 아직 C928… 해시다. 게시 스크립트의 해당 위치 덮어쓰기는 자동 승인 검토가 명시적 배포 승인이 없다는 이유로 거부했다. 실행 중 Worker와 `C:\GameProject`는 없었다. 실제 복사 및 화면 재검증은 미완료다.

## 현재 후속 — 관제 세션 식별 복구와 작업 흐름 애니메이션 (2026-09-24)

- 첨부 시각과 일치하는 `Worker/Task/Worker_NewThread/_20260924_010358.txt`를 조사했다. SOL PLAN 호출은 `exit 0`으로 작업 카드를 반환했지만 결과의 session 필드가 비어 있었다. Worker는 같은 관제 세션으로 REVIEW 해야 한다는 계약에 따라 Luna 호출 전에 중단했다. 대응 시각·작업 폴더의 Codex CLI rollout 메타데이터에는 `originator=codex_exec`, `source=exec`, 일치하는 CWD와 session ID가 기록되어 있었다. 즉 관제 추론 실패가 아니라 stdout의 `thread.started` ID를 Worker가 받지 못한 세션 상관관계 실패다.
- JSONL session ID 읽기를 BOM 및 필드 대소문자 차이에 강하게 만들고, 이벤트가 빠진 경우 CLI 세션 디렉터리에서 호출 전 snapshot 대비 새 파일 중 `codex_exec`/`exec`, 동일 CWD, 호출 시간대가 모두 맞는 세션이 정확히 하나일 때만 ID를 보완하도록 했다. 후보가 없거나 여러 개면 이전과 같이 구현 AI를 실행하지 않아 엉뚱한 세션 리뷰를 막는다. 기존 요청별 SOL 계획 → Luna 구현 → 같은 SOL 세션 검토 구조는 유지한다.
- pipeline 화살표가 한 칸뿐인 경로에서도 opacity/이동 pulse를 보이며, Luna에서 SOL 관제로 되돌아가는 화살표도 역방향 표기로 움직이게 했다. 중앙 Current Task 화살표도 세 기호 사이에 진행 pulse가 순환한다.
- 검증: Debug build 경고 0/오류 0; 전체 34개 테스트 통과(Worker 29, Core 1, Agent 3, Server 1); `git diff --check` 통과. JSONL BOM/대소문자 파서, 동일 폴더·호출 시각의 세션 rollout 유일 후보 및 모호한 복수 후보 거부를 테스트했다. Release 게시 성공, 게시 EXE SHA-256 `C92837826EEF2C52443C2B4EEBCD526DB8F6E0C27CCCF7BF464F9B1E2470C258`. C:\GameProject는 현재 없어 자동 복사되지 않았다. `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`는 구버전 해시 `FC421C2E13D514CA37C50C388333C005C4385CEF5E0FD7FEE955EEBD86ED7F04`이며 활성 PID 50960이 이 파일을 사용 중이다. 종료·교체 시도는 자동 검토가 거부해 새 변경은 게시 출력에만 있다. Native desktop 앱이 연결되지 않아 실행 화면에서 애니메이션을 직접 확인하지 못했다.

## 현재 후속 — 역할 모델 capability 중복 검사 제거 (2026-09-24)

- 첨부 화면의 `gpt-6-sol / high`는 설정 enum에 실제 포함되어 있고 `CodexModelRequest`가 CLI 요청 인수로 조합할 수 있다. 그러나 실행 전 안내는 별도로 비동기 로딩한 `codex debug models` 카탈로그로 같은 조합을 재검사해 차단했다. 이 두 목록의 차이/시점 차이가 사용자에게 보인 오탐 경로다.
- 실행 preflight에서 동적 카탈로그 기반 coordinator/implementer 차단을 제거했다. 모델·추론 유효성은 설정 콤보와 요청 조합이 공유하는 `CodexServedModels` enum만 기준으로 한다. 미등록 저장값은 설정 콤보에 임의 항목으로 추가하지 않고 enum 첫 모델 및 해당 모델 기본 추론으로 안전하게 표시한다. 폴더·provider·transport·CLI 인증 검사는 유지한다.
- 자동 회귀 검사에 정확히 `gpt-6-sol / high` 조합이 CLI 인수로 만들어지는 검증을 추가했다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0); `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 32개 통과(Worker 27, Core 1, Agent 3, Server 1); `git diff --check` 통과. Release 게시 성공, 새 EXE SHA-256 `FC421C2E13D514CA37C50C388333C005C4385CEF5E0FD7FEE955EEBD86ED7F04`. `C:\GameProject` 폴더가 현재 없어 프로젝트 게시 후 복사는 실행되지 않았다. 활성 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe` PID 27136의 EXE는 이전 SHA-256 `44C02E1A1F140F09E1CFF5233FEFD5644A7F82A78AED8F6A6A652F75A1259758`이고, 종료 시도가 자동 승인 검토에서 진행 중 작업 중단 위험으로 거부되어 덮어쓰지 않았다. `git fetch origin`은 성공했지만 미커밋 변경이 있어 `git pull --rebase`는 저장소 지침에 따라 중단했다. 따라서 최신 피드백 확인 및 커밋/푸시는 아직 수행하지 않았다.

## 현재 후속 — JEV 설정 검사 기록과 비차단 적용 (2026-09-24)

- CLI-to-CLI 사전 검사에서 JEV 활성 여부를 실행 차단 조건으로 사용하던 오류를 제거했다. 실행 preflight는 실제 실행에 필요한 경로·CLI 인증 등 환경 조건만 확인한다. 모델/추론 유효성은 역할 콤보와 CLI 요청 구성이 공유하는 enum이 담당한다. Luna 뒤의 실제 CLI-to-CLI 경로는 구현된 Sol 검토로 진행하며 Judge 카드를 다음 활성 단계라고 잘못 표시하지 않는다.
- `JSON 설정 테스트` 실행 결과를 `target-settings.json`의 `judgeEndpointValidation`에 기록한다. provider/endpoint/timeout의 SHA-256 fingerprint, 성공 여부, 결과 코드, UTC 시각만 저장하고 Endpoint 주소나 응답 전문을 검증 기록에 중복 저장하지 않는다. 적용하는 현재 설정과 fingerprint가 일치하는 성공 기록이 없으면 미검증/실패 경고를 띄우되 저장·실행을 막지 않는다. 테스트 실패 시에도 결과를 보존하여 사용자가 환경을 확인하고 직접 다시 검사할 수 있다. Endpoint/timeout/provider가 바뀌면 fingerprint 불일치로 현 설정은 재검사 대상으로 판단한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0); `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 32개 통과(Worker 27, Core 1, Agent 3, Server 1); `git diff --check` 통과. Worker 테스트에 JSON round-trip, 구성 변경 시 stale 기록, 미검증/실패 경고 동작을 추가했다. Release publish 성공; 게시 EXE와 `C:\GameProject\ProjectHub.Worker.exe` SHA-256 `376D4094D3609A518D4A964E54DBDDDDCF01DD045E0AB8C7F611432740AB5FC9` 일치. 실행 중인 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`는 자동 검토가 종료를 거부한 기존 프로세스 상태를 보존하기 위해 덮어쓰지 않았다.

## 현재 후속 — 시작 대기 카드 색상 및 하단 preflight 동기화 (2026-09-24)

- 시작/새 작업 입력 대기 상태에서는 설계 관제·작업·고수준 작업·판단 역할 카드를 모두 역할색으로 표시한다. 실행이 시작되어 이력 모드로 바뀌면 기존 current/next만 컬러로 표시하는 단계별 강조 규칙을 적용한다. 중간에 설정을 적용해도 본문 모드와 실행 단계에 따라 카드 표시가 일관되게 다시 계산된다.
- 하단 안내와 실행 버튼 활성 조건을 `GetDashboardPreflightError()` 한 경로에서 계산하도록 정리했다. coordinator-first 모드는 현재 저장된 `_targetSettings`의 JEV/high-level/provider/model/reasoning/folder 값, Web 모드는 로그인/Bridge/확장/대화 연결 상태를 기준으로 한다. 설정 적용 직후 안내도 갱신한다. 편집 중인 미적용 초안은 실행에 사용되지 않으므로 안내에 반영하지 않는다. 실행 설정 파일의 비민감 필드도 확인했으며 현재 `CLI_TO_CLI`, `judge.enabled=true`, `highLevelEnabled=false`, coordinator `codex_cli`다. 첨부 화면의 JEV 안내는 현재 저장 설정과 일치한다. 판단 AI를 끄고 적용하면 같은 계산 경로가 이를 반영한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0); `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 30개 통과(Worker 25, Core 1, Agent 3, Server 1); `git diff --check` 통과. Release 게시 성공, `C:\GameProject\ProjectHub.Worker.exe`와 `src/ProjectHub.Worker/bin/ProjectHub.Worker.exe` SHA-256 `415765284F766BBA794373CE12B73BD8D5F91A31EAF1ECD37986723C95B7A451` 일치. `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`는 다른 이전 바이너리 상태라 갱신하지 않았다. 실제 Explorer 화면 확인은 CUA Native apps가 제공되지 않아 미실행이다.

## 현재 후속 — 메인 화면 시각·입력/이력 상태 보정 (11-UI-B-K1–K27, 2026-09-24)

- 동기화된 최신 피드백 `e4859dd`의 11-UI-B-K1–K27을 한 UI 작업 범위로 반영했다. 메인 행 높이와 여백을 승인 비율에 맞추고, 상단 요약/서버 카드/설정 카드를 확대·정렬했다. 5개 단계는 5개의 동일 폭 카드와 4개의 좁은 connector로 재구성했으며, 카드 제목/아이콘/모델을 중앙 정렬했다.
- 현재 단계와 다음 단계만 색을 유지하고 나머지는 회색 아이콘 자산/배경/글자로 표시한다. 현재 카드에만 강조 그림자를 쓰며 실제 route의 화살표만 pulse한다. `TaskDirection` 텍스트 파싱을 제거하고 상태 enum을 기준으로 단계/skip 경로를 계산한다. 고수준 runner가 연결되지 않은 동안 고수준 작업은 active로 표시되지 않는다.
- `메시지 및 작업 이력` 본문을 입력/이력 두 모드로 사용한다. 유휴 시 큰 입력란과 실행 버튼, 작업 접수 직후 최신순 요약 이력, 진행 중 취소, 완료 뒤 이력 유지와 새 작업 버튼으로 전환한다. footer 입력 bar는 제거했다. 요약은 요청/구조화 결과의 짧은 부분만 사용하고 JSON 전문·URL·token 형태 문자열을 감춘다. 상세 transcript/export는 그대로 유지한다. 실행 불가 이유는 footer 한 줄에 표시하고 CLI 모드의 Server offline은 차단하지 않는다.
- 검토/잔여: K26 Explorer 3상태 화면 캡처 수용과 K27 별도 JEV 검증은 미완료다. 연결된 CUA 앱 목록이 비어 있어 화면 조작/캡처를 할 수 없었다. 실행 중인 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`가 main window handle 없이 트레이에 남아 있다. 이를 종료해 Worker 배포 폴더를 덮어쓰려던 단계는 자동 승인 검토에서 `Stop-Process -Force`가 상태 손실 위험으로 거부되어 중단했다. 사용자 승인 전에는 해당 프로세스를 종료하거나 배포 폴더를 덮어쓰지 않는다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0); `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 30개 통과; `git diff --check` 통과. Release publish 성공. 최신 게시 EXE와 `C:\GameProject\ProjectHub.Worker.exe` SHA-256은 `9E52E2EA7017F331D266A8554558B6A0121C1016B97DD696876458694F1728FB`로 일치한다. `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`는 실행 중 인스턴스 때문에 이전 SHA-256 `10E30B463B54B5011D4718A4519C1C51C8BD8F83ED833B3EC4B26BE81FD3CAF8` 상태라 최종 배포 복사는 잔여다. CUA에서 Native apps 목록도 비어 있어 실제 화면 확인은 수행하지 못했다. EXE 프로세스 상태 확인은 화면 수용 검증으로 간주하지 않는다.
- 저장소: 작업 전 `git fetch origin` 및 `git pull --rebase`를 완료했고 최신 `GPT-Web-Feedback.md`를 읽었다. 구현/문서 변경을 검토 후 커밋·푸시한다.

## 현재 후속 — 승인 이미지 기준 메인 화면 (11-UI-B-A–H, 2026-09-23)

- 동기화된 `GPT-Web-Feedback.md`의 11-UI-B 지시와 승인 화면을 확인했다. 메인 창을 상단 프로젝트/폴더/서버 요약 및 설정 버튼, 고정 5단계 파이프라인, 메시지·작업 이력, 우하단 실행 버튼의 단일 화면으로 재구성했다. 기존 설정 팝업과 내부 동작 컨트롤은 보존했다.
- 설정에서 프로젝트/작업 폴더/서버 주소와 역할별 모델명·아이콘을 카드에 연결했다. 현재/다음 단계만 컬러로 표시하며 진행 경로 화살표만 pulse하고, 선택형 high-level/JEV를 건너뛰는 경로를 표현한다. 사용자 화면 이력은 원문 transcript와 분리해 최신순의 단계/유형/payload 크기/파일 수/검증 상태를 표시하고, 내부 transcript/export는 보존한다.
- 사용자 결정에 따라 -G 입력은 작업 대기 중에만 표시되는 Dashboard 텍스트 상자로 연결했다. `TaskLaunchRequest`가 보이는 입력, 작업 폴더, 선택 세션을 하나의 요청으로 전달하며, 비어 있는 입력/준비되지 않은 연결에서는 실행 버튼을 비활성화한다. 실행 중에는 같은 버튼으로 취소하고 입력란은 감춘다.
- 완료: 11-UI-B-A–H 구현 및 자동 회귀 검증. 잔여: -J 실제 Explorer 화면에서 폼·단계 애니메이션·입력/전송/취소 동작을 확인하는 수용 검증.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0); `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 30개 통과; `git diff --check` 통과. Release 게시 성공, `C:\AI-AGENT\Worker` 및 `C:\GameProject` 복사 완료. 세 실행 파일 SHA-256은 `8031046C35FFBACB9A93B7D6D489D97B65CD712614C1AE5BCD1D5792A015F858`로 일치한다. 게시 EXE를 실행해 PID 41236, 창 제목 `ProjectHub`인 프로세스가 살아 있음을 확인했다. Explorer 화면 캡처/조작은 이 세션에 연결된 앱 목록이 비어 있어 수행하지 못했다. 첫 샌드박스 빌드는 SDK 경로 접근 거부로 막혔고, 승인된 재실행에서 XAML 컴파일을 통과했다.
- 저장소 동기화: 변경 전 `git fetch origin`과 `git pull --rebase`를 완료하고 최신 `GPT-Web-Feedback.md`를 읽었다. 변경 적용 후 커밋·푸시 예정.

## 최근 설정창 작업 통합 요약 (11-UI-A 후속, 2026-09-23)

커밋 `36af426`–`22b2262`에서 반영한 범위:

- 설계·관제 AI / 작업 AI / 고수준 작업 AI / 판단 AI 제목을 보존한다. 저장소·폴더와 AI 설정 카드를 정렬하고, 메인 콤보와 역할별 스레드/작업 폴더 연결을 유지한다.
- 설치 Codex CLI를 직접 조회해 현재 지원 모델 7개와 모델별 추론 깊이를 enum으로 만든다. 설정 선택은 `--model` 및 `model_reasoning_effort` CLI 인수로 조합하며, 실행 전 CLI capability 확인을 유지한다.
- 판단 AI JSON endpoint 테스트 결과를 카드 왼쪽 빈 영역에 표시한다. 성공은 파란 `Endpoint 응답 확인 완료`, 실패는 빨간 `Endpoint 확인 실패`; 오류 상세는 tooltip/MESSAGE에 남긴다.
- 설정 팝업을 위로 60px 이동해 footer 버튼을 화면 안쪽으로 당겼다. 창 드래그, 메인창 입력 차단, 아이콘/카드 UI도 유지한다.
- 검증: Debug build 0 warning/0 error, 전체 30개 테스트 통과, `git diff --check` 통과. Release EXE는 Worker 및 GameProject 배포 위치와 SHA-256 `2686313D9B80FF02DD507CD10DF330D0999B99A40769A03018318E385004727D`로 일치한다.
- 남은 확인: 실제 Explorer 설정창에서 전체 모델/추론 목록, footer 노출, endpoint 성공/실패 문구를 눌러 확인하는 시각·UI 검증. 현재 세션에서는 데스크톱 앱 제어가 제공되지 않아 수행하지 못했다.
- 원격 동기화: `git fetch origin`, `git pull --rebase` 완료, GPT-Web-Feedback 최신본 확인, 당시 `main`은 `origin/main`과 동일했다.

## 현재 후속 — 설정창 입력 모달·카드 표시 (11-UI-A, 2026-09-23)

- 설정 팝업을 열 때 배경 차단 오버레이를 먼저 켜고 키보드 포커스를 설정 탭으로 이동한다. 메인 창이 포커스를 받으면 입력을 폐기하고, 설정 팝업이 열린 상태의 닫기 요청은 설정창만 닫도록 했다. 팝업 내부 Escape도 설정창을 닫는다.
- 설계·관제 AI가 GPT Web 탭이면 모델 콤보를 비활성화한다. 서버 카드에는 상태 LED가 있는 랙 아이콘, CLI 스레드 카드에는 대화 아이콘을 넣었다. 팝업 기본 TextBlock 글꼴 크기를 14px로 맞췄다. 역할 카드의 네 제목은 보존 대상이며, 아래 기록의 제거 표기는 사용자 의도를 잘못 해석한 내용이다.
- 검증: Debug build 경고 0/오류 0; 전체 테스트 29개 통과; git diff --check 통과. Release 게시와 C:\AI-AGENT\Worker, C:\GameProject 복사 완료. 세 EXE SHA-256: 0A219F77B9F83FC588D7E540F23F234DF4050B4929B745DCB0F26C2A7AC0BF69.
- 저장소 동기화: git fetch, git pull --rebase 완료(원격 최신 상태). 최신 GPT-Web-Feedback.md 확인; 이번 설정 UI 수정과 충돌하는 새 지시는 없었다. 시각 E2E 캡처는 수행하지 않았다.

## 현재 후속 — 역할 제목 복원·선택 목록 연동 (11-UI-A, 2026-09-23)

- 사용자 정정에 따라 네 역할 이름을 제거하지 않고 카드 왼쪽 아이콘 위에 복원했다: 설계·관제 AI, 작업 AI, 고수준 작업 AI, 판단 AI.
- 모델 콤보를 열 때 Codex CLI 카탈로그가 0~1개 모델만 반환한 기존 UI 상태를 다시 조회하도록 하고, 조회 성공 시 모델과 모델별 reasoning 목록을 갱신한다. 스레드 선택 목록은 발견된 프로젝트/세션 전체를 제공한다. 항목 선택은 공통 작업 폴더와 메인 스레드 선택을 동기화하고, 역할 간 호환 세션을 유지하며, 적용 시 작업 폴더와 선택 세션을 저장한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0); `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 29개 통과; `git diff --check` 통과.
- Release 게시와 두 배포 폴더 복사 성공. 저장소 게시본, `C:\AI-AGENT\Worker`, `C:\GameProject` SHA-256 일치: `3EE9CE3DBA832595AECBB6F012FED7749194A64C2A354D1B2FC65833F7CF771D`.
- 잔여: 실제 설정창에서 각 모델/추론/스레드 선택 후 저장값 및 작업 폴더 갱신을 화면으로 확인하지 못했다. 최신 GPT-Web-Feedback.md를 fetch/rebase 후 확인했고 현재 origin/main은 변경이 없었다.

## 후속 — 설정창 모델·추론 목록 초기 표시 (11-UI-A, 2026-09-23)

- CLI의 `debug models`가 현재 지원 모델 7개를 반환함을 확인했다. 설정창이 시작 시 모델 카탈로그를 읽는 동안에도 열릴 수 있고, UI가 아직 초기화되지 않은 목록을 보여줄 수 있어 설정창 열기에서 공유 시작 설정 작업 완료를 기다리게 했다. 열 때 카탈로그가 0~1개면 한 번 더 조회하고 역할 모델/추론 콤보를 다시 채운다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0); 전체 29개 테스트 통과; `git diff --check` 통과. 실제 팝업 조작은 이 세션에서 데스크톱 창 제어가 제공되지 않아 미확인이다.
- Release 게시 및 Worker/GameProject 복사 성공. 세 EXE SHA-256 일치: `E2E40A6B462C633FD1957CB9D3C40888540894F6FEDBEEA9DA2996F8470D607A`.

## 후속 — 서비스 모델/추론 enum 및 요청 조합 (11-UI-A, 2026-09-23)

- 현재 설치 CLI의 `codex debug models`를 직접 다시 질의했다. `visibility=list`, `supported_in_api=true`인 모델 7개와 모델별 추론 enum 목록을 `CodexServedModels`로 고정 정의했다. UI 목록은 늦은 CLI catalog 로딩에 의존하지 않는다.
- 설정에서 고른 모델/추론을 `CodexModelRequest`에 검증·보관하고, 실제 CLI 요청에 `--model <model-id> -c model_reasoning_effort="<effort>"`로 추가한다. 동등한 query 표현은 `model=<model-id>&reasoning=<effort>`이며, CLI 실행은 HTTP URL query 대신 CLI 인수 계약을 사용한다. 런타임 catalog 기반 실행 전 capability 검증도 유지한다.
- 검증: Debug build 경고 0/오류 0; 전체 30개 테스트 통과. 새 fixture가 7개 enum 모델 수, Luna의 `ultra` 거부, query 조합 및 실제 CLI 인수 순서를 확인한다. 설정창의 픽셀/UI 확인은 현재 세션의 데스크톱 창 제어가 없어 미확인이다.
- Release 게시본과 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사본 해시를 재시도 후 대조해 모두 일치함을 확인했다: `6C14DC32491815EF2C21CAD65EF7C534733598580B1B03B27752DC25C25BACD7`.

## 후속 — 설정 팝업 하단 버튼·JEV 테스트 상태 표시 (11-UI-A, 2026-09-23)

- 팝업을 위로 60px 이동해 하단 닫기/적용 버튼이 작업표시줄/화면 아래에 가려지지 않도록 조정했다.
- 판단 AI 카드의 비어 있는 두 번째 행에 endpoint 테스트 상태를 표시한다. 성공 응답은 파란색 `Endpoint 응답 확인 완료`, HTTPS 설정 오류/실패/예외는 빨간색 `Endpoint 확인 실패`로 보이고 상세 결과는 tooltip과 MESSAGE에 남는다. 요청 중 버튼은 중복 입력을 막고 완료 뒤 다시 활성화한다.
- 검증: Debug build 경고 0/오류 0; 전체 30개 테스트 통과; `git diff --check` 통과. 화면 캡처는 현재 데스크톱 UI 제어가 없어 미확인.
- Release 게시본 및 Worker/GameProject 복사본의 SHA-256 일치: `2686313D9B80FF02DD507CD10DF330D0999B99A40769A03018318E385004727D`.

## 현재 요약 — 2026-09-23 / Task 11-A 구현 완료

동기화된 깨끗한 기준점 `f501694469f2f1a590d1739be5e6039978c7ebde`에 복구 태그 `recovery/before-11a-cli-to-cli-2026-09-23`를 만들고 시작했다. Task 11-A의 A/B/C를 완료했다.

- 설정에 Legacy Web / coordinator-first CLI 모드, coordinator·implementer별 모델/reasoning 선택을 추가했다. Codex CLI model catalog에서 공개·지원되는 모델과 reasoning만 실행 가능하며 자동 대체하지 않는다. 설정 계약은 기존 JSON과 호환된다.
- 역할별 provider/model/reasoning 설정을 각각 저장·표시한다. 현재 선택 가능한 provider는 OpenAI Codex CLI이며, 다른 provider가 설정 파일에 있으면 미지원 상태로 표시하고 실행을 차단한다.
- Sol 관제는 read-only 별도 세션에서 구조화 work card를 만든다. 유효 카드와 세션 ID가 확보되기 전 Luna를 실행하지 않는다. Luna는 별도 workspace-write 세션에서 구현·구조화 결과를 반환한다. 같은 Sol 세션이 read-only 검토한다.
- 작업 완료는 implementer exit 0, 모든 필수 validation command의 JSONL 관측 및 실제 exit 0, 구조화 검토의 정확한 AC 집합 전체 PASS, `ACCEPT`가 모두 충족될 때만 허용한다. 역할·모델·reasoning·session·호출 usage를 MESSAGE/telemetry에 기록하고 취소 시 프로세스 취소와 UI 복구를 연결했다.
- JEV ON은 현재 CLI-to-CLI 모드에서 preflight 차단하며 Legacy Web에서 유지한다. ACTION/NEXT Web 계약을 바꾸지 않았다. 09-B E2E 잔여는 사용자 결정대로 해결 처리/정기 관리 제외, Bridge 이슈와 07 잔여는 기존 정책 유지.
- 검증: `dotnet test ProjectHub.sln --configuration Debug --no-restore` 통과 (Core 1, Agent 3, Server 1, Worker 23; 전체 28); `node --check extension/gptweb-hub/content.js`; `git diff --check` 성공. disposable temp 폴더에서 지원 모델 `gpt-5.6-sol` / `low`의 구조화 출력 및 session ID smoke 통과.
- 후속 재확인(2026-09-23): 기존의 “GPT-6 Sol/Luna가 미지원” 안내를 정정한다. 현재 `codex-cli 0.155.0-alpha.16`의 `codex debug models`는 두 모델 모두 `visibility=list`, `supported_in_api=True`로 표시한다. 실제 `codex exec --ephemeral --sandbox read-only` 호출도 각 모델 `low` 설정에서 성공했다. Explorer Worker를 재시작해 최신 catalog를 읽혔다.
- Release 게시 성공. `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`와 `C:\GameProject\ProjectHub.Worker.exe` 복사본 및 프로젝트 게시본의 SHA-256은 `11E56140632E5BCCA5650B5310AC9C585532F606B25A8BC2A07466AB8FF5C8D1`로 일치한다. Explorer에서 기존 Worker를 종료한 뒤 새 Worker 창이 응답하는 것을 확인했다.
- 다음 후보는 11-B JobRunner 분리/재시작 복구다. 이번 범위에서는 시작하지 않았다.

## 현재 요약 — 2026-09-23 / 09-C 및 10-A/B/C 구현 완료

정책/피드백 동기화 기준 HEAD는 `6c4634e`이다. 동기화된 피드백의 CLI-to-CLI 네 역할 설계를 검토했다. 사용자 결정에 따라 09-B E2E 잔여는 해결 처리하고 정기 관리에서 제외하며, 재발할 때만 새 이슈로 등록한다. 09-C evidence envelope 이후 Task 10-A/B/C에서 capability 계층 분리, 호출별 사용량 계측, 격리 음성 대조 fixture를 구현했다.

- `JevContract`는 첫 유효 NEXT만 해석하고, Judge ON Web은 첫 본문 `[REPORT]`, JEV는 `[VALIDATION REQUEST]`를 요구한다. NOUL/SCORE/CHOICE의 구조·범위·연속 번호·허용값을 검증한다.
- `JevJudgeRunner`는 `TYPESAFE_API_KEY`를 환경변수에서만 읽고, 실제 키·응답 전문·endpoint를 로그나 Web 메시지에 남기지 않는다. 누락/타입/범위/알 수 없는 ID는 ERROR, 유효 응답의 threshold 미달은 PARTIAL이다.
- Footer v1의 고정 표식은 유지한다. `JevEvidenceEnvelope`은 Codex의 텍스트 파일 artifact를 제한적으로 수집하고 QID→Evidence ID 연결, source revision/content digest/provenance를 만든다. free-form 결과는 `SUMMARY_ONLY`로 분리한다.
- 명시적 `EVIDENCE:` 질문이 SUMMARY_ONLY만 가지면 높은 JEV 점수만으로 PASS할 수 없다. 최신 evidence, QID별 결과와 JEV 결과는 Worker state에 보존한다. 바뀐 증거가 현재 batch 밖의 QID에 영향을 주면 그 QID만 NEEDS_RECHECK 처리하며, 현재 batch에서 이미 재검증한 QID는 추가 반복하지 않고 무관한 QID의 PASS는 다음 round에도 유지한다.
- ENGINE_HEADLESS/UI_BROWSER/HUMAN_UX/JEV 검증 계층을 별도로 보존하며 관측하지 못한 계층은 `NOT_RECORDED`다.
- ENGINE_HEADLESS에서는 Node/.NET 실행 파일 탐지와 실제 미실행 상태를 구분하고, Worker UI adapter가 없는 UI_BROWSER는 `BLOCKED_BY_TOOL`로 기록한다. HUMAN_UX는 사용자 확인이 없으면 `NOT_RECORDED`다.
- Codex/JEV/Web 호출 메타데이터와 토큰·payload byte·latency·retry·usage-known을 `%Worker%/state/usage/<job-id>/calls.jsonl`에 기록한다. 본문 대신 prompt/footer/payload hash를 쓰며 Web usage는 `unknown`이다. Codex cumulative usage snapshot 중복 합산을 방지한다.
- `JevNegativeControls.json`에 6개 격리 사례를 두고 fixture 구조 회귀를 검증했다. 실 TypeSafe provider 대조 실험은 실행하지 않아 탐지율을 주장하지 않는다.
- JEV PARTIAL은 같은 Codex 세션에서 triage 후 최대 3회 재검증하고, 미해결은 Web 검토로 넘긴다. QID와 threshold를 유지하며, report-only 단계 재진입은 `REPORT_PHASE_REENTERED_JEV`로 차단한다.
- Worker 회귀 테스트는 importance threshold, PARTIAL 분류, QID, retry 지침, evidence reference/digest/provenance, SUMMARY_ONLY 차단, QID별 이전 PASS 무효화를 검증한다.
- 검증: `dotnet test ProjectHub.sln --configuration Debug --no-restore` 통과 (Core 1, Agent 3, Server 1, Worker 16). 기본 sandbox는 Windows SDK 사용자 경로 접근이 거부되어 권한 확장 실행으로 통과했다.
- 실제 TypeSafe API 호출은 수행하지 않았다. 이번 변경의 Release 게시본을 `C:\AI-AGENT\Worker`와 `C:\GameProject`에 복사했다. 세 EXE SHA-256은 `F727D0BCD0FC0BFD9E1B6CA059089D099EB0B11E7F744CACB659C547B8EED0EF`로 일치한다. 파일 잠금 해제를 위해 기존 Worker를 종료했으며, 새 실행본의 Bridge/Extension 연결 상태는 이번 작업에서 재확인하지 않았다.
- 설정창의 JEV Timeout 입력을 명시적 활성 상태로 보강하고 숫자 입력·포커스 전체 선택·10~600초 정규화를 추가했다. 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore`, `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore`, `git diff --check` 성공.
- 2026-09-23 Worker 자동 CLI 실행의 일반 작업 sandbox를 `danger-full-access`로 변경했다. CLI 빌드가 Windows SDK·MSBuild·NuGet 외부 경로에서 접근 거부되는 문제를 해소하기 위한 설정이며, 명시적 읽기 전용 요청은 기존 `read-only`를 유지한다.
- Release 게시와 복사 증거는 위 최신 SHA-256을 기준으로 한다.



09-A·09-B의 세부 구현과 기존 실험은 아래 날짜별 이력으로 보존한다. 현재 활성 09-B/09-C/10-A/B/C 구현 backlog는 없다. 실 provider JEV 음성 대조 측정과 07 잔여 검증은 실행 여부/상태를 위 정책에 따라 별도로 관리한다.

2026-09-22 GPT Web 대화 동기화 경합 수정: ChatGPT SPA에서 대화를 빠르게 전환할 때 이전 polling 응답이 새 대화 상태를 덮어쓰지 않도록 navigation generation과 conversation ID를 함께 검증한다. Worker bridge에는 현재 대화의 binding 상태를 노출하고, Worker 설정의 GPT Web 상태를 `READY`/`BIND REQUIRED`로 구분해 미연결 대화에서 작업이 조용히 생성되지 않도록 보완했다. 연결 실패 메시지도 구체적인 원인을 표시한다. 검증: `node --check extension/gptweb-hub/content.js` 통과, `dotnet build ProjectHub.sln --configuration Debug --no-restore`는 기본 샌드박스의 Windows SDK 접근 거부 후 권한 확장으로 경고 0/오류 0 성공, `git diff --check` 통과. 현재 실행 중인 Worker는 수정 전 바이너리이므로 재게시·재기동 후 화면 검증이 필요하다.

2026-09-22 Git Target Settings 정책 정정: Git Repository 주소는 직접 입력하지 않고 저장된 Working Folder의 저장소 `origin`에서만 자동 표시하는 읽기 전용 값으로 변경했다. Working Folder가 없거나 유효하지 않으면 Git 주소·branch·HEAD를 빈 상태로 둔다. Auto Detect는 Working Folder를 지우지 않고 해당 폴더를 다시 탐색한다. Server 설정 기능은 유지하되 `Server: MANUAL`과 Working Folder의 `Source: SETTINGS` 표시를 제거했다. Git 수동 URL은 저장·검증·CLI 전달에 사용하지 않도록 정리했다.

2026-09-22 Working Folder 선택창 표시 수정: WPF 설정 Popup과 Windows FolderBrowserDialog가 서로 다른 최상위 창으로 겹치던 문제를 수정했다. 폴더 선택 중에는 설정 Popup과 입력 차단 오버레이를 잠시 닫고, 선택이 끝나면 설정 Popup을 복원해 폴더 선택창이 항상 전면에 표시되도록 했다.

2026-09-22 JEV Test 샘플 질문 변경: Test 버튼의 NOUL 질문을 `오늘 비가 올 확률은 몇 퍼센트나 될지 1.00으로 정규화해봐`로 변경하고 `PASS: YES >= 0.5` 문턱값을 적용했다.

2026-09-22 JEV Test 문턱값 표시 오류 수정: Test 버튼이 NOUL 질문과 `PASS` 문턱값을 한 줄로 생성해 계약 파서가 threshold를 찾지 못하던 문제를 확인했다. 계약 문서 형식대로 질문과 `PASS: YES >= 0.90`을 별도 줄로 생성하도록 수정했다. Debug build, 전체 테스트 5개, `git diff --check`를 통과했다.

2026-09-22 게시 스크립트 저장소 루트 탐색 정정: 복사본의 `..\Server` 고정 후보 대신 스크립트 위치에서 상위로 이동하며 `.git` 저장소 루트를 찾고, 배포 폴더에서 실행할 때는 현재 상위 폴더의 저장소 자식 후보를 탐색한다. 최종 소스는 항상 `저장소루트\src\ProjectHub.Worker\ProjectHub.Worker.csproj`, 게시·복사 대상은 `저장소루트\..\Worker`로 계산한다.

2026-09-22 복사된 게시 스크립트 빌드 경로 수정: `Worker\publish-worker.ps1`이 자기 상위 폴더를 프로젝트 루트로 오인해 `MCP\ProjectHub.Worker.csproj`를 찾던 문제를 수정했다. 이제 `..\Server\src\ProjectHub.Worker`와 상위 경로를 탐색해 원본 Worker 프로젝트를 찾으며, 원본 `src\ProjectHub.Worker\bin` 실행도 유지한다. 배포 폴더의 복사본을 갱신한 뒤 복사본 CMD로 검증한다.

2026-09-22 JEV Endpoint 설정·Test UI 보완: 근거 없는 `Auto Detect`를 제거하고 공식 계약 문서의 `https://api.typesafe.ai/v1/systemone`을 기본 Endpoint로 표시했다. Test 버튼은 최소 NOUL 계약 요청을 실제 JEV API에 보내 응답 결과를 설정 상태와 MESSAGE의 `JEV TEST` 항목에 표시한다. 저장된 Endpoint는 `JevJudgeRunner`의 실제 호출 주소로 연결하며 HTTPS만 허용한다. 검증: Debug build 성공(경고 0/오류 0), 전체 테스트 5개 통과, Extension 구문 검사 및 `git diff --check` 통과.

2026-09-22 게시/복사 역할 정정: 게시 결과는 기존처럼 Worker 프로젝트 `bin`에 생성하고, 게시 성공 후 EXE만 저장소 루트의 부모 `Worker` 폴더에 항상 덮어쓴다. `publish-worker.cmd/.ps1`은 배포 폴더에 없을 때만 1회 복사하도록 분리했다.

2026-09-22 Worker 게시 복사 경로 일반화: `publish-worker.ps1`이 저장소 루트를 기준으로 `..\Worker`를 계산해 대상 폴더를 없으면 생성하고, 게시된 `ProjectHub.Worker.exe`와 `publish-worker.cmd/.ps1`을 덮어쓰도록 변경했다. 절대경로를 코드에 넣지 않으며 저장소가 어디에 있든 동일한 상대 구조를 사용한다. 검증 대상: `C:\Projects\AI-AGENTS\MCP\Worker`.

2026-09-22 Worker 게시 경로 기준 고정: `FolderProfile.pubxml`의 기준 없는 `./bin`을 `$(MSBuildProjectDirectory)\bin\`으로 변경했다. `publish-worker.ps1/.cmd`와 `.csproj`도 Worker 프로젝트 경로의 `bin`을 사용하므로 실행 위치에 따라 게시 위치가 바뀌지 않는다. 검증 명령: `src\ProjectHub.Worker\bin\publish-worker.cmd -NoRestore`.

2026-09-22 게시 스크립트 실행 정책 보완: `.ps1` 직접 실행 시 Windows PowerShell 실행 정책으로 `PSSecurityException/UnauthorizedAccess`가 발생할 수 있어, 동일 `bin` 폴더에 `publish-worker.cmd` 래퍼를 추가했다. 래퍼는 `-NoProfile -ExecutionPolicy Bypass`로 게시 PowerShell을 호출하고 실패 시 exit code와 pause를 표시한다. 시스템 실행 정책을 전역 변경하지 않는다.

2026-09-22 JEV API smoke test: 재시작한 Codex 프로세스에서 `TYPESAFE_API_KEY`가 설정된 것을 확인하고, Worker 계약과 동일한 `[NEXT : JEV]`/`[VALIDATION REQUEST]` NOUL payload를 TypeSafe `systemone` endpoint에 전송했다. HTTP 200, `answers` 포함, C1 응답 및 `model/answers/usage` 구조를 확인했다. Worker의 `RouteCodexResultAsync`는 JEV 호출 전 `JEV REQUEST`, 응답 후 `JEV RESULT`를 MESSAGE 로그에 기록하도록 구현되어 있다. 네이티브 Worker 창 자동화 런타임을 사용할 수 없어 실제 화면 캡처 기반 MESSAGE 표시 검증은 별도 잔여로 남긴다.

2026-09-21 Worker UI 높이 점검 및 축소: `MainWindow.xaml`의 초기 창 높이가 1260px로 1080px을 초과하고 최소 높이도 1100px로 제한되어 있음을 확인했다. 초기 높이를 1050px, 최소 높이를 900px로 조정해 1080px 화면에서도 전체 UI를 표시할 수 있도록 했다. 검증: XAML 변경 후 `dotnet build ProjectHub.sln --configuration Debug --no-restore`.

2026-09-21 최신 원격 동기화: 로컬 변경은 `codex-preserve-before-latest-sync` stash로 보존하고 `origin/main`의 `ce5e93f`까지 `pull --rebase`로 적용했다. 프로젝트 정책상 `reset/checkout`은 수행하지 않았다. CMD와 Setup이 PowerShell 실행 엔진을 저장소 기준 상대 경로 `.\bin\...`로 참조하도록 정리했으며, 루트 `bin`에 모든 `ProjectHub_*.ps1` 엔진이 존재하고 CMD 대상 검사를 통과했다.

2026-09-21 프로젝트 루트 기준 경로 보완: Commit_Push/Fetch_Pull/Force_Restore/Sync 내부 엔진 참조도 `$PSScriptRoot` 기준이 아니라 전달된 프로젝트 루트의 `bin`을 기준으로 해석하도록 변경했다. 따라서 엔진이 `bin`에서 실행되거나 다른 위치에서 호출되어도 대상 프로젝트의 `bin`을 사용한다. 루트·`bin` PowerShell 전체 구문 검사와 `git diff --check`를 통과했다.

## Baseline

- 저장소: `C:\Projects\AI-AGENTS\MCP\Server`
- 브랜치: `main` (원격 `origin/main` 추적)
- 도구체인: .NET SDK 9.0.312 확인
- 솔루션: `ProjectHub.sln`
- 활성 작업: `tasks/07-project-deployment-package.md`

## 확인된 현재 구현

- `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent` 프로젝트가 생성됐다.
- `tests/ProjectHub.Core.Tests`, `Server.Tests` 프로젝트가 생성됐다.
- 프로젝트 참조가 Core 중심 계층으로 연결됐다.
- ASP.NET Core Server는 `/api/status`, workstation heartbeat/list, project state 수동 POST/GET API를 제공하며 Supabase REST 연결을 사용한다.
- `GPT-Web-Feedback.md`가 원격에서 추가됐으며, pull 전 확인 규칙을 `AGENTS.md`에 반영했다.
- Core 서비스 계약과 Infrastructure의 교체 가능한 NoOp 저장소 DI 등록 경계를 추가했다.
- `/api/status` 통합 테스트가 추가되어 HTTP 200과 기본 JSON 필드를 검증한다.
- `SupabaseOptions`와 named `HttpClient` 등록 경계가 추가됐으며 Server의 실제 Supabase E2E가 완료됐다.
- 04-A Supabase 설정/클라이언트 경계 구현과 빌드·테스트 검증이 완료됐다.
- `supabase/workstations.sql`과 `supabase/project-state.sql`을 Supabase에 적용하고 실제 row 저장을 확인했다.
- `IWorkstationRepository`, `SupabaseWorkstationRepository`, heartbeat upsert와 workstation 조회 API의 실제 Mini PC→Supabase E2E가 완료됐다.
- heartbeat upsert payload에서 null 메타데이터 필드를 제외하고, Supabase 오류 응답 본문을 읽어 502로 반환하도록 보완했다.
- 사용자가 Mini PC와 Supabase에서 04-F 실제 E2E를 완료했다. heartbeat upsert, 중복 방지, `last_seen` 갱신, workstation 조회, 서버 재시작 후 persistence를 확인했다.
- `supabase/project-state.sql`에 04-G 첫 단계의 `projects`와 `project_states` 최소 스키마를 정의하고 사용자가 Supabase에서 실행했다.
- `SupabaseProjectStateRepository`와 수동 project state POST/GET API를 구현했다.
- 사용자가 서버 PC에서 project state POST/GET, Supabase row 저장, 동일 project/workstation 재전송 update를 검증했다.
- `head_sha`에 실제 커밋 SHA `cebda36a4937056e9abd11254131ee42ad7afc83`가 저장된 것을 확인했다.
- `ProjectHub.Agent`가 설정 기반 heartbeat sender/runner로 구현됐으며 실제 외부 DEV PC E2E까지 완료됐다.
- 평상시 Agent에서는 대용량 hash/upload/staging/reconciliation을 수행하지 않도록 분리했다. 명시적 `ProjectHub_Sync.ps1`이 시작 시점의 size/mtime 고정 manifest를 만들고 control-plane Git/file 요약을 먼저 Server에 반영한 뒤, 별도 `ProjectHub_LargeData_Uploader.ps1`가 실제 upload/resume/finalize와 metadata checkpoint를 수행한다. uploader는 project/workstation/object hash/size 기준으로 Supabase `UPLOADING` session을 조회해 기존 session ID를 재사용하고, 없을 때만 GUID session을 생성한다.
- `GitStateCollector`가 등록된 localPath에서 branch, HEAD full SHA, dirty, changed/untracked/deleted 수를 읽기 전용으로 수집한다. 05-B 관련 테스트가 통과했다.
- `ProjectActivityMonitor`가 등록 프로젝트를 감시하고 1초 debounce 후 Git 상태를 기존 project-state API로 전송한다. 실제 DEV PC 루트 프로젝트 외부 E2E까지 검증했다.
- 실제 DEV PC 저장소 루트에서 임시 파일 생성 후 공식 HTTPS 터널 경유 Agent → Server → Supabase 상태 갱신을 확인했다. `dirty=true`, `untracked_count=1`, `last_file_activity` 갱신을 확인하고 임시 파일·설정을 복구했다.
- 임시 E2E에서 발견한 `last_file_activity` 누락을 수정해 파일 이벤트 처리 시각을 자동 기록하도록 보완했다. 빌드·테스트 재검증도 통과했다.
- Server 기본 origin을 표준 `Urls` 설정으로 `http://127.0.0.1:5240`에 고정하고, `ASPNETCORE_URLS` 또는 실행 인자로 재정의할 수 있게 했다.
- 사용자가 Cloudflare Named Tunnel `projecthub`와 `projecthub.ornithopter.bid`를 구성하고 외부 `/api/status` 성공을 확인했다. Agent 외부 E2E는 아직 검증하지 않았다.

## 목표 구조

개발 PC Agent가 heartbeat와 Git 상태를 Server에 보내고, Server의 ProjectService가 Infrastructure 저장소를 통해 Supabase에 상태·이벤트·lease를 기록한다.

## 진행

잔여 작업: Worker-B 최소 smoke test, 현재 실행 중인 작업 중지 동작 검증, 이후 Worker-D GPT Web bridge

## 작업 정책

- 한 번에 하나의 세부 작업만 수행한다.
- v0.2에서 Explorer 사용자가 명시적으로 실행한 Commit_Push/Fetch_Pull CMD에 한해 Git 변경을 수행한다. Agent 자동 실행·원격 shell·reset/checkout은 금지한다.
- 비밀값은 환경 변수로만 읽고 저장소에 기록하지 않는다.

## 표준 검증

```powershell
dotnet build ProjectHub.sln --no-restore
dotnet test ProjectHub.sln --no-restore
```

## 현재 작업

06 Large Data/NAS 기능 검증 완료 / 07 프로젝트 배포 패키지 검증 중

07 구현: Setup/Sync/Restore 배포 패키지에 이전·현재 manifest diff, REMOVED 승인 GUI, Server tombstone API, current-folder Restore의 LOCAL_ONLY 보호와 REMOVED 삭제 승인을 추가했다. `hw`는 `ProjectHub\\bin`, `ProjectHub\\config`, `ProjectHub\\state`, `ProjectHub\\log` 구조로 최신화했고 루트에는 Commit_Push/Fetch_Pull/Force_Restore 3개 진입점만 유지했다. 남은 검증은 Force Restore GUI 승인 동작 1건이다.

2026-09-17 재검증: Server `/api/status=200`, NAS Gateway `200`, GC dry-run `safe=0, keep=0, review=0`을 확인했다. 500MiB `forUpload.z01`은 .NET SHA-256 fallback으로 해시 계산 후 기존 session 재사용, NAS `already_present`, 원본과 동일한 size/hash, `STAGED`, `CHECKPOINTED`까지 성공했다. Server session은 `COMPLETED`로 확인됐다.

2026-09-18 최신 피드백 반영: `Invoke-WebRequest -InFile` 대용량 chunk 전송 회귀를 수정해 binary PUT에 `curl.exe --data-binary`를 사용하도록 변경했다. assertion cache와 401 1회 refresh는 유지하고, chunk 실패 진단에 파일·session·index·size·URI·HTTP status·예외 정보를 추가했다. `hw`에서 Explorer Sync와 동일한 경로로 500MiB 업로드를 재검증했으며 실패 0건, SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, Server `STAGED`/`COMPLETED` session/`CHECKPOINTED`를 확인했다. checkpoint commit은 `be3cff250b18ce651f1e50167a9ff8407d395197`이다.

2026-09-18 operation logging 구현 및 검증: `ProjectHub.Server` category로 주요 operation만 Information 로그를 남기고, Microsoft/ASP.NET Core/HttpClient 반복 로그는 Warning으로 제한했다. Console single-line/timestamp를 적용했다. `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore` 통과 후 로컬 Server `http://127.0.0.1:5280`에서 `SERVER_STARTED` 로그와 `/api/status=200`을 확인했다.

2026-09-18 삭제 반복 표시 원인 수정: `Removed` enum의 실제 JSON 값 `7`을 인식하도록 `Sync`를 수정하고, checkpoint 응답에서 단일 commit SHA를 명시적으로 선택하도록 보완했다. `hw` 삭제 API가 `forUpload.z01`을 tombstone 처리했고, 최신 `Sync`를 `hw\bin`에 복사해 실행한 결과 `0 large files`, `Removed=0`, `Failed=0`으로 확인했다.

2026-09-18 최신 feedback 구현 완료: `ProjectHubConsoleFormatter`와 공통 operation log helper로 최종 콘솔 형식 `yyyy-MM-dd HH:mm:ss [LEVEL] [WORKSTATION] [PROJECT] MESSAGE [STATUS]`를 적용했다. `dotnet build ProjectHub.sln --no-restore` 및 `dotnet test ProjectHub.sln --no-restore`가 모두 통과했고, 로컬 Server startup의 `SERVER_STARTED`와 `/api/status=200`을 재확인했다.

2026-09-18 NAS 삭제/Full-log 구현: Sync 승인 후 Server가 활성 object 참조 수를 확인해 NAS named alias를 삭제하고, 참조가 0일 때 canonical object까지 삭제하도록 `delete-object.php`와 delete assertion 흐름을 추가했다. 다른 활성 참조가 있으면 object를 보존한다. NAS 실패는 `PARTIAL`과 ERROR 로그로 남긴다. `ContentRoot\log\yyyyMMdd.log` 일자별 file logger와 heartbeat 포함 file-only 로그, rollover 및 정상 종료 구분선을 추가했다. C# build/test는 통과했고, local startup `/api/status=200` 및 일자별 log 파일 생성을 확인했다. 실제 NAS delete PHP 배포/운영 E2E가 다음 검증 항목이다.

2026-09-18 운영 배포 E2E 완료: 배포된 Server와 Gateway health를 확인하고, `hw` delete assertion으로 named alias 및 canonical object 삭제 성공을 확인했다. 동일 object 재호출은 `already_deleted=true`로 idempotent 동작했다.

2026-09-18 운영 로그 후속 수정: 문자열 `operation=delete` 요청이 400을 남긴 호환성 문제를 확인해 `LargeDataOperation`에 `JsonStringEnumConverter`를 적용했다. 숫자 요청 호환성을 유지하며 문자열/숫자 입력을 모두 허용한다. build/test 통과; 운영 재배포 후 최종 확인 대기.

추가 GC 검증: PowerShell 배열 응답 호환성 문제를 수정한 뒤 완료 session 2개를 개별 인식했고, dry-run은 SAFE 2개(각 524,288,000 bytes), KEEP 0, REVIEW 0으로 정상 집계됐다. `-Apply`는 실행하지 않았다.

GC 적용 검증: 사용자가 승인한 범위에서 두 SAFE session에 `-Apply`를 실행해 Gateway cleanup을 완료했고, 같은 명령을 다시 실행했을 때 두 session 모두 `ALREADY_CLEAN`으로 반환됐다. 테스트 `UPLOADING` session은 GC dry-run에서 `KEEP`으로 보호됐고, fixture 삭제 후 session count 0 및 SAFE/KEEP/REVIEW 0을 확인했다.

2026-09-17 재검증: NAS Gateway health 및 `provision.php=405`, `upload-start.php=405`는 응답했고, 일시적인 운영 Server `502` 복구 후 `/api/status=200`, GC, assertion 기반 업로드를 완료했다.

후속 개선 구현: uploader assertion을 파일/session 범위에서 캐시하고 JWT 만료 임박 또는 Gateway 401에서만 1회 refresh한다. 파일별 실패 격리, mixed batch checkpoint 보수 정책, `STAGING_CLEANED; OBJECT_RETAINED` 출력, manifest `.result.json` 결과 기록을 추가했다.

실제 NAS1DUAL 기준:

- PHP-visible storage root: `/mnt/HDD1/ProjectHub`
- 운영자가 `objects/sha256`와 `staging`을 미리 생성하고 Gateway는 root 내부만 사용
- Gateway HTTPS port: `8443`
- Gateway URL: `https://dfblackbox-nas.duckdns.org:8443/projecthub/`
- Agent는 Supabase·SMB·NAS filesystem에 직접 접근하지 않고 NAS Gateway HTTPS만 사용

완료: 05 실제 DEV PC root E2E, 06 RS256 assertion 및 NAS provision/authentication E2E

완료: 명시적 Batch Sync manifest와 별도 uploader 분리, main metadata 선반영, 기존 resumable session 재사용, chunk/status/resume/finalize → STAGED → Git checkpoint/CHECKPOINTED 경로 구현. Agent 재시작·watcher는 자동 upload/staging을 시작하지 않는다. 업로드 중 source 변경은 `CHANGED_DURING_UPLOAD`으로 checkpoint 대상에서 제외한다.

최종 검증 선행 결과: `https://suhonas.ipdisk.co.kr:8443/projecthub/`는 인증서 검증 실패(`SEC_E_WRONG_PRINCIPAL`, SNI/certificate hostname 불일치)로 정상 TLS health check가 되지 않았다. 운영 Agent에 TLS 우회는 적용하지 않으며, NAS 인증서/hostname 정리 후 upload E2E를 재개한다.

2026-09-18 사용자 진입점 UX 보완: `ProjectHub_Setup.cmd`, `ProjectHub_Sync.cmd`, `ProjectHub_Restore.cmd`, `ProjectHub_GC.cmd`, `ProjectHub_update.cmd`, `ProjectHub_Agent_Test.cmd`가 성공·실패와 무관하게 종료 코드를 출력하고 `pause` 후 동일 종료 코드를 반환하도록 통일했다. `ProjectHub_update.cmd`는 콘솔을 `220x50`으로 설정해 진행 로그가 잘리지 않도록 했다. 누락된 Agent 스크립트 오류 경로도 동일한 종료 코드/일시정지 형식을 사용한다. `git diff --check` 통과; 실제 배포 PC 반영 및 Explorer 더블클릭 E2E가 후속 검증 대상이다.

2026-09-18 v0.2 정책 전환: 사용자가 Explorer에서 명시적으로 실행하는 `ProjectHub_Commit_Push.cmd`는 add/commit/fetch/pull --rebase/push 후 Sync 및 large-data checkpoint를 실행하고, `ProjectHub_Fetch_Pull.cmd`는 clean 상태 확인 후 fetch/pull --rebase와 Restore를 실행한다. detached HEAD, dirty pull 대상, merge/rebase 진행, 충돌, push reject는 자동 해결하지 않고 중단한다. 기존 Sync/Restore CMD는 호환성을 위해 유지한다.

2026-09-18 강제 복구 구현: `ProjectHub_Force_Restore.cmd`와 `ProjectHub_Force_Restore.ps1`를 추가했다. 실행 전 `FORCE` 명시 입력이 없으면 종료하며, 승인 후에만 `fetch origin` → `reset --hard origin/<branch>` → `clean -fd`를 수행한다. `.projecthub/project.json`, ProjectHub CMD, `bin\ProjectHub_*.ps1`는 임시 보관 후 복원하고, 이후 최신 checkpoint 기준 기존 Restore 엔진으로 NAS 대용량 파일과 REMOVED 파일을 처리한다. 파괴적 실행·NAS 복구 E2E가 후속 검증 대상이다.

2026-09-18 hw 순차 검증: 최신 ProjectHub 파일을 `hw`에 배포하고 일반 Fetch_Pull dirty 보호(exit 2)를 확인했다. 강제 복구는 `FORCE` 승인 후 Git `fetch/reset --hard/clean -fd`와 500MiB 로컬 삭제까지 성공했다. tombstone 재등록 후 500MiB 업로드는 원본 SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, `ALREADY_PRESENT`, `STAGED`, checkpoint `be3cff250b18ce651f1e50167a9ff8407d395197`로 완료했다. 이후 `.git`, `.projecthub`, ProjectHub 런처·엔진만 남기고 테스트 파일/솔루션/대용량 파일을 삭제했다. 최종 Force Restore의 Git 복구는 성공했지만 NAS Restore는 `RESTORE_SIZE_MISMATCH: forUpload.z01`로 실패해 원상복구 E2E는 미완료다. 업로드 중복 실행 시 임시 chunk 경합이 발생했으나 중복 프로세스 종료 후 단일 uploader 재시도로 성공했다.

2026-09-18 최신 피드백 구현: `download.php`가 공통 canonical object 경로를 사용하고 `object_not_found`, `object_size_mismatch`, `object_not_readable`, `object_read_failed`를 구분해 로그/응답하도록 보강했다. Restore는 전체 파일 PREPARE(임시 다운로드·size/SHA 검증) 후 APPLY하며, 완료 전에 `RESTORE_VERIFY expected/matched/mismatched/missing`을 출력하고 불일치 시 실패한다. Force Restore도 Restore 결과의 mismatch/missing 0을 확인한다. Setup은 새 프로젝트의 ProjectHub 전용 폴더 구조를 생성하고 루트 3개 진입점을 ProjectHub\bin 엔진으로 연결한다. PHP lint는 개발 PC에 PHP가 없어 실행하지 못했으며, 수정 download.php의 NAS 배포 후 단독 API와 Restore E2E가 남았다.

2026-09-18 download/restore 재검증 완료: NAS 실제 canonical object `S:\HDD1\ProjectHub\objects\sha256\e9\e93ac6ff...e8267`는 524,288,000 bytes였다. 수정 `download.php`를 `S:\HDD1\DocRoot\projecthub`에 배포한 뒤 assertion 단독 호출이 HTTP 200, Content-Length 524,288,000, fopen preflight `open-ok`로 응답했다. `hw\ProjectHub_Restore.cmd`는 500MiB를 다운로드하고 `RESTORE_VERIFY matched=1, mismatched=0, missing=0`으로 완료했으며, 로컬 파일 크기·SHA-256도 원본과 일치했다. 이전 HTTP 500은 NAS 웹 루트의 구버전 download.php와 readfile 처리 문제였다.
2026-09-18 Restore 다운로드 UX 보완: `ProjectHub_Restore.ps1`을 HttpClient 스트림 수신 방식으로 변경해 업로드와 같은 콘솔에서 `DOWNLOAD`, 퍼센트, 수신/전체 바이트, 완료 로그를 표시한다. PREPARE/APPLY 검증과 완료 후 `pause`는 유지한다.
2026-09-18 Force Restore UX/배포 구조 보완: `ProjectHub_Force_Restore.ps1`의 파괴적 실행 승인을 콘솔 `FORCE` 문자열 입력에서 Windows 확인 대화상자의 `계속`/`취소` 선택으로 변경했다. 새 `ProjectHub\bin` 구조를 우선 사용하고 구형 루트 `bin`은 호환 fallback으로 유지한다.

2026-09-18 문서 정정 및 `hw` 최신화: `hw`에서 구형 `.projecthub\`, 루트 `bin\`, 구형 Setup/Sync/Restore 진입점을 제거하고 `ProjectHub\bin`, `ProjectHub\config`, `ProjectHub\state`, `ProjectHub\log` 구조로 통합했다. 루트 사용자 진입점은 `ProjectHub_Commit_Push.cmd`, `ProjectHub_Fetch_Pull.cmd`, `ProjectHub_Force_Restore.cmd`만 유지했다. 500MiB `forUpload.z01`은 `.gitignore`로 커밋에서 제외하고 로컬 테스트 파일로 보존했다. `ProjectHub_Commit_Push.cmd` 실행 결과 커밋 `8dd5c80081929da8957d32938ea5aa58c064f252`를 `origin/main`에 push했고, 후속 Sync/uploader는 `checkpointed=true`로 완료했다.

문서 불일치 정정: 이전 기록의 `FORCE` 콘솔 입력, `hw\ProjectHub_Restore.cmd` 실행, 구형 `.projecthub/bin` 구조, Restore 미완료 표시는 변경 전 상태를 기록한 이력이다. 현재 Force Restore는 GUI `계속`/`취소` 승인 창을 사용하고 Restore는 `ProjectHub\bin\ProjectHub_Restore.ps1` 엔진을 사용한다. 실제 남은 검증은 Force Restore GUI 승인 창의 계속/취소 동작 확인 1건이다. Commit_Push 루트 CMD는 종료 전 `pause`를 수행하지만, 대용량 uploader는 별도 프로세스로 실행되므로 그 창의 Enter 대기 여부는 별도 개선·검증 항목으로 분리한다.

Gateway URL 변경 확인: `https://dfblackbox-nas.duckdns.org:8443/projecthub/` health `200` 및 JSON 응답 성공, `provision.php` GET은 `405`로 method 경계가 정상이다. 인증 없는 POST 응답 본문 확인은 PowerShell의 예외 응답 형식 차이로 별도 Gateway 클라이언트 검증에서 수행한다.

NAS upload 구현: `nas-gateway/upload-start.php`, `upload-chunk.php`, `upload-status.php`, `upload-finalize.php`를 추가했다. 로컬 PHP 파일은 실제 NAS 배포 후 운영 assertion으로 검증해야 하며, 현재 원격 upload 경로는 아직 배포되지 않아 HTML 응답을 반환한다.

NAS 배포 후 재검증: health `200`, 기존 `provision.php` GET `405`, upload-start GET `405`, upload-status GET 및 upload-start POST(Authorization 없음) `401 upload_session_required`를 확인했다. 운영 Server `https://projecthub.ornithopter.bid`는 같은 시각 `/api/status`와 assertion 발급 모두 Cloudflare `502`였으므로 운영 assertion 기반 upload E2E는 Server 복구 후 재개한다.

Server 재기동 후 재검증: `/api/status`는 `200`으로 복구됐으나 운영 assertion 발급은 `503 PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM is not configured`로 실패했다. NAS Gateway까지의 인증 upload E2E는 Server PC에 private key 환경 변수를 설정하고 재기동한 뒤 재개한다.

Server 실행 스크립트 `ProjectHub_Server_Test.ps1`를 추가했다. `C:\AI-Server\ProjectHub\src\ProjectHub.Server\projecthub-private.pem`을 `GetContent -Raw`와 동일한 `[IO.File]::ReadAllText()` 방식으로 읽어 PEM 개행을 보존하고, `PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM` process 환경 변수로만 주입한다.

NAS upload 최종 재검증: 운영 Server assertion 발급 성공 후 NAS `upload-start.php` → `upload-chunk.php` → `upload-status.php` → `upload-finalize.php`를 실제 실행했다. 11바이트 테스트 객체에서 chunk 수신 `11`, status `completed_chunks=[0]`, finalize `complete`, SHA-256 object 생성과 동일 hash 재업로드 `already_present=true` dedup을 확인했다.

새 Server 세션 재검증: `/api/status=ok`, 운영 assertion 발급 성공, 17바이트 객체에 대해 upload-start → chunk(`17`) → status(`bytes_received=17`) → finalize(`complete`)와 SHA-256/size 일치를 확인했다.

500MiB 실제 파일 `forUpload.z01` 검증: SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, 16MiB chunk 32개가 NAS staging에 정확히 수신되어 `bytes_received=524288000`을 확인했다. NAS finalize는 `size_mismatch`를 반환해 object 확정이 보류됐고, finalize 직후 `clearstatcache`를 추가해 보정했다. 보정 파일 재배포 후 동일 세션 finalize를 재시도한다.

500MiB 최종 재검증: 기존 session에 새 assertion으로 finalize를 재시도한 뒤 session은 정리됐고, 동일 hash `upload-start`에서 `already_present=true`, `size_bytes=524288000`을 확인했다. object 확정 및 실제 파일 크기 검증이 완료됐으며, hash는 finalize 대상 경로와 원본 SHA-256이 일치한다.

06 후속 연결 구현: Agent startup 대용량 inventory를 Server reconciliation API로 전송하고, Server에 STAGED metadata 및 명시적 commit SHA 기반 CHECKPOINTED dataset API를 추가했다. Git 자동 변경은 수행하지 않는다.

06 한계 기록(2026-09-17): `Sync`는 현재 존재하는 대용량 파일만 manifest로 수집하며 이전 manifest와 비교해 로컬에서 삭제된 파일을 NAS/Supabase 삭제 대상으로 추적하지 않는다. `ProjectHub_GC.ps1`는 TTL/lifecycle 기준의 upload session staging 정리만 담당하고 canonical NAS object 및 `large_objects`·`project_large_files`·`large_data_sets` metadata를 자동 삭제하지 않는다. NAS-only orphan은 Server API로 열거하지 못하므로 관리페이지/별도 관리 절차가 필요하다.

## 2026-09-19 Worker-A UI skeleton

최신 `GPT-Web-Feedback.md`의 Worker-A 지시에 따라 `src/ProjectHub.Worker` WPF 데스크톱 프로젝트를 추가했다. 제공된 Worker 대시보드 이미지를 기준으로 1024x768 메인 화면, Header/model/reasoning selector, Project/GPT Web/Server/System 상태 카드, 통합 Current Task/Task Flow, Codex/GPT Web 결과 탭, 자연어 Command 입력, Clear/Run Task 버튼, Windows tray 숨김/Open/Pause/Exit 기본 동작을 구현했다. Run Task와 결과 전환은 현재 mock 상태이며 Codex CLI, persistent state, Git, GPTWeb-Hub bridge는 후속 Worker-B~E 범위다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

잔여 작업: Worker-B — Codex CLI 자동 탐색·실행·결과 수집.

2026-09-19 Worker-A UI refinement: 메인 창을 세로 980px 기준으로 확장하고 Last Result/Command 영역의 사용 공간을 늘렸다. Header의 Settings/Minimize/Close/READY 배치를 분리해 겹침을 제거했고, Model/Reasoning 선택 컨트롤의 크기·색·여백을 정리했다. Current Task의 방향 문구, 상태 설명, Codex/Worker/GPT Web 단계 아이콘을 확대했다. 샘플 명령/최근 명령 버튼과 하단 설명은 제거했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 2: CURRENT TASK 행 높이를 줄이고 LAST RESULT 영역을 확장했다. Codex/GPT Web 토글 버튼과 결과 제목·시간·본문의 글자 크기를 키우고 결과 카드 여백을 확대했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0). XAML 변경 범위이므로 기존 전체 테스트 통과 상태를 유지한다.

2026-09-19 Worker-A UI refinement 3: 실제 실행 화면에서 Run/Clear 버튼이 보이도록 창 높이를 1120px, Command/Last Result 행을 각각 300px로 확장했다. Windows 기본 제목 표시줄과 중복되던 사용자 정의 최소화/닫기 버튼을 제거하고 Settings 아이콘과 READY 표시를 별도 열에 배치했다. Model/Reasoning 콤보박스의 화살표 토글을 수직·수평 중앙 정렬했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

잔여 작업: Worker-B — Codex CLI 자동 탐색·실행·결과 수집.

2026-09-19 Worker-A UI refinement 4: 실행 화면에서 Command 하단 Run/Clear 버튼이 완전히 보이도록 창 높이를 1260px, Command 행을 340px로 확장했다. 설정 아이콘 오른쪽의 헤더 READY 표시와 사용하지 않는 FooterStatus 상태 표시·갱신 로직을 제거했고, 연결되지 않은 상태/선택 핸들러도 정리했다. 트레이 Exit에 필요한 종료 상태만 유지했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

잔여 작업: Worker-B — Codex CLI 자동 탐색·실행·결과 수집.

2026-09-19 Worker-A UI refinement 5: 첨부 실행 화면 기준으로 상단 헤더 행을 76px, 연결 상태 카드 행을 122px로 축소해 카드 아래의 남는 공간을 줄였다. 하단 Command 공간은 유지했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 6: 사용자 의도에 맞춰 헤더 행은 이전 86px로 복구하고 연결 카드 행 축소 122px만 유지했다. 카드 내부 경로·상태 문구가 잘리지 않도록 창 폭을 1200px, 최소 폭을 1100px로 확장했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 7: Worker 헤더의 PC명을 `SUHO_DEV_PC`로 변경하고 실행 상태 설명 문구를 제거했다. 연결 카드에서 System 카드를 삭제하고 3열로 변경했다. 첫 카드는 Codex / `CODEX - MCP-with-MiniPC`, 두 번째는 GPT Web / `GPT Web - MCP 프로젝트 진척도 확인`, 세 번째 서버 카드는 `MINIPC_SERVER` 닉네임을 표시한다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 8: 연결 카드의 두 번째 줄에서 제목 중복을 제거하고 본문만 표시하도록 변경했다. Model/Reasoning 선택기를 상단에서 제거해 Command 하단 우측, Clear/Run 버튼 왼쪽에 여백을 두고 배치했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 9: Command 하단의 Model/Reasoning 선택기와 Clear 버튼 사이 간격만 추가 조정했다. Reasoning 열을 152px로 확보하고 오른쪽 여백을 16px로 설정해 버튼과 시각적으로 분리했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A icon refinement: 제공된 `worker아이콘.png`를 기반으로 128px 헤더용 PNG와 256px PNG-embedded ICO를 생성했다. WPF 헤더 이미지, Windows 실행 파일 ApplicationIcon, tray NotifyIcon에 연결했으며 원본은 프로젝트 밖에 보존했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A Current Task icon refinement: 사용자 지정 매핑에 따라 CODEX에는 OpenAI 아이콘, WORKER에는 콘솔 아이콘, GPT WEB에는 웹 아이콘을 96px 투명 PNG로 리사이징해 적용했다. 각 아이콘은 66px 원형 단계 표시 안에 배치했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A Current Task flow refinement: 단계 사이 진행 표시를 `>>>` 화살표로 교체하고 우측 이동·점멸 애니메이션을 추가했다. CODEX/WORKER/GPT WEB은 컬러·그레이스케일 아이콘을 겹쳐 비활성 단계는 회색으로 표시하고, mock task 실행 시 Codex → Worker → GPT Web 순으로 활성 아이콘이 전환되도록 구현했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-B 1차: `CodexCliRunner`를 추가해 PATH 및 `%LOCALAPPDATA%\OpenAI\Codex\bin` 하위에서 `codex.exe`를 자동 탐색하고, 선택 모델·추론값을 적용한 `codex exec --json`을 비동기로 실행하도록 연결했다. stdout/stderr, exit code, 실행 시간, output-last-message 결과를 수집하며 Run 버튼은 실행 중 Cancel로 전환되고 취소 시 프로세스 트리를 종료한다. Codex 결과는 Last Result의 Codex 탭과 Current Task 상태에 반영한다. GPT Web bridge는 Worker-D 전까지 mock 유지.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개). 로컬 Codex CLI `codex-cli 0.155.0-alpha.9.2` 및 `codex exec --help` 확인.

잔여 작업: Worker-B 2차 — JSON event/token usage/session id 파싱 강화 및 실제 CLI 실행 smoke test.

2026-09-19 Worker-A Current Task animation refinement: 진행 화살표를 개별 TextBlock 3개로 분리하고 150ms 타이머로 `>.. → >>. → >>> → .>> → ..>` 5단계 순차 점등을 구현했다. 활성 흐름만 컬러로 애니메이션하며 비활성 흐름은 회색 고정이고, Window 종료 시 타이머를 중지한다.

검증: `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore -p:OutDir=C:\Users\ornit\AppData\Local\Temp\projecthub-worker-build\ -p:UseAppHost=false` 성공(경고 0, 오류 0). 전체 솔루션 빌드는 실행 중인 `ProjectHub.Worker (PID 43396)`가 기존 DLL을 잠가 실패했으며, 코드 컴파일 오류는 확인되지 않았다.

2026-09-19 Worker 단일 인스턴스 개선: named mutex로 Worker 중복 실행을 차단하고, 두 번째 실행 요청이 들어오면 기존 프로세스에 named event를 보내 기존 창을 복원·활성화하도록 변경했다. StartupUri를 명시적 창 생성으로 전환해 시작 순서를 제어했다.

검증: `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore -p:OutDir=C:\Users\ornit\AppData\Local\Temp\projecthub-worker-build-single-instance\ -p:UseAppHost=false` 성공(경고 0, 오류 0).

2026-09-19 작업 범위 재분석 및 중지 정책 정정: 기존 잔여 작업에 적힌 `Force Restore GUI 계속/취소 검증`은 현재 GPTWeb-Hub Worker 작업과 무관하므로 이번 작업의 잔여 항목에서 제외한다. 잔여 작업 3번은 `현재 실행 중인 작업 중지` 기능이며, 이는 개발 전체를 중단한다는 뜻이 아니다. 실행 중인 작업을 중지할 때는 이미 진행된 변경을 억지로 되돌리지 않는다. 작업 시작 전 저장소가 최신 동기화 상태가 아니면 이전 작업이 삭제될 수 있으므로, 새 작업 시작 전 최신 pull/rebase와 `GPT-Web-Feedback.md` 확인을 우선한다.

현재 잔여 작업 재정의:
- Worker-B 2차는 최소 범위로 유지한다: Codex CLI 1회 실제 실행 smoke test와 성공/실패/취소 결과 확인만 수행한다. JSON event/token usage/session ID 확장은 후속 선택 사항으로 둔다.
- GPT Web bridge는 Worker-D 범위이며 현재는 mock 유지한다.
- 현재 실행 중인 작업을 중지해도 이미 진행된 변경은 유지하며 억지로 revert하지 않는다. 개발 작업 자체는 계속한다.
2026-09-19 Worker-B 최소 smoke test 및 현재 작업 중지 검증: 로컬 `codex-cli 0.155.0-alpha.9.2`를 프로젝트 루트에서 파일 변경 금지 프롬프트로 1회 실행했다. `EXIT_CODE=0`, `SMOKE_OK`를 확인했다. 현재 작업 중지는 Run 버튼의 Cancel 전환 → CancellationToken 취소 → Codex 프로세스 트리 종료 → `Codex CANCELED` 표시 흐름으로 동작하며, 완료된 변경을 되돌리는 로직은 없다. Worker 단독 컴파일도 경고 0/오류 0으로 통과했다.

현재 남은 작업: GPT Web bridge(Worker-D 후속 범위). 현재 실행 중인 작업 중지 기능은 구현·코드 검증 완료 상태이며 실제 UI 클릭 검증은 별도 확인 사항이다.

2026-09-19 Worker-D bridge 1차: Worker에 `127.0.0.1:43821` loopback HTTP bridge를 추가했다. status/project list/conversation binding/pending task/create/claim/result/heartbeat API를 제공하고 `%LOCALAPPDATA%\ProjectHub\Worker\bridge-state.json`에 binding·task 상태를 원자적으로 저장한다. Chrome 확장은 1.5초 polling으로 bridge 상태·프로젝트·task를 표시하며, bridge가 끊기면 Disconnected 상태를 표시한다. 외부 LAN bind와 ChatGPT DOM 자동입력은 구현하지 않았다.

검증: `node --check extension/gptweb-hub/content.js` 성공, Worker 단독 빌드 성공(경고 0, 오류 0). Worker DLL에서 bridge를 별도 호스트로 실행해 `/bridge/status`, `/bridge/projects`, `/bridge/bind`, `/bridge/bindings/{conversationId}`를 호출했고 `BRIDGE=ready`, `LOOPBACK=True`, `PROJECT=MCP-with-MiniPC`, `BOUND=True`를 확인했다.

현재 남은 작업: Chrome에서 확장을 실제 로드한 뒤 polling 화면을 확인하는 UI E2E 1건. GPT Web DOM 자동입력·응답 제출은 별도 후속 범위.

2026-09-19 GPTWeb-Hub 확장 상태 행 정리: Project/Worker/Web 상태 텍스트가 실제 연결 상태를 표시하므로 우측 `READY` 점·문구를 제거했다. `node --check extension/gptweb-hub/content.js` 통과.

2026-09-19 GPTWeb-Hub 상태 색상 개선: 확장 상태 텍스트에 정상(`status-ok`, 녹색), 연결 끊김(`status-offline`, 적색), 대기(`status-pending`, 주황색) 스타일을 추가하고 bridge polling 결과에 따라 자동 갱신하도록 변경했다. `node --check extension/gptweb-hub/content.js` 통과.

2026-09-19 확장 bridge 진단 보완: bridge 연결 전에도 Project/Worker는 주황색 대기, Web은 빨간색 연결 끊김으로 즉시 표시하도록 초기 상태 색상 적용을 추가했다. `node --check extension/gptweb-hub/content.js` 통과. 확장 reload만으로 기존 ChatGPT 탭의 content script가 교체되지 않으므로 탭 새로고침이 필요하다.

2026-09-19 상태 색상 미적용 원인 수정: `setStatusValue`는 정상 동작했지만 `statusRow` 생성부의 실제 span에 `status-value` class가 누락되어 CSS가 적용되지 않았다. 해당 class를 추가하고 `node --check extension/gptweb-hub/content.js`를 통과했다.

2026-09-19 Worker 실행 문제 수정: 사용자 세션에 남아 있던 구버전 PID 8812를 종료하고 최신 소스로 실제 `bin\Debug\net9.0-windows`를 재빌드했다. 최신 Worker PID 21844가 `ProjectHub Worker` 창으로 응답하며 `127.0.0.1:43821/bridge/status`에서 `bridge=ready`, `loopback=true`를 확인했다. bridge 시작 실패가 UI 전체 종료로 이어지지 않도록 App 시작 예외를 기록하고 Worker UI는 계속 표시하는 보호 로직을 추가했다.

검증: `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore` 성공(경고 0, 오류 0), localhost bridge status 성공.

2026-09-19 Worker 재실행 문제 수정: 첫 실행 후 창이 사라져도 activation 대기 Task가 무기한 `WaitOne()`에 남아 숨은 프로세스가 종료되지 않는 경로를 확인했다. activation event를 250ms timeout polling으로 바꿔 CancellationToken을 확인하고, 종료 시 대기 루프가 남지 않도록 수정했다. 기존 숨은 PID 21844를 종료한 뒤 최신 빌드로 1차 실행 및 즉시 2차 실행을 수행했으며 두 번 모두 동일 PID 단일 인스턴스와 `bridge=ready`, `loopback=true`를 확인했다.

검증: `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore` 성공(경고 0, 오류 0), 1차·2차 실행 bridge status 성공, 최신 Worker 창 제목 `ProjectHub Worker` 확인.

2026-09-19 Worker 트레이 재실행 흐름 정정: X 버튼은 실제 종료하지 않고 `Hide()`로 트레이 상태를 유지하도록 복원했다. 기존 백그라운드 대기 Task 대신 WPF `DispatcherTimer`가 250ms마다 named activation event를 확인해 숨은 창을 복원·활성화한다. 트레이 `Exit`에서만 `_allowClose`를 통해 bridge·tray icon·mutex를 정리한다.

검증: 최신 Worker 빌드 성공(경고 0/오류 0). 1차 실행에서 창 handle `790082`와 bridge ready를 확인하고 X 버튼으로 숨긴 뒤에도 동일 PID `45672`와 bridge ready를 확인했다. 2차 실행 후 동일 PID의 창 handle `790082`가 복원되고 bridge ready가 유지됐다.

2026-09-19 3초/10초 재실행 시나리오 및 트레이 종료 보강: 강제 종료 방식의 `3초 실행 → 종료 → 10초 대기 → 재실행`에서는 기존 문제를 재현하지 못했고 2차 bridge도 정상 확인했다. 실제 트레이 Exit 경로의 종료 보장을 위해 `Application.Current.Shutdown()`을 사용하고, `OnExit`에서 bridge/activation event 정리 실패가 mutex 해제를 건너뛰지 않도록 finally 정리를 추가했다. X 버튼의 트레이 숨김 동작과 재실행 복원은 유지한다.

검증: Worker 빌드 성공(경고 0/오류 0), 최신 Worker PID 25196 창 표시 및 bridge `ready` 확인.

2026-09-19 Worker 실제 shutdown 재검증 및 잔류 프로세스 수정: 강제 종료가 아닌 WPF `Application.Shutdown()` 경로에서 브리지는 닫히지만 PID가 남는 결함을 재현했다. 원인은 X 버튼의 트레이화 Closing 처리와 실제 종료 요청이 같은 경로에서 취소될 수 있었던 점과 Windows Forms 트레이 자원 종료 보장이 부족했던 점이다. App에 명시적 `RequestShutdown()` 상태를 두고 트레이 Exit가 이를 사용하도록 했으며, 종료 중 Closing은 숨김으로 취소하지 않도록 변경하고 ContextMenuStrip/NotifyIcon을 정리한 뒤 프로세스 종료를 보장한다. 테스트 전용 shutdown 옵션과 분리 출력 폴더는 제거했다.

검증: 분리 출력에서 최신 수정본으로 `3초 실행 → 실제 shutdown → 4초 후 확인` 수행 결과 `BRIDGE_EARLY=200`, `LATE=False`, `BRIDGE_LATE=UNREACHABLE`. 최종 일반 빌드 `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore` 성공(경고 0, 오류 0).

2026-09-19 GPTWeb-Hub 상태 행 명칭 정정: 세 번째 행은 웹 연결 대상명이 아니라 현재 연결 상태를 표시하므로 라벨을 `Web`에서 `Status`로 변경했다. 상태값과 녹색/적색 색상 갱신 로직은 유지했다.

검증: `node --check extension/gptweb-hub/content.js` 성공.

2026-09-19 GPTWeb-Hub 상태 색상 의미 정정: Project/Worker는 bridge에서 실제 값이 지정된 경우에만 녹색으로 표시하고, 아직 값이 없을 때만 대기 상태를 표시하도록 변경했다. bridge 연결이 끊겨도 마지막으로 확인된 Project/Worker 값과 녹색 상태는 유지하며, Status 행만 빨간색 Disconnected로 전환한다.

검증: node --check extension/gptweb-hub/content.js 성공.

2026-09-19 최신 GPT-Web-Feedback Settings 기능 구현: 원격 피드백을 fetch/pull --rebase로 동기화한 뒤, 확장 톱니바퀴의 mock 상태 순환을 제거하고 실제 Settings modal을 추가했다. Host/Port/BasePath/선택적 Worker Path 입력, loopback 검증, chrome.storage.local 저장·복원, Test Connection, Save 즉시 polling URL 전환, Cancel을 구현했다. Project/Worker/Status 상태는 저장된 bridge 설정을 사용한다. Worker bridge task endpoint는 최신 task와 terminal 상태를 반환하고 확장 CURRENT REQUEST는 실제 PENDING/CLAIMED/COMPLETED/FAILED 상태에서 파생된다.

검증: node --check extension/gptweb-hub/content.js 성공, dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore 성공(경고 0, 오류 0).

2026-09-19 GPTWeb-Hub SPA navigation refresh 보완: 전체 페이지 새로고침이 아닌 ChatGPT 대화 이동은 content script가 재실행되지 않아 패널과 CURRENT REQUEST가 유지되는 문제를 확인했다. history.pushState/replaceState, popstate, hashchange를 감지해 URL 변경 시 task 상태를 IDLE로 초기화하고 Worker bridge를 즉시 재조회하도록 보완했다. 대화별 binding 식별·복원은 아직 후속 범위다.

검증: node --check extension/gptweb-hub/content.js 성공.

2026-09-19 Conversation binding 구현: ChatGPT URL의 conversation ID를 식별하고 Worker의 binding 조회/저장 API와 연결했다. Project가 연결되지 않은 대화는 Project 행에 연결 버튼을 표시하며, 현재 Worker project를 POST /bridge/bind로 저장한다. 페이지 이동·새로고침 후 binding을 다시 조회해 연결된 Project를 녹색으로 복원한다. task 조회에도 conversationId 필터를 적용해 다른 대화의 task가 섞이지 않도록 했다.

검증: node --check extension/gptweb-hub/content.js 성공, dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore 성공(경고 0, 오류 0), git diff --check 통과.


2026-09-19 GPTWeb-Hub 표현·연동 의미 정리: 확장 첫 상태 행을 Project에서 GPT Web으로 변경하고 현재 ChatGPT 대화 제목을 표시하도록 했다. 기존 대화-Worker 프로젝트 바인딩은 GPT Web 행의 연결 동작으로 유지했다. Worker 행은 bridge의 repository 값(저장소명)을 표시하고 Status 행만 bridge 연결 상태를 표시한다. Worker 헤더에는 메인 저장소명 MCP-with-MiniPC을 제목 옆에 표시하고, Codex 카드의 두 번째 줄은 실행 요청 첫 줄을 Codex 대화 제목으로 표시하도록 연결했다.

검증: node --check extension/gptweb-hub/content.js 성공, dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore 성공(경고 0, 오류 0), git diff --check 통과.


2026-09-19 GPTWeb-Hub 테스트 전송 버튼: 현재 ChatGPT 대화 ID가 식별된 경우 패널의 테스트 전송 버튼으로 Hello World! 테스트 문구니까 인사만 짧게 해줘를 실제 ChatGPT 입력창에 주입하고 전송하도록 구현했다. 대화방·입력창·전송 버튼 미검출 상태를 사용자에게 표시하며, 대화 이동 시 버튼 활성 상태를 갱신한다.

검증: node --check extension/gptweb-hub/content.js 성공.


2026-09-19 GPTWeb-Hub 대화 표시 개행: ChatGPT 페이지 제목이 프로젝트명 - 대화방명 형식이면 GPT Web 상태값을 두 줄로 표시하도록 수정했다.

검증: node --check extension/gptweb-hub/content.js 성공.


2026-09-19 GPTWeb-Hub 이미지 테스트 전송: 테스트 버튼 문구를 이미지가 무엇인지 짧게만 대답해줘로 변경하고, 확장 내부 Canvas에서 생성한 gptweb-hub-test.png를 File/DataTransfer로 ChatGPT file input 또는 paste/drop 경로에 첨부한 뒤 기존 전송 버튼으로 함께 전송하도록 구현했다. 별도 로컬 파일 권한은 사용하지 않는다.

검증: node --check extension/gptweb-hub/content.js 성공.


2026-09-19 GPTWeb-Hub 요청·수신 연동: 테스트 전송 버튼을 제거하고 CURRENT REQUEST가 IDLE일 때만 텍스트 입력, 전송, 파일 드롭을 활성화했다. 드롭 파일은 최종 파일명만 표시하며 전송 시 ChatGPT 입력창에 첨부한다. ChatGPT DOM의 최신 assistant 메시지를 MutationObserver로 감시해 하단 RESULT MESSAGE에 갱신한다. PENDING/CLAIMED/COMPLETED/FAILED 상태에서는 입력과 드롭을 비활성화한다.

검증: node --check extension/gptweb-hub/content.js 성공, git diff --check 통과.


2026-09-19 GPTWeb-Hub 요청·수신 UI 정리: 테스트 전송 버튼과 전용 이미지 생성 코드를 제거했다. CURRENT REQUEST가 IDLE이고 대화가 식별된 경우에만 텍스트 입력·전송·파일 드롭을 활성화하며, 드롭 파일은 파일명만 표시하고 일반 파일 첨부 경로로 ChatGPT에 전달한다. 전송 후 새 assistant 응답이 감지될 때까지 컨트롤을 잠그고, 최신 assistant 메시지를 RESULT MESSAGE에 갱신한다.

검증: node --check extension/gptweb-hub/content.js 성공, git diff --check 통과.

2026-09-19 GPTWeb-Hub 통합 왕복 구현: 최신 GPT-Web-Feedback.md의 통합 범위를 반영했다. Bridge task에 conversation/project 일치, PENDING 중복 방지, CLAIMED lease/시작시각, 첨부 메타데이터, TEXT_RESULT 응답 필드를 추가했다. 확장은 수동 textarea·드롭존·전송·테스트 버튼·별도 RESULT MESSAGE를 제거하고 단일 TASK 영역으로 통합했다. 바인딩된 현재 대화의 PENDING 작업만 한 번 claim한 뒤 Worker Message를 자동 입력·첨부·전송하고, assistant DOM의 신규 응답이 안정화되면 /bridge/task/{id}/result로 반환한다. CLAIMED 작업은 새로고침 후 재전송하지 않고 응답 대기 상태를 복원한다. Worker는 bridge 완료 이벤트를 받아 GPT Web 결과를 Last Result와 Current Task에 표시한다.

최종 검증 예정: node --check extension/gptweb-hub/content.js, dotnet build ProjectHub.sln --no-restore, dotnet test ProjectHub.sln --no-build --no-restore, git diff --check, Worker loopback task create→claim→result 및 중복 claim/재조회 검증. 실제 ChatGPT DOM 자동 입력·응답 완료는 브라우저 확장 로드 상태에서 별도 확인한다.

2026-09-19 Worker 모델 선택기 개선: 설치된 Codex CLI의 실제 사용 가능 모델 세트(Sol, Astra, Terra, Luna)를 조회해 Model 콤보박스 목록을 정정했다. 기본 선택을 GPT-5.6 Luna / Medium으로 변경하고, ComboBox의 화살표 영역만이 아니라 전체 영역을 ToggleButton이 받아 드롭다운이 열리도록 템플릿을 수정했다.

2026-09-19 Worker UI 상태·메시지·사용량 개선: ComboBox Popup의 ZIndex와 항목 hover 스타일을 보강해 메뉴 가림을 줄였고, ProjectHub 저장소명·실제 PC명·bridge task 상태를 Worker UI에 반영했다. Current Task에서 Round/시간/처리 문구와 ChatGPT 응답 생성 중 보조 문구를 제거했다. LAST RESULT를 MESSAGE로 변경하고 Codex/Web 결과 도착 시 해당 탭을 자동 활성화하며, 다음 결과가 올 때까지 각 메시지를 유지한다. 단순 성공 장식은 제거했다. Codex JSONL의 usage/token_usage 이벤트에서 명령별 누적 token을 파싱해 하단에 표시한다. Codex CLI는 계정 5시간/주간 제한값을 노출하지 않으므로 해당 값은 CLI 미제공으로 표시하며 임의 값은 기록하지 않는다.

검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과. 계정 사용량 조회 도구 자체는 값을 반환했지만, 이 값은 Worker 프로세스가 직접 조회할 수 있는 CLI 계약이 아니므로 UI에 주입하지 않았다.
2026-09-19 Worker preflight 및 Current Task 확장: Worker 시작 후 Codex login status, ProjectHub Server http://127.0.0.1:5240/api/status, GPT Web bridge heartbeat를 검사하고 Project/Web/Server 카드에 실제 READY/WAITING/OFFLINE 상태를 표시하도록 연결했다. Run Task 직전에도 세 조건을 다시 확인해 준비되지 않은 연동에서는 작업을 시작하지 않는다. 상태는 3초 polling으로 갱신한다. Current Task 우측 아이콘을 84px, 내부 이미지를 72px, 단계 폰트를 17px로 확대하고 단계 문구를 현재 실행 상태에 맞게 갱신한다. 검증: Worker build 성공, 전체 테스트 5개 통과, Codex login status 성공. 현재 로컬 ProjectHub Server 5240은 응답하지 않아 UI에서는 OFFLINE으로 표시된다. GPT Web은 확장 heartbeat가 들어오면 READY로 전환된다.

2026-09-19 Worker 연결상태·Current Task 표시 보완: Server preflight는 ProjectHub_Agent_Test.ps1의 계약과 동일하게 PROJECTHUB_AGENT_SERVER_BASE_URL 환경변수를 우선 사용하고, 미설정 시 https://projecthub.ornithopter.bid/api/status를 확인하도록 변경했다. 로컬 127.0.0.1 검사로 오인하지 않는다. GPT Web 상태의 WAITING은 검정, OFFLINE은 적색으로 구분하고 비활성 Current Task 아이콘 배경은 회색으로 표시한다. 우측 아이콘 영역의 폭과 여백을 조정해 아이콘·>>> 진행 표시가 잘리지 않도록 했다. GPT Web 프로젝트명과 대화방명은 다시 개행 표시한다. 초기 연결 확인값도 WAITING/CHECKING으로 맞췄다.
검증: node --check 성공, Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과.

2026-09-19 Worker Current Task 시각 상태·GPT Web heartbeat 수정: 아이콘 배경 Border보다 내부 Grid가 작아 배경이 잘리던 구조를 80px 컨테이너로 맞췄다. Codex/Worker/GPT Web 라벨과 단계 글씨를 활성 상태 색상 또는 비활성 SlateGray로 동기화했다. GPT Web 확장의 refresh에서 /bridge/heartbeat 호출이 누락되어 WAITING에 머물던 문제를 복구했다. WAITING은 Worker가 heartbeat를 받기 전 상태이며, heartbeat 수신 후 WebConnected=true가 된다.
검증: 최신 Worker 실행 후 /bridge/status bridge=ready, heartbeat 응답 ok=true, heartbeat 직후 webConnected=true. node --check 성공, Worker build 성공(경고 0/오류 0), git diff --check 통과.

2026-09-19 Worker 상태 카드 점 표시 수정: GPT Web WAITING 상태에서 XAML 상태 점이 녹색으로 고정되어 있던 문제를 수정했다. Project/Web/Server 상태 점을 코드로 관리해 READY는 녹색, WAITING은 회색, OFFLINE 또는 인증 필요 상태는 적색으로 표시한다.
검증: Worker build 성공(경고 0/오류 0), extension node --check 성공, git diff --check 통과.

2026-09-19 GPT Web 즉시 연동 재검증: Worker bridge는 확장 heartbeat 수신 시 webConnected=true로 전환되고 10초간 heartbeat가 없으면 false로 복귀한다. 실제 실행 Worker에서 수동 heartbeat 직후 true, 2초 후 heartbeat 미갱신 상태에서 false를 확인해 WAITING 판정 자체는 정상임을 검증했다. 확장이 localhost bridge를 호출하도록 MV3 manifest에 127.0.0.1/localhost:43821 host_permissions를 추가했다.
검증: content.js node --check 성공, manifest JSON 파싱 성공, git diff --check 통과. 실제 브라우저에서 새 manifest를 적용하려면 확장 새로고침이 필요하다.

2026-09-19 GPTWeb-Hub 제목 개행 재수정: 통합 과정에서 title()이 원문 한 줄을 반환하도록 되돌아가 프로젝트명과 대화방명이 자연 줄바꿈에 맡겨져 잘리던 문제를 확인했다. ChatGPT document.title에서 프로젝트명 - 대화방명 구분자를 분리해 명시적 개행 문자로 반환하도록 복구했다. status-value의 white-space:pre-line과 함께 두 줄 표시를 보장한다.
검증: node --check extension/gptweb-hub/content.js 성공, git diff --check 통과.

2026-09-19 Worker Codex 프로젝트·스레드 선택 구현: Codex 카드의 고정 Codex 작업 대기 중 문구를 프로젝트 콤보박스와 스레드 콤보박스로 교체했다. 실제 ~/.codex 세션 인덱스와 session_meta의 cwd/session_id/thread_name을 읽어 프로젝트별 기존 스레드를 목록화하고, 프로젝트만 선택하면 ＋ 신규 스레드를 기본 선택한다. 신규 선택은 codex exec -C 프로젝트경로, 기존 선택은 codex exec resume 스레드ID로 실행하도록 연결했다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과. 실제 Codex resume 실행은 사용자 UI에서 선택 후 명령 실행 시 확인한다.

2026-09-19 Codex 프로젝트·스레드 검증: ProjectHub 프로젝트에서 현재 스레드 resume을 시도했으나 기존 thread writer가 활성 상태라 실패했다. 절차에 따라 ProjectHub 신규 스레드에서 --image로 화면 이미지를 첨부하고 이미지를 짧게 설명하라는 1회 실행을 수행했으며 exit 0과 짧은 응답을 받았다. 다만 종료 시 Codex CLI가 신규 rollout flush 실패(thread not found) 경고를 남겼고 session_index에는 신규 thread ID가 확인되지 않아, 실행 성공과 영속 스레드 등록은 분리해서 판단해야 한다.
검증 결과: 현재 스레드 resume 실패(활성 writer), 신규 스레드 이미지 질문 실행 성공(exit 0), 이미지 응답 수신 성공, 신규 스레드 인덱스 영속화는 미확인.

2026-09-19 검증 정책 변경: 향후 검증 최우선 순위를 빌드 완료 Explorer 실행파일의 실제 화면 조작으로 지정했다. 실행파일을 실행한 뒤 화면에서 메시지를 작성·전송하고 결과 및 연동 상태를 확인한다. Explorer 화면 검증이 불가능한 경우에만 CLI·API·직접 프로세스 호출을 대체 수단으로 사용하며, 결과에 대체 검증임을 명시한다.

2026-09-19 Explorer 우선 검증 재시도: 최신 Worker 실행파일을 빌드하고 C:\AI-AGENT\ProjectHub\src\ProjectHub.Worker\bin\Debug\net9.0-windows\ProjectHub.Worker.exe를 실행했다. 화면 직접 조작을 위해 컴퓨터 제어 런타임을 호출했으나 런타임 초기화 실패로 UI 메시지 작성·전송은 수행하지 못했다. 정책에 따라 화면 성공으로 간주하지 않고 대체 검증으로 전환했다.
대체 검증 결과: Worker 프로세스 실행 중, bridge=ready, webConnected=true, git diff --check 통과. 화면 메시지 전송 검증은 미완료.

2026-09-19 Codex 선택 UI 단일 목록 개선: 프로젝트 콤보박스를 제거하고 실제 세션을 프로젝트별로 즉시 조회하는 단일 스레드 콤보박스로 변경했다. 항목은 (프로젝트명) 스레드명 또는 (프로젝트명) ＋ 신규 스레드 형식이며 선택 항목의 ProjectPath/session_id를 CLI에 사용한다. 선택 상태가 목록 위를 덮던 원인은 기본 ToggleButton 템플릿의 선택 배경이어서 전용 투명 템플릿으로 교체했다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과.

2026-09-19 Codex 스레드 목록 UX 보완: 선택값이 CodexThreadOption 객체 문자열로 표시되던 문제를 ToString override와 단일 목록 표시로 수정했다. 선택 없음 placeholder를 프로젝트 선택으로 표시하고, 콤보 목록 MaxHeight를 240px에서 960px로 확대했다. 실행파일 위치에서 상위 .git을 찾아 ProjectHub 루트를 프로젝트 목록에 포함하며, 실제 session_id와 projectPath를 %LOCALAPPDATA%/ProjectHub/worker-selection.json에 저장·다음 실행 시 복원한다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과.

2026-09-19 Codex 프로젝트·스레드 매칭 수정: 세션 파일의 모든 rollout ID를 사용자 스레드로 추가하던 오류를 제거하고 session_index.jsonl에 등록된 사용자 thread ID가 해당 프로젝트 cwd와 일치할 때만 목록에 포함하도록 변경했다. 실제 인덱스/cwd 교차검사에서 ProjectHub는 현재 사용자 스레드 1개로 확인됐다.
검증: 기존 Worker가 EXE를 잠가 첫 빌드는 실패했으나 해당 프로세스를 종료 후 최신 Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과. 최신 Worker 재실행 완료.

2026-09-19 Run Task Codex→GPT Web 전달 원인 분석 및 이미지 첨부 연결: 기존 MainWindow RunTask는 Codex CLI 결과를 MESSAGE에 표시한 뒤 종료했고, BridgeServer에 GPT Web task를 생성하는 호출이 없어 Worker→GPT Web 단계가 시작되지 않았다. Codex 성공 후 최신 GPT Web 바인딩으로 PENDING 작업을 생성하고, Worker가 만든 텍스트 이미지 PNG를 localhost 첨부 URL로 제공하도록 연결했다. 동일 대화의 PENDING/CLAIMED 작업 중복도 차단한다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, content.js node --check 성공, manifest JSON 파싱 성공, git diff --check 통과. 빌드된 Worker EXE 기동 후 /bridge/status에서 bridge=ready·webConnected=true와 최신 binding을 확인했다. Explorer 화면 자동화 런타임은 초기화 직후 종료되어 실제 화면 입력·전송 검증은 수행하지 못했으며, 정책에 따라 이를 성공으로 간주하지 않는다. Codex CLI 실행 자체와 실제 GPT Web 응답 수신은 화면 검증 불가로 미완료.

2026-09-19 GPT Web 본문 미전송 수정: 확장이 파일 첨부 직후 전송 버튼을 즉시 클릭해 업로드 처리 전 전송이 무시될 수 있었고, ChatGPT DOM 변형에 대한 입력창·전송 버튼 선택도 제한적이었다. 표시 중이고 비활성화되지 않은 composer/send button을 탐색하고, 본문 입력 반영·파일 업로드 완료·전송 가능 상태·입력창 비움을 순서대로 확인한 뒤 전송하도록 수정했다. 파일 다운로드 HTTP 오류도 명시적으로 처리한다.
검증: content.js node --check 성공, git diff --check 통과. 실제 ChatGPT 화면 전송은 화면 자동화 런타임 초기화 실패로 아직 미검증이며, 확장 새로고침 후 기존 바인딩 대화에서 재검증이 필요하다.

2026-09-19 GPT Web 채팅방 이동 동기화 수정: SPA 내 pushState/replaceState/popstate/hashchange로 대화방이 바뀌면 이전 대화 ID를 유지한 채 변경을 감지하도록 수정했다. 기존 대화가 Worker에 연결된 상태로 이동하면 새 대화에 자동 bind하고, 새 conversationId/projectId를 포함한 heartbeat를 다시 전송한다. 이동 전에 ID를 초기화하던 순서 오류를 제거했다.
검증: content.js node --check 성공, git diff --check 통과. 실제 ChatGPT SPA 이동 검증은 화면 자동화 런타임 초기화 실패로 미완료이며, 확장 새로고침 후 연결된 대화에서 다른 대화로 이동해 Worker 카드의 GPT Web 대화명이 갱신되는지 확인해야 한다.

2026-09-19 GPT Web 동기화 경쟁 상태 및 Worker 대화명 표시 수정: 채팅 이동 직후 자동 동기화 refresh와 1.5초 주기 refresh가 동시에 실행되면 주기 refresh가 새 대화를 미연결 상태로 덮어써 자동 bind가 누락될 수 있었다. refresh를 직렬화하고 queued auto-bind를 보존했다. heartbeat에 conversationId/projectId/conversationTitle을 포함하고 BridgeServer가 현재 GPT Web 대화명을 보관하며 Worker GPT Web 카드에 표시하도록 연결했다.
검증: Worker build 성공(경고 0/오류 0), content.js node --check 성공, heartbeat 응답 및 /bridge/status에서 conversationId·개행 포함 conversationTitle·projectId 반영 확인. git diff --check 통과. 실제 ChatGPT SPA 이동은 확장 새로고침 후 확인 필요.

2026-09-19 GPT Web 확장 정지 및 FINISHED 후 재수신 수정: refresh에 autoBind 인자를 추가한 뒤 setInterval(refresh, 1500)이 1500을 autoBind=true처럼 전달하던 문제를 setInterval(()=>refresh(), 1500)으로 수정했다. FINISHED 이후 activeTaskId/sentTaskId가 남아 새 PENDING 작업을 claim하지 못하던 문제는 task ID 변경을 감지해 phase·sentTaskId·baselineAssistant를 초기화하도록 수정했다. FINISHED 결과 표시는 유지하면서 다음 작업 수신을 허용한다.
검증: content.js node --check 성공, git diff --check 통과. 새 task ID 전환 로직은 정적 코드 확인까지 완료했으며 실제 ChatGPT에서 연속 2회 작업 수행은 확장 화면 자동화 런타임 문제로 미완료.

2026-09-19 Worker→GPT Web 대기 중 Run Task 상태 수정: Codex 실행이 끝난 직후 finally에서 Run Task로 복귀하던 문제를 수정했다. GPT Web task가 PENDING/CLAIMED인 동안 _awaitingWebResult를 유지하고 버튼은 Cancel 표시를 계속한다. 이 상태에서는 RunTask_Click이 새 작업을 시작하지 않는다. GPT Web task가 COMPLETED/FAILED가 될 때만 _awaitingWebResult를 해제하고 Run Task로 복구한다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, content.js node --check 성공, git diff --check 통과. 최신 실행 파일 반영을 위해 Worker 재기동이 필요하다.

2026-09-19 GPT Web 확장 연결 확인 정지 진단: 실행 중 Worker 프로세스가 없어 127.0.0.1:43821의 status/projects/task/heartbeat 요청이 모두 연결 거부되어 확장이 초기 연결 확인 상태에 머무는 조건을 재현했다. Worker 실행 파일 기동 후 브리지 응답은 정상(각 엔드포인트 1~270ms)으로 확인했다. 확장이 브리지 무응답을 무한 대기하지 않도록 모든 bridge fetch에 4초 AbortController timeout을 추가했다.
추가 확인: FINISHED 이후 새 task 재수신, 주기 refresh 인자 오류, Worker→GPT Web Cancel 상태 유지 수정도 함께 반영된 최신 코드 기준이다.
검증: Worker build 성공, 전체 테스트 5개 통과, content.js node --check 성공, git diff --check 통과. 실제 브라우저 확장 화면은 자동화 런타임 불가로 직접 조작하지 못했으며, 확장 새로고침이 필요하다.

2026-09-19 GPT Web 연결 문구 표시 수정: status/projects/task 조회가 성공하고 Status=Connected인 경우에도 COMPLETED task 분기로 조기 return하면서 초기 systemText인 연결 확인 중...이 남았다. 성공 응답을 받은 직후 정상적으로 연결되어 있습니다.로 갱신해 FINISHED/PENDING/CLAIMED 상태에서도 연결 문구가 일관되게 표시되도록 수정했다.
검증: content.js node --check 성공, git diff --check 통과. 실제 화면 반영에는 확장 새로고침이 필요하다.

2026-09-19 Worker task 전달 불일치 및 Cancel 일괄 복구 수정: 직접 브리지 상태에서 active PENDING task의 conversationId(6aac...)와 현재 GPT Web heartbeat conversationId(6aa52...)가 달라 확장이 task를 받을 수 없는 원인을 확인했다. CreateTaskForLatestBinding은 최신 저장 바인딩 대신 현재 heartbeat 대화 바인딩을 우선 사용하도록 수정했다. Worker Cancel은 Codex 실행 중에는 CLI 취소, GPT Web 대기 중에는 active task를 FAILED/canceled로 종료하고 화면·버튼·결과 탭을 초기 상태로 복구한다.
검증: 저장된 PENDING task에 CancelActiveTask를 실제 호출해 Canceled=true 및 activeTask 제거를 확인했다. Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, content.js node --check 성공, git diff --check 통과. 최신 Worker 재기동 후 bridge=ready·webConnected=true·현재 heartbeat conversationId 확인.

2026-09-19 GPT Web 비활성 탭 전송 보완: 이미지와 본문이 ChatGPT 입력창에 도착했지만 전송되지 않은 현상에서, 기존 확장은 composer만 focus하고 넓은 버튼 탐색 후 synthetic click만 실행했다. 실제 전송 버튼 selector를 우선 사용하고 버튼 focus 후 click, 입력창 Enter fallback을 추가했다. 전송 후 입력창이 비워지지 않으면 탭 active/inactive 상태를 포함한 전송 실패를 표시하고 task를 send_failed로 종료한다. 브라우저 탭을 강제로 활성화하지는 않는다.
검증: content.js node --check 성공, git diff --check 통과. 실제 비활성 ChatGPT 탭에서의 전송은 화면 자동화 런타임을 사용할 수 없어 미완료이며, 실패 시 포커스 상태가 확장에 표시되도록 변경했다.

2026-09-19 GPT Web 조기 FINISHED 및 잘린 응답 수정: 확장이 ChatGPT 스트리밍 중간 문구를 800ms 안정화만으로 최종 응답으로 오인해 Worker에 부분 응답을 전송하고 FINISHED로 전환하던 문제를 확인했다. 생성 중단/Stop 버튼이 표시되는 동안은 완료 타이머를 취소하고, 생성 종료 후 최종 응답이 2.5초 동안 변하지 않을 때만 Worker result API로 전송한다. result API 성공 후에만 확장 phase가 FINISHED가 된다.
검증: content.js node --check 성공, git diff --check 통과. 실제 ChatGPT 스트리밍 응답 E2E는 화면 자동화 런타임 문제로 미완료.

2026-09-19 GPT Web 응답 반환 정지 추가 진단: 브리지 active task가 CLAIMED 상태이고 Worker result가 없음을 확인했다. 과거 task 두 건은 ChatGPT 스트리밍 중간 문구 "개의 이미지 분석 중"으로 잘못 COMPLETED 처리됐고, 현재 task는 result 미수신 상태였다. assistantStreaming 감지를 일반 취소 텍스트가 아닌 명시적 Stop/중지 버튼 selector로 좁혔다. 현재 멈춘 task는 Cancel 처리 후 Worker 재기동으로 activeTask 제거를 확인했다.
검증: content.js node --check 성공, git diff --check 통과, Worker 재기동 후 bridge=ready·activeTask 없음 확인. 실제 ChatGPT 최종 스트리밍 E2E는 확장 화면 자동화 런타임 문제로 미완료.

2026-09-19 GPT Web 이전 응답 재사용 및 부분 응답 완료 오인 보완: 확장 새로고침 후 CLAIMED task를 복원할 때 현재 최신 assistant 메시지를 기준 응답으로 초기화하지 않아 이전 질문/답변을 새 응답으로 오인할 수 있던 경로를 수정했다. CLAIMED 복원 시 task별 sentTaskId와 baselineAssistant를 설정하고, 생성 중 판정은 Stop/중지 버튼뿐 아니라 data-is-streaming, data-streaming, aria-busy 상태와 버튼 label/testid/title을 함께 확인한다. 스트리밍 종료 안정화 대기시간을 3.5초에서 5초로 늘려 부분 응답 조기 반환 가능성을 낮췄다. Worker의 GPT Web 전달 문구는 이미지의 글자를 직접 분석하도록 정리되어 있다.

검증: node --check extension/gptweb-hub/content.js 성공, dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과. 빌드된 Worker 실행파일에서 bridge=ready·webConnected=true 확인. Explorer 화면 자동화 런타임은 재시작 후에도 초기화 직후 종료되어 실제 화면 입력·ChatGPT DOM 왕복은 미검증. 대체 로컬 bridge 검증은 task 생성→claim→result→COMPLETED 1회 성공. 잔여: 확장 새로고침 후 실제 ChatGPT에서 새 task를 실행해 최종 응답 전체가 Worker로 반환되는 화면 E2E.

2026-09-19 Codex 스레드 목록 갱신 시점 보완: 최초 Worker 시작 시 목록을 읽은 뒤 Codex 계정 인증이 성공하면 즉시 session_index.jsonl을 다시 조회하도록 연결했다. Codex CLI 실행 완료 후에는 300ms 간격으로 5회 목록을 재조회해 CLI의 session flush가 늦게 반영되는 신규 스레드도 같은 실행에서 확인하도록 보완했다. 기존 저장 선택값 복원은 유지한다.

검증: dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore 성공(경고 0/오류 0), dotnet test ProjectHub.sln --no-build --no-restore 전체 5개 통과, git diff --check 통과. 실제 Codex 신규 스레드가 session_index.jsonl에 등록되는지 여부는 CLI flush 결과에 의존한다.

2026-09-19 Codex 신규 스레드 안전 보관·복원 경로 구현: Codex JSONL의 thread.started에서 session ID를 추출해 Worker 전용 로컬 archive에 metadata.json, transcript.jsonl, handoff.md를 저장하도록 추가했다. Codex 내부 session_index.jsonl은 직접 수정하지 않는다. Worker 스레드 목록은 공식 인덱스와 로컬 archive를 합쳐 표시하고, 신규 CLI 실행 완료 후 archive/session index를 재조회하며 새 session ID를 선택 상태로 복원한다. 이후 실행은 선택된 ID를 공식 codex exec resume 경로로 사용한다.

검증: Worker 빌드 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과, 최신 Worker 재기동 후 bridge=ready·webConnected=true 확인. 실제 Codex 신규 CLI 실행으로 archive 파일 생성 및 resume 왕복은 계정 작업을 추가로 소비하므로 이번 검증에서는 수행하지 않았다.


2026-09-19 Worker -> GPT Web 고정 테스트 데이터 제거: Run Task 성공 후 GPT Web task를 생성할 때 사용하던 고정 프롬프트와 테스트 이미지를 제거했다. 이제 현재 CommandInput 명령을 그대로 Web prompt로 전달하고 Codex FinalMessage가 있으면 실행 결과를 추가 문맥으로 전달한다. 첨부물도 고정 문자열 대신 현재 Codex 결과(결과가 없으면 현재 명령)로 생성한다. MainWindow 초기 MESSAGE 샘플 제목·시간·본문도 중립적인 대기 문구로 교체했다. 추가 검색에서 사용자 콘텐츠를 고정하는 다른 Worker/Extension 코드는 발견되지 않았으며 테스트 fixture의 sample 문자열과 일반 상태 라벨은 유지했다.
검증: node --check extension/gptweb-hub/content.js, dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore, dotnet test ProjectHub.sln --no-build --no-restore, git diff --check. Explorer 화면 자동화는 런타임 초기화 오류로 수행할 수 없어 대체 검증으로 기록한다.

2026-09-19 Web 응답 → Codex CLI 후속 처리 추가: Bridge task가 COMPLETED가 되면 Worker가 화면 갱신만 하고 종료하던 누락을 수정했다. 최초 실행의 project path, session ID, model, reasoning을 보존하고 Web response를 같은 Codex 세션의 후속 prompt로 실행한다. 후속 Codex 결과는 Web task를 재생성하지 않고 최종 MESSAGE에 Web 응답과 Codex 후속 결과를 함께 표시한다. 후속 CLI 실행 중에도 Cancel을 유지한다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, extension node --check 통과, git diff --check 통과. Explorer 화면 E2E는 자동화 런타임 제한으로 수행하지 않았다.

2026-09-19 새 Codex 스레드 선택 복원 수정: CLI가 새 session ID를 반환한 뒤 RefreshCodexSelectionsAfterCliAsync가 해당 ID를 영구 선택 상태에 저장하도록 변경했다. 기존에는 저장된 기본 스레드를 다시 읽어 새 스레드 실행 직후 기본 설정으로 되돌아갔다. 선택 목록이 늦게 갱신되어도 5회 재조회 중 새 session이 나타나면 저장된 선택으로 복원된다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과.

2026-09-19 Extension 상태와 Worker 전체 완료 분리: Extension의 Web task FINISHED는 Worker 전달 완료 의미로 유지한다. Worker가 Web 응답을 Codex CLI에 전달한 뒤 Codex 결과를 검사하고, 응답에 진행 카운터 N/M이 있고 N<M이면 다음 GPT Web task를 Worker가 생성한다. [WORKER_DONE] 또는 진행 조건 부재 시 Worker가 최종 종료한다. 다음 task 생성 후 followup finally가 상태를 초기화하던 문제도 수정했다. Extension 코드는 변경하지 않았다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, extension node --check 통과.

2026-09-19 Worker 이미지 첨부 조건 수정: Worker가 Codex 결과를 항상 PNG로 만들어 Web task에 첨부하던 문제를 수정했다. 원래 명령에 텍스트만·이미지 생성 금지·이미지 첨부 금지 의미가 있으면 attachment를 빈 목록으로 전달하고, 그 외 이미지 분석 작업에는 기존 동적 첨부를 유지한다. 초기 Web 전달과 Worker가 생성하는 다음 순환 task 모두 같은 조건을 적용했다. Extension은 변경하지 않았다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, extension node --check 통과.

2026-09-19 원격 GPT-Web-Feedback ACTION protocol v1 반영: 원격 main의 최신 피드백을 fetch 후 읽었다. 로컬 변경 때문에 pull --rebase는 dirty worktree에서 중단되었으며 stash/reset 없이 원격 문서를 기준으로 구현했다. Worker는 사용자가 입력한 [ACTION=BEGIN]의 본문을 최초 Codex 명령으로 전달하고, ACTION job에서는 Web 응답의 첫 유효 제어행을 파싱한다. CONTINUE 본문만 다음 Codex 명령으로 전달하며, PAUSE/END는 Codex에 전달하지 않고 각각 FINISH_PAUSED/FINISH_SUCCESS로 종료한다. ACTION 누락·중복·알 수 없는 값·빈 본문·진행 중 BEGIN은 FINISH_PROTOCOL_ERROR로 처리한다. Codex 결과를 Web에 재전달할 때 ACTION 프로토콜 설명을 Worker prompt에 추가하고, taskId 중복 소비를 차단하며 round 상한 30을 적용했다. Extension은 transport 역할만 유지하고 수정하지 않았다.
검증: Worker build 성공(경고 0/오류 0), 전체 테스트 5개 통과, extension node --check 통과.
## 2026-09-19 ACTION 프로토콜 프롬프트 중복 전송 보완

- 초기 [ACTION=BEGIN]의 CLI 명령은 최초 Web 전달에만 원래 작업으로 포함한다.
- 이후 ACTION 반복 라운드에서는 원래 COMMAND를 다시 붙이지 않고 Codex 결과와 프로토콜 안내만 전달한다.
- CLI 결과를 Web에 전달할 때 요청된 [ACTION=CONTINUE], [ACTION=PAUSE], [ACTION=END] 선택 안내를 항상 프롬프트 첫 부분에 포함한다.
- 검증: Worker 별도 출력 경로 빌드 성공. 기존 테스트와 확장 문법 검사는 앞선 검증에서 통과.

## 2026-09-19 ACTION 접두문 누락 보완

- 일반 명령도 CLI 결과를 GPT Web에 전달할 때 ACTION 프로토콜 안내를 거치도록 초기·반복 Web 프롬프트 경로를 통일했다.
- 초기 전달에만 원래 COMMAND를 포함하고, 반복 라운드에는 원래 COMMAND를 재전송하지 않는다.
- 검증: Worker 별도 출력 경로 빌드 성공(경고 0, 오류 0), 확장 JavaScript 문법 검사 성공.

## 2026-09-19 전체 재빌드

- 실행 중인 ProjectHub Worker 프로세스는 확인되지 않았다.
- ProjectHub.sln을 Debug 구성으로 Clean 후 전체 Build했다.
- Worker 실행파일이 src/ProjectHub.Worker/bin/Debug/net9.0-windows/ProjectHub.Worker.exe에 현재 시각으로 재생성되었다.
- 검증: Core 1개, Agent 3개, Server 1개 테스트 통과; 확장 JavaScript 문법 검사 통과.

## 2026-09-19 CONTINUE 응답 판별 보완

- Web 응답의 첫 번째 유효행만 대상으로 Contains 방식으로 CONTINUE/PAUSE/END/BEGIN을 판별한다.
- Web 응답이 비어 있거나 첫 행에 ACTION이 없으면 PAUSE로 처리한다.
- 실제 CRLF/LF 개행을 기준으로 응답 본문을 분리한다.
- 초기 사용자 명령 파싱은 기존 일반 명령 동작을 유지하고, Web 응답에만 기본 PAUSE 정책을 적용한다.
- 검증: ProjectHub.sln Debug 빌드 성공(경고 0, 오류 0), 테스트 5개 통과, 확장 JavaScript 문법 검사 통과. 최신 Worker 재기동 완료.

## 2026-09-19 일반 COMMAND ACTION 반복 중단 수정

- 원인: 일반 명령은 _actionProtocolEnabled가 false여서 첫 Web 응답만 후속 처리하고 CONTINUE 반복 루프에 진입하지 않았다.
- 수정: 모든 작업에서 ACTION 반복 모드를 활성화하여 일반 명령도 Web의 CONTINUE/PAUSE/END를 처리한다.
- 검증: 전체 Debug 빌드 성공(경고 0, 오류 0), 테스트 5개 통과, 확장 JavaScript 문법 검사 통과. 최신 Worker 재기동 완료.

## 2026-09-19 Current Task·토큰·메시지 내보내기

- Current Task 좌측 텍스트 영역을 확장하고 긴 상태 문구 줄바꿈을 허용했다.
- CLI usage를 중첩 usage/token_usage, snake_case/camelCase, total 누락 계산까지 처리하고 사이클별 누적값을 표시한다.
- 하나의 Task에서 USER COMMAND, CODEX, WORKER, GPT WEB 메시지를 순서대로 누적한다.
- 정상 종료 시 실행 폴더의 Task/프로젝트_스레드/_yyyymmdd_hhmmss.txt로 UTF-8 transcript를 내보낸다.
- 검증: 전체 Debug 빌드 성공(경고 0, 오류 0), 테스트 5개 통과, 확장 JavaScript 문법 검사 통과. 최신 Worker 재기동 완료.

## 2026-09-19 Web 프롬프트 불필요 문구 제거

- ACTION 선택 안내는 유지하고, 첫 유효행·본문 역할·ACTION 오류를 설명하던 중복 문구는 BuildWebPrompt에서 제거했다.
- Task transcript 저장 기능은 유지한다.
- 검증: 전체 Debug 빌드 성공, 테스트 5개 통과, 확장 JavaScript 문법 검사 통과. 최신 Worker 재기동 완료.

## 2026-09-19 최신 구현 상태 및 문제점

- Worker는 일반 명령을 포함한 모든 Task에서 Web ACTION 반복을 처리한다.
- Web 응답은 첫 번째 유효행의 CONTINUE/PAUSE/END/BEGIN을 판별하며, 빈 응답 또는 제어행이 없는 응답은 PAUSE로 안전하게 처리한다.
- Web 프롬프트에는 ACTION 선택 안내만 남겼고, 중복 COMMAND를 후속 Web 라운드에 재전송하지 않는다.
- CLI usage는 중첩 구조, snake_case/camelCase, total 누락 계산을 지원하며 한 Task의 라운드별 사용량을 누적한다.
- 한 Task의 USER COMMAND, CODEX, WORKER, GPT WEB 메시지를 순서대로 누적하고 정상 종료 시 실행 파일 폴더 기준 Task/<프로젝트>_<스레드>/_yyyymmdd_HHmmss.txt로 UTF-8 transcript를 저장한다.
- Current Task 긴 상태 문구는 좌측 영역에서 줄바꿈되도록 조정했다.

남은 문제와 제약:

- 빌드된 Explorer 실행 파일을 통한 실제 화면 E2E 왕복 검증은 아직 완료하지 못했다. 현재 검증 결과는 build/test/node 문법 검사와 Worker 직접 실행 확인까지다.
- GPT Web 확장이 응답 완료 또는 Stop 상태를 보고하지 않거나 CLAIMED/연결 확인 상태에 머무르면 Worker는 결과를 확정할 수 없다. 이 경우 무한 대기를 막기 위한 취소·제한 처리가 별도 후속 범위다.
- CLI가 usage 값을 출력하지 않는 실행에서는 정확한 계정 한도 사용량을 산출할 수 없다. 현재 값은 CLI가 반환한 필드의 누적값이며, 5시간/주간 한도 조회는 별도 계정 API가 필요하다.
- 원격 최신 커밋과의 동기화는 로컬 변경을 먼저 커밋한 뒤 git pull --rebase로 수행해야 하며, 충돌 발생 시 자동 해결하지 않는다.

최종 확인: dotnet build ProjectHub.sln --configuration Debug --no-restore, dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore, node --check extension/gptweb-hub/content.js.
## 2026-09-19 COMMAND 상·하단 분리

- COMMAND 영역을 상하 2등분했다.
- 상단 CLI COMMAND는 Codex CLI에만 전달한다.
- 하단 GPT WEB INSTRUCTION은 CLI에 전달하지 않고, CLI 결과 뒤에 별도 구분자로 붙여 GPT Web에 전달한다.
- 후속 라운드에도 동일한 Web 지침을 적용하며, transcript에는 USER COMMAND와 GPT WEB INSTRUCTION을 각각 기록한다.
- 하단 입력이 비어 있으면 기존 ACTION 프로토콜 안내만 사용한다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore 성공(경고 0, 오류 0), 테스트 5개 통과, node --check extension/gptweb-hub/content.js 통과, git diff --check 통과.
- 잔여 검증: 빌드된 Explorer 화면에서 두 입력값을 실제 작성·전송하는 E2E 확인.
## 2026-09-19 최신 원격 피드백 반영

원격 피드백 bd162d9의 최우선 기준인 실제 GPT Web 왕복 E2E를 기준으로 Worker·Bridge·Extension transport를 보완했다.

구현:

- conversation binding이 없거나 현재 대화와 다르면 task 생성·claim을 거부한다.
- result 제출 시 taskId, conversationId, leaseId를 검증하고 lease 없는 제출을 거부한다.
- 이미 완료된 result의 재전송은 기존 완료 task를 반환해 중복 처리를 막는다.
- Worker가 Web 응답 ACTION을 엄격히 판별하도록 보완했다. Web 응답의 ACTION 누락·복수·첫 유효행 위반·빈 BEGIN/CONTINUE 본문은 protocol error로 처리한다.
- Extension은 실제 ChatGPT composer 후보를 좁혀 선택하고, 입력 표시·Send 후 composer 비움·응답 시작을 확인한다.
- Extension result POST는 동일 payload를 최대 3회 재시도하며, lease를 포함한다.
- Extension 새로고침 후 task별 conversation/sessionStorage 기준점으로 기존 assistant 응답 재사용을 막는다.
- Extension 화면 상태는 IDLE, WORKER → GPT WEB, GPT WEB → WORKER, 작업 종료 흐름으로 표시한다.

검증:

- 최신 Worker EXE 직접 실행: 성공
- binding 없는 task 생성: conversation_not_bound 거부
- binding 후 task 생성: PENDING
- conversation 일치 claim: CLAIMED
- lease 없는 result: lease_mismatch 거부
- 올바른 lease result: COMPLETED
- dotnet build: 경고 0, 오류 0
- dotnet test: 5개 통과
- node --check extension/gptweb-hub/content.js: 통과
- git diff --check: 통과

실제 화면 E2E 잔여:

Windows computer-use 런타임이 초기화 단계에서 두 번 종료되어, 빌드된 EXE와 실제 Chrome 화면의 2회 왕복 및 첨부 1회 검증은 수행하지 못했다. 따라서 이를 완료로 기록하지 않는다. 다음 검증은 반드시 Explorer/Chrome 실제 화면에서 TEXT_ONLY 2회 왕복 후 IMAGE_ATTACHMENT 1회 순서로 수행한다.
## 2026-09-19 응답 수신 실패 원인 보완

실제 실패 task를 조회한 결과, Bridge가 응답을 받지 못한 것이 아니라 Extension의 TEXT_INSERT 단계에서 다음 오류로 종료됐다.

- task status: FAILED
- finish reason: send_failed
- result: ChatGPT composer text insertion not confirmed

원인 및 보완:

- composer 입력 검증이 개행·공백 차이를 고려하지 않아 정상 입력도 실패로 판정할 수 있었다.
- 입력·Send·응답 시작을 TEXT_INSERT, SEND_BUTTON_FIND, SEND_CONFIRM, RESPONSE_START 단계로 분리했다.
- 비활성 ChatGPT 탭에서 포커스를 얻지 못하면 task를 FAILED로 소비하지 않고 WEB_REQUIRES_FOREGROUND 상태로 유지한다.
- 탭이 활성화되면 같은 CLAIMED task와 저장된 baseline을 사용해 재개하며 중복 task를 생성하지 않는다.

Extension 재검증 전에는 Chrome에서 확장을 새로고침해야 한다.
## 2026-09-19 포커스 정책 정정

앞선 수정에서 비활성 탭을 WEB_REQUIRES_FOREGROUND으로 제한하려 했으나 기존 동작과 맞지 않아 제거했다. Extension은 다시 포커스 여부와 무관하게 composer 입력·Send를 시도한다. 보완 대상은 포커스가 아니라 composer 내용 검증의 공백·개행 차이이며, 전송 실패 단계는 TEXT_INSERT/SEND_CONFIRM/RESPONSE_START로 구분한다.
## 2026-09-20 BEGIN 전용 Web 프로토콜·추가 지침

- 명시적인 [ACTION=BEGIN]으로 시작한 작업의 최초 Worker → GPT Web 전송에만 ACTION 프로토콜 안내와 하단 GPT Web 추가 지침을 포함한다.
- 일반 CLI 명령과 BEGIN 이후의 후속 Web 전송에는 프로토콜 안내·GPT Web 추가 지침·원래 지침을 재전송하지 않는다.
- 후속 전송은 현재 Codex 실행 결과만 전달하며, ACTION 반복 판정은 Web 대화의 최초 BEGIN 지시를 문맥으로 유지한다.
- 검증: ProjectHub.sln Debug 빌드 성공(경고 0, 오류 0), 테스트 5개 통과, git diff --check 통과.
## 2026-09-20 최초 Web 질문 프로토콜 복원

일반 명령도 최초 Worker → GPT Web 질문에는 ACTION 프로토콜 안내를 포함하도록 정정했다. 다만 하단 GPT Web 추가 지침은 명시적 [ACTION=BEGIN] 초기 작업일 때만 포함한다. BEGIN 이후 후속 Web 전송에는 프로토콜과 추가 지침을 모두 포함하지 않는다.
## 2026-09-20 Web prompt 제목 제거

- 최초 Web prompt에서도 원래 작업 블록을 제거했다.
- Codex 실행 결과 제목도 제거해 결과 원문을 바로 전달한다.
- 프로토콜 안내와 BEGIN 전용 Web 추가 지침의 조건은 유지한다.
## 2026-09-20 CLI 실제 파일 수신 및 가짜 첨부 제거
- Codex CLI JSON 출력의 파일·첨부·경로 필드에서 실제 존재하는 파일만 수신하도록 CodexCliFile 파싱을 추가했다.
- Worker는 수신한 실제 파일을 로컬 첨부 저장소에 복사한 뒤 원래 파일명과 MIME을 유지해 GPT Web에 전달한다.
- 텍스트 결과를 임의 PNG로 변환하던 CreateTextImageAttachment와 명령어 기반 가짜 이미지 첨부 판정은 제거했다.
- CLI가 파일을 반환하지 않으면 Web task의 첨부 목록은 비어 있다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore 성공, dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore 5개 통과, node --check extension/gptweb-hub/content.js 통과, git diff --check 통과.

## 2026-09-20 Web 라운드별 ACTION 프로토콜 복원
- 로그 분석 결과, 최초 Web 응답 이후 Codex 후속 결과를 Web에 재전송하는 경로에서 ACTION 지침을 제외하고 있었다.
- 이 때문에 다음 Web 응답이 ACTION 없이 도착해 strict parser에서 정상적인 제어 응답으로 처리되지 못했다.
- 후속 Web prompt도 매번 ACTION 선택 지침을 포함하도록 수정했다. Web 추가 지침은 최초 전송에만 유지한다.
- 검증: Worker 재빌드 성공, 전체 테스트 5개 통과, Extension 구문 검사 통과, 최신 Worker 재기동 후 Bridge ready 및 Web connected 확인.

## 2026-09-20 실제 스무고개 왕복 성공 검증
- 로그: src/ProjectHub.Worker/bin/Debug/net9.0-windows/Task/Web2LLM_(Web2LLM) Resume AMD Web2LLM work/_20260920_004853.txt
- 최초 Codex가 정답 준비 완료를 알리고, GPT Web이 질문자 역할로 첫 질문을 시작했다.
- 이후 8회 질문에서 매번 GPT Web의 질문과 잔여 횟수, Codex의 예/아니오 답변이 순서대로 전달됐다.
- 후속 Worker → GPT Web 요청마다 ACTION 선택 지침이 포함됐고, 마지막 응답은 [ACTION=END]로 정상 종료됐다.
- 역할 전도, 중간 응답 재사용, ACTION 누락, 조기 종료는 이번 로그에서 확인되지 않았다.
- 검증 결과는 실제 Worker 실행파일과 연결된 GPT Web 왕복 로그 기준이며, 현재 ACTION 라운드 반복 구현의 성공 사례로 기록한다.

## 2026-09-20 배포 검증 목표 정정
- 피드백의 배포·진단·Extension 내장·갱신 작업은 하나의 배포 개선 묶음으로 수행한다.
- clean PC, 별도 테스트 사용자, 무설치 환경을 이용한 검증은 범위에서 제외한다.
- 필수 검증 환경은 현재 개발 PC의 빌드된 ProjectHub.Worker.exe와 Chrome이다.
- Worker EXE 최초 실행 시 %LOCALAPPDATA%\ProjectHub\GPTWeb-Hub\extension\ 폴더가 생성되고 manifest.json/content.js가 추출되는지 확인한다.
- Chrome에서 해당 폴더를 최초 1회 Load unpacked한 뒤, Extension 파일 갱신 시 Chrome 확장 새로고침으로 버전업·갱신이 가능한지 확인한다.
- 기존 Task, attachments, logs, state 보존과 Extension 경로 고정 여부를 같은 PC에서 확인한다.
- self-contained single-file publish 설정은 유지하되, clean PC에서의 .NET Runtime 미설치 실행 검증은 수행하지 않는다.

## 2026-09-20 배포 개선 일괄 구현
- ProjectHub.Worker에 win-x64 self-contained single-file publish 기본 설정을 추가했다.
- Extension manifest.json/content.js를 Worker 실행파일에 EmbeddedResource로 내장하고, 최초 실행 및 버전 변경 시 %LOCALAPPDATA%\ProjectHub\GPTWeb-Hub\extension\ 폴더로 원자적 추출·갱신한다.
- Worker 런타임 데이터 경로를 %LOCALAPPDATA%\ProjectHub\Worker\ 아래의 config, state, Task, attachments, logs로 통합하고 Codex archive도 AppData로 이동했다.
- Worker UI에 Extension 버전·추출 경로·Chrome 탐색 결과를 표시하고, single-file에서 외부 아이콘 파일에 의존하지 않도록 tray 아이콘을 실행파일 아이콘에서 읽도록 변경했다.
- Codex CLI 탐색은 PATH 및 bundled 경로를 유지하고 Chrome 설치 경로 진단을 추가했다.
- 검증: Debug 전체 빌드 성공(경고 0/오류 0), Release self-contained single-file publish 성공, EXE 실행 후 Bridge ready 확인, Extension 파일 자동 생성 확인, 설치 manifest 버전 하향 후 EXE 재실행 시 0.1.0 자동 갱신 확인.
- clean PC 검증은 수행하지 않는다. 현재 개발 PC의 EXE 최초 실행·Extension 추출·재실행 갱신을 기준으로 한다.

## 2026-09-20 Worker 게시 경로 영구 설정
- ProjectHub.Worker.csproj의 PublishDir을 src/ProjectHub.Worker/bin/으로 고정했다.
- 앞으로 Release self-contained single-file publish 결과는 항상 src/ProjectHub.Worker/bin/ProjectHub.Worker.exe로 생성된다.
- Release DebugSymbols/DebugType을 비활성화해 게시 PDB를 생성하지 않는다.
- 검증: dotnet publish 성공, bin 루트 EXE 생성 확인, bin 루트 PDB 없음 확인.

## 2026-09-20 EXE 실행 폴더 자급 경로 정정

- `WorkerPaths.Root`를 `AppContext.BaseDirectory\Worker`로 변경했다.
- 상태, 설정, Task, 첨부파일, 로그, Codex 로컬 archive는 모두 실행 중인 Worker EXE 폴더 아래에 생성된다.
- GPTWeb-Hub Extension은 `AppContext.BaseDirectory\GPTWeb-Hub\extension`에 생성·갱신된다.
- 따라서 게시 폴더를 다른 위치로 옮겨 실행해도 해당 폴더가 자체 데이터 루트가 된다. clean PC 검증은 수행하지 않는다.
- 검증 예정: Release self-contained single-file 게시 후 EXE 옆 `Worker\` 및 `GPTWeb-Hub\extension\` 생성, Bridge ready, 테스트 통과.

## 2026-09-20 CLI 기본 작업 폴더 고정

- 프로젝트·스레드를 선택하지 않은 신규 CLI 작업도 `AppContext.BaseDirectory`를 `workingDirectory`로 사용하도록 수정했다.
- 따라서 경로를 지정하지 않은 파일 생성·수정 명령은 실행 중인 Worker EXE가 있는 폴더를 기준으로 수행된다.
- 프로젝트·스레드를 선택한 경우에는 기존처럼 선택된 프로젝트 경로 또는 Codex 세션 경로를 우선한다.

## 2026-09-20 원격 피드백 후속 보수 반영

- 원격 최신 피드백 `e56017f`의 Worker Web 전송 보수 및 Codex 선택 재검토를 읽고 반영했다.
- Extension은 Send 후 composer 비움만으로 성공/실패를 판정하지 않고, 같은 prompt의 새 user message 또는 assistant 응답 시작을 확인한다. 전송 불확실 시 동일 prompt 자동 재전송은 하지 않는다.
- Codex placeholder를 `(현재 폴더) ＋ 신규 스레드`로 바꾸고, 빈 ProjectPath도 EXE 폴더로 안전하게 fallback한다.
- Codex CLI 실행 전 workingDirectory가 비어 있거나 존재하지 않으면 명시적으로 실패하도록 검증을 추가했다.
- 검증 완료: Debug 빌드 성공, 테스트 5개 통과, Extension 구문 검사 통과, 게시 EXE 실행 및 Bridge ready 확인.

## 2026-09-20 작업 사이클 제한을 무응답 timeout으로 변경

- 기존 Web ↔ Codex 30회 `_maxRounds` 제한을 제거했다.
- 작업 중 Web task 진행/수신 또는 Codex 결과 수신이 있으면 마지막 활동 시각을 갱신한다.
- Web 또는 Codex 응답이 30분 동안 없을 때만 Worker watchdog이 작업을 종료한다.
- timeout 시 활성 Codex 취소, Web task 취소, `FINISH_TIMEOUT` 표시, Task transcript 저장을 수행한다.
- 검증: Extension 구문검사, Debug 빌드, 테스트 5개, diff 검사를 통과했다.

## 2026-09-20 CLI 실패 진단 정보 표시

- Codex 결과 카드에 실행 파일 경로, 작업 디렉터리, session ID, CLI stderr를 추가했다.
- exit code 1 발생 시 실제 CLI 진단 정보를 확인할 수 있다.
- 검증 대상은 이번에만 C:\GameProject\ProjectHub.Worker.exe로 복사해 실행한다.


## 2026-09-20 Codex resume 비 Git 신뢰 오류 수정

- 최초 신규 스레드뿐 아니라 GPT Web 응답 후 Codex resume에도 작업 폴더의 Git 여부를 확인하도록 수정했다.
- 비 Git 폴더의 신규 실행과 resume 모두 --skip-git-repo-check를 사용하고, Git 저장소에서는 사용하지 않는다.


## 2026-09-20 Codex 파일 생성 권한 보완

- Web 지시가 [ACTION=PAUSE]로 종료된 최신 로그에서 원인이 Git이 아니라 Codex read-only sandbox임을 확인했다.
- Worker가 실행하는 모든 Codex xec에 --sandbox workspace-write를 추가했다.
- 사용자가 명령에 파일 권한을 승인한 경우 작업 폴더의 파일·폴더 생성을 허용한다.
- 기존 비 Git 폴더 조건부 --skip-git-repo-check와 함께 적용한다.


## 2026-09-20 CLI sandbox 권한 정책 명확화

- Codex CLI 실행은 기본적으로 `--sandbox workspace-write`를 사용한다.
- 최초 사용자가 명령에 `읽기 전용`, 파일·폴더 생성/수정 금지, `read-only`, `do not create/modify/write`처럼 제한을 명시한 경우에만 해당 Task 전체를 `--sandbox read-only`로 실행한다.
- Web 후속 응답의 문구로 sandbox 정책을 다시 판정하지 않고, 최초 사용자 명령에서 결정한 정책을 모든 resume 호출에 유지한다.
- 결과 진단 정보에 실제 적용 sandbox 모드를 표시한다.
- 검증: Debug build 성공, 전체 테스트 통과, Release 게시 성공, C:\GameProject 게시본 교체·재시작 완료.

## 2026-09-20 Chrome 확장 경로 고정

- 프로젝트 경로와 Worker 실행 폴더에 따라 Chrome 확장 설정을 다시 하지 않도록 Extension 경로를 `%LOCALAPPDATA%\ProjectHub\GPTWeb-Hub\extension\`로 고정했다.
- Worker가 어느 프로젝트에서 실행되더라도 시작 시 같은 공용 확장 폴더의 `manifest.json`·`content.js`를 최신 내장 리소스로 갱신한다.
- Chrome은 이 폴더를 최초 1회 Load unpacked로 등록하고, 이후에는 Worker 재시작 뒤 Chrome 확장 새로고침만 수행하면 된다.
- 프로젝트 생성·선택 위치는 Worker 데이터 및 Codex 작업 폴더에만 영향을 주며 확장 등록 경로에는 영향을 주지 않는다.

## 2026-09-20 SEND_UNCONFIRMED 보완

- Send 후 8초 안에 단일 selector로 확인하던 방식을 제거했다.
- 사용자 메시지 탐지에 `data-message-author-role`, `conversation-turn-user`, conversation turn 후보를 함께 사용하고, 전송 전 메시지 목록과 비교해 새 메시지를 판정한다.
- 새 사용자 메시지와 assistant 응답 시작을 병렬 확인하며 확인 대기 시간을 30초로 늘렸다.
- composer 비움 여부는 성공 조건으로 사용하지 않는다.
- 검증: `node --check extension/gptweb-hub/content.js`, Debug build, 전체 테스트, Release publish 성공. 고정 Extension 폴더와 `C:\GameProject` 게시본 갱신 및 Worker 재시작 완료.

## 2026-09-20 Worker watchdog 기준으로 전송 확인 timeout 제거

- Extension의 Send 후 고정 30초 실패 판정을 제거했다.
- 전송 확인은 `WAIT_RESPONSE` 상태에서 새 사용자 메시지·assistant 응답 시작을 계속 관찰한다.
- 확인이 늦어도 `send_failed`를 Worker에 보내지 않으며, 결과 수신·사용자 취소·Worker 30분 무응답 watchdog이 최종 종료를 담당한다.
- 검증: `node --check extension/gptweb-hub/content.js` 통과 후 Release 게시본에 재내장한다.

## 2026-09-20 Send 확인 대기 비동기화

- Send 이후 확장이 확인을 `await`하지 않도록 변경했다. 클릭 직후 `전송 요청 완료 · 응답 대기 중`으로 반환해 refresh 루프를 막지 않는다.
- 이후 ChatGPT 응답은 기존 observer가 감시하고, 전송이 실제로 진행되지 않으면 Worker watchdog이 최종 timeout 처리한다.
- 전송 확인 실패를 이유로 Extension이 임의로 `send_failed`를 제출하지 않는다.

## 2026-09-20 전송 버튼 지속 감시 및 창 위치 복원

- Extension은 입력 확인·활성 Send 버튼 대기를 제한 시간으로 실패 처리하지 않고 `WAIT_SEND_READY` 상태를 유지한다.
- 입력이 확인되고 Send 버튼이 활성화되면 감시 루프가 클릭하고 `WAIT_RESPONSE`로 전환한다. 실제 전송 불가 시 Worker watchdog이 종료를 담당한다.
- Worker 창의 `Left`·`Top`을 실행파일 폴더 하위 `Worker\config\window-placement.json`에 저장하고, 다음 실행 시 화면 밖 위치가 아닌 경우 복원한다.
- 검증: Extension `node --check`, Debug build, 전체 테스트 통과, Release 게시 및 C:\GameProject 재시작, 고정 확장 해시 일치, window-placement.json 생성 확인.

## 2026-09-20 창 위치 저장 시점 정정

- 창 이동 중 `LocationChanged` 저장을 제거했다.
- 트레이 `Exit` 요청과 실제 허용된 Window Closing 시점에만 현재 `Left`·`Top`을 저장한다.
- 일반 X 버튼은 기존대로 트레이로 숨기므로 위치 파일을 갱신하지 않는다.

## 2026-09-20 공용 확장 업데이트 적용 버튼

- Extension manifest에 background service worker를 추가했다.
- 설정 화면의 `업데이트 적용` 버튼이 `chrome.runtime.reload()`를 요청하고 현재 ChatGPT 탭을 다시 로드해 최신 content.js를 적용한다.
- Worker는 계속 `%LOCALAPPDATA%\ProjectHub\GPTWeb-Hub\extension\`만 관리하며, 프로젝트·실행 폴더 변경과 Chrome 확장 등록 경로를 분리한다.
- 최초 1회 Chrome에서 공용 경로를 Load unpacked로 등록한 뒤에는 프로젝트 변경 시 재등록하지 않는다.
- 검증: content/background JavaScript 구문 검사, Debug build, 전체 테스트, Release publish, 공용 경로의 manifest/content/background 갱신 및 Worker 재시작 완료.

## 2026-09-20 첨부파일 수신 로직 복구

- `sendToChatGPT`에 남아 있던 미정의 `attachFiles` 호출을 실제 구현으로 교체했다.
- Worker의 loopback `downloadUrl`에서 파일을 받아 `File`·`DataTransfer`로 ChatGPT file input에 주입하고 input/change 이벤트를 발생시킨다.
- 파일 input이 아직 DOM에 없으면 첨부/업로드 버튼을 눌러 생성한 뒤 다시 탐색한다.
- 첨부 수신 이후에는 `WAIT_SEND_READY` 상태에서 Send 버튼 감시로 전환한다.
- 검증: 첨부 URL HTTP 200 및 App.xaml 269 bytes 수신, JavaScript 구문 검사, Debug build, 전체 테스트, Release 게시, 공용 확장 갱신 및 Worker 재시작 완료.

## 2026-09-20 확장 업데이트 버튼 위치 개선
- `업데이트` 버튼을 설정 모달 내부에서 제거하고 GPTWeb-Hub 제목 우측 헤더로 이동했다.
- 패널을 닫거나 설정 모달을 열지 않아도 확장 갱신을 실행할 수 있으며, 기존의 `chrome.runtime.reload()`와 현재 ChatGPT 탭 새로고침 동작은 유지한다.
- 검증: `node --check extension/gptweb-hub/content.js` 통과.

## 2026-09-20 전송 버튼 주기 감시 보완
- 첨부파일 주입과 메시지 작성이 끝난 뒤에도 전송 버튼 후보를 계속 탐색한다.
- `data-testid`·`aria-label` 변형을 확장하고, 버튼이 비활성인 동안에도 주기적으로 `.click()`을 시도한다.
- 버튼이 활성화된 순간 포인터 이벤트와 click을 발생시키고 즉시 감시를 종료해 중복 전송을 막는다.
- 전송 완료 및 후속 결과 판정은 기존대로 Worker가 담당한다.
- 검증: `node --check extension/gptweb-hub/content.js` 통과.

## 2026-09-20 확장 버전 식별 보완
- Extension manifest 버전을 `0.1.1`로 올려 코드 갱신과 Chrome/Worker 배포 상태를 구분할 수 있게 했다.
- Worker는 manifest 버전뿐 아니라 `content.js`·`background.js` 내용도 비교해 공용 Extension을 갱신한다.
- 검증: 게시 후 공용 경로의 manifest 버전과 세 파일 SHA-256을 소스와 비교한다.

## 2026-09-20 확장 0.1.1 실제 재배포 확인
- 최신 게시 EXE를 `C:\GameProject\ProjectHub.Worker.exe`에 교체하고 Worker를 재시작했다.
- `%LOCALAPPDATA%\ProjectHub\GPTWeb-Hub\extension\manifest.json`이 `0.1.1`로 갱신됐다.
- `content.js`, `manifest.json`, `background.js`의 소스·공용 배포본 SHA-256이 모두 일치한다.
- Worker 실행 상태: `C:\GameProject\ProjectHub.Worker.exe`, 정상 응답.
- Chrome은 확장 새로고침과 기존 ChatGPT 탭 새로고침이 필요하다.

## 2026-09-20 업데이트 버튼 전체 상태 초기화
- `업데이트` 클릭 시 현재 conversation의 PENDING/CLAIMED Task를 `/bridge/reset`으로 `extension_reset` 종료한다.
- 확장의 `sessionStorage` task 복구값, composer 문자열, Worker Message, Web Response, lease, baseline, phase, spinner를 초기화한 후 확장을 재로드한다.
- reset 요청이 실패해도 로컬 확장 상태는 초기화해 이전 Task 복구로 인한 정지를 방지한다.
- 검증 완료: `node --check extension/gptweb-hub/content.js`, Debug 빌드 성공, 전체 테스트 5개 통과, Release 게시 및 C:\GameProject 재시작, reset endpoint 응답 확인.

## 2026-09-20 C:\GameProject 게시 실행 기준 고정
- Release 게시 후 self-contained 단일 파일인 `src/ProjectHub.Worker/bin/ProjectHub.Worker.exe`를 `C:\GameProject\ProjectHub.Worker.exe`로 자동 복사하도록 `ProjectHub.Worker.csproj`에 게시 후 작업을 추가했다.
- 실행·검증은 항상 `C:\GameProject\ProjectHub.Worker.exe`를 기준으로 한다. 런타임용 `bin\Release\net9.0-windows\win-x64\ProjectHub.Worker.exe`만 복사하면 DLL 의존성으로 즉시 종료될 수 있으므로 배포 대상으로 사용하지 않는다.
- 검증: self-contained 게시 성공, `C:\GameProject\ProjectHub.Worker.exe` 복사 및 실행 유지 확인.

## 2026-09-20 GPT Web 확장 일치 READY 조건
- 확장이 heartbeat에 `extensionVersion`과 `extensionBuild`를 전송하고 Worker가 기대값과 비교한다.
- GPT Web 카드가 연결되어 있어도 확장 식별자가 불일치하면 `UPDATE REQUIRED`로 표시하고 `Run Task`를 비활성화한다. 활성 작업 중에는 Cancel 동작을 유지한다.
- 확장 manifest와 기대 버전을 `0.1.2`, 빌드 식별자를 `2026-09-20.3`으로 갱신했다.
- 검증: JavaScript 구문 검사, Debug 빌드, 전체 테스트 5개, Release 게시, C:\GameProject 실행본 해시 일치, Bridge HTTP 200, AppData 확장 자동 갱신 확인.

## 2026-09-20 루트 bin 게시본 자동 복사 보강
- 게시 후 복사 원본을 `$(PublishDir)` 추정 경로가 아닌 `src/ProjectHub.Worker/bin/ProjectHub.Worker.exe`로 명시했다.
- 게시 로그에서 루트 self-contained EXE의 `C:\GameProject\ProjectHub.Worker.exe` 복사를 확인했으며, 두 파일 크기·SHA-256이 일치하고 실행 상태를 유지했다.

## 2026-09-20 전송 버튼 활성 후보 선택 보강
- 확장은 disabled 전송 버튼을 먼저 선택하던 경로를 제거하고, 현재 composer 주변의 활성 전송 버튼을 우선 탐색한다.
- 활성 버튼을 찾기 전에는 click을 호출하지 않고 계속 감시하며, 활성화된 버튼에만 pointer/mouse/click 이벤트를 보낸다.
- 확장 버전 `0.1.3`, 빌드 `2026-09-20.4`로 갱신했다. 검증: node 문법 검사, Debug 빌드, 전체 테스트 5개, Release 게시, AppData 확장 갱신, C:\GameProject 실행본 Bridge HTTP 200.

## 2026-09-20 ACTION 첫 유효행 파서 수정
- 기존 파서는 응답 전체에서 정확히 일치하는 ACTION 행을 모두 세어 본문에 포함된 예시·인용까지 중복 ACTION으로 오판할 수 있었다.
- 이제 첫 번째 유효행 하나만 ACTION으로 판정하고 이후 본문에 등장하는 ACTION 문자열은 무시한다.
- 검증: Debug 빌드 성공, 전체 테스트 5개 통과. Release 게시 및 실행본 반영은 별도 승인 대기.

## 2026-09-20 Worker UI 그룹 재배치
- 상단 Codex/GPT Web/Server 상태 카드를 메인 화면에서 분리해 설정 버튼의 팝업으로 이동했다.
- MESSAGE 그룹을 COMMAND 위로 배치했다. 유휴 상태에서는 MESSAGE를 접고 COMMAND 입력 영역을 확장하며, 실행 중에는 COMMAND 입력 영역을 접고 MESSAGE를 확장한다. 모델 콤보박스와 하단 Clear/Run 버튼은 유지한다.
- 실행 중인 C:\GameProject 게시 EXE는 종료하거나 덮어쓰지 않았다. 이번 변경은 소스·문서와 Debug 빌드·테스트까지만 검증했다.

## 2026-09-20 저장소·서버 자동설정 1회 초기화

- Worker 시작 시 InitializeStartupConfigurationAsync가 Codex 프로젝트/스레드 탐색, Codex 로그인 상태 확인, 서버 주소 해석 및 /api/status 확인을 한 번만 수행한다.
- 3초 상태 타이머는 더 이상 CLI 로그인 확인이나 서버 요청을 반복하지 않고, 이미 수신한 Bridge/Web 상태와 초기화 결과만 화면에 반영한다.
- Run Task 직전에도 동일한 1회 초기화 메서드를 호출하므로 시작 이벤트보다 빠른 클릭도 안전하게 처리한다.
- CLI 실행 완료 뒤 신규 스레드 목록을 갱신하는 동작은 새 스레드 반영을 위해 유지한다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore, git diff --check.
## 2026-09-20 MESSAGE 그룹 제목 토글

- 유휴 상태에서 MESSAGE 본문을 완전히 숨기지 않고 MESSAGE 제목 버튼을 유지한다.
- 제목을 클릭하면 Codex/GPT Web 탭과 결과 본문이 펼쳐지고 다시 클릭하면 본문이 접힌다.
- 작업 실행 중에는 MESSAGE 본문을 자동으로 펼치고 COMMAND 입력 영역을 접는다.
- 작업이 끝나면 MESSAGE 본문을 다시 접고 제목만 남긴다.
- 검증: Debug 빌드, 전체 테스트, git diff --check.
## 2026-09-20 저장소·서버 대상 설정 계층

- Worker 설정 팝업에 Git Repository URL과 ProjectHub Server Base URL을 추가했다.
- 시작 시 현재 선택 프로젝트의 Git root, origin URL, branch, Local HEAD SHA를 식별하고, Server Base URL을 Settings manual > environment > 기존 설정 > default 순서로 확정한다.
- 설정은 실행 폴더 하위 Worker/config/target-settings.json에 저장하며 Repository/Server 값과 자동 감지 출처를 분리한다.
- Auto Detect는 수동 override를 제거하고 현재 프로젝트·환경변수·기본값을 다시 적용한다. Settings 저장은 다음 실행부터도 유지되는 manual override가 된다.
- 반복 상태 타이머는 주소를 재조회하지 않는다. 프로젝트 선택 변경과 Settings 저장/Auto Detect에서만 대상 주소·Git 식별을 갱신한다.
- 현재 구현은 Web 리뷰 직전 Git 상태 재검증·push/remote/server SHA 일치 게이트를 다음 단계로 남긴다. 주소 확정과 리뷰 직전 상태 검증을 별도 경로로 유지한다.
- 검증: Debug 빌드 성공(경고 0/오류 0), 전체 테스트 5개 통과, git diff --check 통과.
2026-09-20 Git 기반 Web review preflight 구현: Worker는 GPT Web task를 생성하기 직전에 각 round의 working tree(`git status --porcelain`), branch, local HEAD SHA, `origin` branch의 `git ls-remote` SHA를 읽기 전용으로 확인한다. DIRTY, detached HEAD, remote 조회 실패, remote branch 없음, local/remote SHA 불일치에서는 GPT Web으로 자동 전달하지 않고 `PAUSE`/`FINISH_PAUSED`와 원인을 표시한다. remote SHA가 local SHA와 같은 경우만 `REMOTE_CONFIRMED`로 `review_commit_sha`를 확정하고 Web prompt에 `REVIEW_SOURCE=GIT`, `REVIEW_COMMIT_SHA`, `SYNC_STATE`를 추가한다. Worker는 commit/push/fetch/pull을 실행하지 않는다. 기존 Server는 `projectId`와 `workstationId`를 알아야 관찰 `head_sha`를 조회할 수 있어 Worker의 현재 target 설정만으로 Server SHA를 비교할 계약이 없으므로 `UNAVAILABLE`로 별도 표시한다. Debug build와 전체 test 5개를 통과했다. 남은 검증: Release 게시본 Explorer/Chrome에서 실제 Git clean/dirty 및 SHA mismatch PAUSE와 remote-confirmed 전달을 확인한다.

## 2026-09-20 MESSAGE 유휴 제목 잘림 수정

- 유휴 상태의 MESSAGE 행 높이를 36px에서 52px로 조정했다. Section Border의 상·하 padding과 29px 제목 행을 포함해 제목 버튼이 잘리지 않는다.
- XAML 초기 높이도 52px로 맞춰 초기 레이아웃에서도 같은 높이를 사용한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore`, `git diff --check`.
## 2026-09-20 설정 Popup Codex 스레드 선택 영역 확장

- 설정 Popup 폭을 1040px로 넓히고, Codex 카드는 가로 공간의 절반을 사용하도록 배치했다.
- Codex 스레드 ComboBox를 420px × 34px로 확장했다.
- 상위 Popup의 자동 닫힘을 해제해 내부 ComboBox 드롭다운 항목을 클릭해도 설정 Popup이 먼저 닫히지 않는다. 설정 버튼을 다시 누르거나 Save Settings를 누르면 닫힌다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, git diff --check.
## 2026-09-20 설정 Popup 배경 어둡게 표시

- 설정 Popup이 열리면 부모 창 전체에 반투명 검은 오버레이를 표시해 설정 작업에 집중할 수 있게 했다.
- 오버레이를 클릭하면 설정 Popup이 닫히고 배경이 원래 밝기로 돌아온다.
- Save Settings도 Popup과 오버레이를 함께 닫는다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, git diff --check.
## 2026-09-20 설정창 Apply·Close 및 상태 정보 재배치

- Target Settings 하단에 Close와 Apply 버튼을 분리했다. Apply는 기존 Save Settings 동작으로 수동 주소 설정을 저장하고 창을 닫는다.
- Git Repository 아래에 branch, Local HEAD, Git source 및 project path를 표시하고, Server Base URL 아래에 Server source를 표시한다.
- Target Settings의 제목·라벨·입력·버튼 글자를 키우고 입력 폭을 넓혔다.
- ComboBox 템플릿 안에 잘못 삽입돼 스레드 선택 입력을 방해하던 중복 오버레이를 제거했다. 설정창 루트에는 입력을 받지 않는 오버레이 하나만 남긴다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, git diff --check.
## 2026-09-20 설정 Popup 모달 입력 차단

- 설정 Popup이 열리면 반투명 오버레이가 부모 창의 모든 입력을 받도록 변경했다.
- 설정 Popup 내부의 컨트롤만 사용할 수 있으며, 배경 클릭으로 닫히지 않는다. Close 또는 Apply로만 닫는다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, git diff --check.
## 2026-09-20 Git·Server 선택 참조 정책 정정

- Git과 Server는 GPT Web 전송의 필수 조건이 아니다. repository 미구성, detached HEAD, dirty 상태, remote 미확인, SHA 불일치에서도 Worker는 GPT Web 확장 전송을 계속한다.
- remote SHA가 local HEAD와 일치할 때만 GIT source와 exact review commit SHA를 prompt에 넣는다. 그 밖의 경우는 LOCAL source로 현재 Worker 결과를 전달하고 Git 상태는 참고 정보로만 남긴다.
- Server observed SHA도 현재 Worker에 projectId/workstationId 매핑이 없으므로 참고 불가 정보일 뿐 전송을 막지 않는다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, git diff --check.
## 2026-09-20 Server 선택사항 preflight 정정

- Run Task preflight에서 Server ONLINE 조건을 제거했다. Server 상태는 Settings와 상태 카드의 참고 표시만 유지한다.
- Codex 로그인 및 GPT Web bridge/extension 연결만 실행 시작 조건으로 사용한다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, git diff --check.
## 2026-09-20 Git 참조 최초 요청 한정 및 스레드 ComboBox 보정

- Git 참조 헤더는 Task의 최초 Codex CLI 요청과 최초 Worker → GPT Web 요청에만 포함한다. 후속 Codex resume 및 Web round에는 포함하거나 재조회하지 않는다.
- CodexThreadCombo는 전역 커스텀 ComboBox 템플릿을 사용하지 않고 기본 WPF ComboBox 스타일을 사용한다. Popup 안에서도 스레드 드롭다운을 열고 항목을 선택할 수 있다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, git diff --check.
## 2026-09-20 신규 스레드 작업 폴더 설정

- Settings에 Working Folder와 Choose Folder 버튼을 추가했다. 신규 스레드는 저장된 Working Folder를 사용하며 값이 없으면 실행 파일 폴더를 사용한다.
- 기존 Codex 스레드를 선택하면 해당 Codex ProjectPath가 Working Folder에 표시되고 입력과 폴더 선택 버튼이 잠긴다.
- Working Folder는 target-settings.json의 manualWorkingDirectory에 저장된다. Auto Detect는 이 수동값을 제거해 실행 파일 폴더로 되돌린다.
- Apply는 신규 스레드용 Working Folder가 실제 존재하는지 확인한 뒤 저장한다.
- 검증: dotnet build ProjectHub.sln --configuration Debug --no-restore, git diff --check.
## 2026-09-20 MESSAGE 누적 로그 및 CLI 라운드 상태

- MESSAGE 본문을 Codex와 GPT Web의 분리 결과 화면 대신 하나의 시간순 누적 로그로 통합했다. 새 메시지는 항상 마지막에 추가되며, 길어지면 세로 스크롤바가 나타나고 최신 메시지로 자동 이동한다.
- Codex/GPT Web 탭은 같은 로그를 유지한 채 선택 탭의 제목과 더 진한 배경색만 바꾼다.
- 작업 시작 시에만 모델, reasoning, 실행 파일, 작업 폴더, 세션 정보를 `TASK START` 항목으로 기록한다.
- CLI 실행은 매 라운드마다 `CLI STATUS`에 PASS/FAIL, exit code, model, session만 기록한다. 실제 Codex 결과는 전달되는 Worker → GPT Web 메시지와 GPT Web 응답의 순서로 로그에 남는다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0), `git diff --check` 통과.
## 2026-09-20 Optional Jev Judge scaffold

- 최신 `GPT-Web-Feedback.md`를 fetch/rebase 뒤 다시 읽고, Judge를 기존 Codex → Worker → GPT Web 흐름의 선택형 보조 단계로 추가했다.
- Settings에 `SUB AI / JUDGE`를 추가했다. Enable Judge는 기본 OFF이며 provider, executable/endpoint, timeout(10~600초), GPT Web fallback 정책을 `target-settings.json`의 `judge` 설정으로 저장한다.
- Jev 자동 탐색은 PATH와 알려진 로컬 경로 및 manual path를 확인한다. 현재 Jev CLI/API 실행 계약은 아직 연결하지 않았으므로 Judge를 켜도 `JUDGE=ERROR`과 사유를 MESSAGE LOG에 기록한 뒤 GPT Web으로 계속 전달한다.
- Judge 활성 시 Current Task에 보라색 pulse 상태를 표시하고 MESSAGE LOG에 `JUDGE STATUS`와 결과를 순서대로 추가한다. Judge OFF에서는 기존 Web handoff prompt, attachment, ACTION=CONTINUE/PAUSE/END 처리가 바뀌지 않는다.
- `JevJudgeRunner`, `JudgeRequest`, `JudgeResult`, `JudgeDecision`은 실제 실행 어댑터와 PASS/REVISE/ESCALATE 분기를 다음 작업에서 연결하기 위한 구조다. 현재 모델 연동과 Judge ON E2E는 잔여 작업으로 남긴다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 성공(총 5개), `git diff --check` 통과. Explorer 실화면 및 Judge ON E2E는 아직 수행하지 않았다.
## 2026-09-20 Dynamic two-node task flow

- Current Task의 고정 3개 아이콘을 현재 전송 방향에 맞는 2개 노드로 교체했다. 중간 화살표는 해당 방향으로 pulse 애니메이션을 표시한다.
- 표시 조합은 `CODEX → WORKER`, `WORKER → GPT WEB`, `GPT WEB → WORKER`, `GPT WEB → CODEX`, `WORKER → JUDGE`, `JUDGE → CODEX`를 지원한다. Judge OFF 상태에서는 기존 Codex/Worker/Web 조합만 사용한다.
- 각 노드는 현재 단계에서만 `진행 중`과 강조색을 표시하고, 상대 노드는 `대기 중`으로 표시한다. Judge에는 별도 J 아이콘을 사용한다.
- 실행 분기와 bridge/codex 호출 순서는 변경하지 않았다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0), `git diff --check` 통과. Explorer 실화면 확인은 잔여 작업이다.
## 2026-09-20 Task flow icon contrast and publish script

- 두 노드 흐름의 아이콘 영역을 100px 원형 배경과 84px 이미지로 확장했다. 활성 노드는 provider별 컬러 이미지와 강조 배경을, 대기 노드는 그레이스케일 이미지와 slate 배경을 사용한다.
- 흐름 패널을 오른쪽 정렬에서 왼쪽 정렬로 옮겨 Current Task 카드의 중앙 쪽에 배치했다.
- `src/ProjectHub.Worker/bin/publish-worker.ps1`을 추가했다. 이 스크립트는 Worker 프로젝트를 Release로 게시하며 `-NoRestore` 옵션을 지원한다. bin 경로가 ignore 대상이므로 파일은 force-add로 Git index에 포함했다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0), `git diff --check` 및 staged diff check 통과.

## 2026-09-21 JEV Contract Gate 구현

최신 `GPT-Web-Feedback.md`와 `JEV-FOOTER-CONTRACT.md`를 기준으로 Worker 라우팅 틀을 구현했다.

- JEV가 켜진 경우 Codex 지시 뒤에 임베디드 `JEV-FOOTER-CONTRACT.md`를 붙인다. Judge OFF에서는 기존 Codex prompt 흐름을 보존한다.
- Codex 결과의 첫 유효행만 독립적으로 검사해 `[NEXT : WEB]`와 `[NEXT : JEV]`를 구분한다. 기존 Web `[ACTION=...]` 파서는 별도로 유지한다.
- `[NEXT : WEB]`은 결과를 GPT Web로 전달하고, `[NEXT : JEV]`는 `[VALIDATION REQUEST]`만 JEV 경로로 분리한다.
- JEV FAIL은 최대 3회까지 같은 Codex session에 보완 지시를 되돌리고, timeout/error/불완전 응답은 GPT Web fallback으로 보낸다. JEV 경로는 파일 쓰기·commit·push를 수행하지 않는다.
- 현재 저장소에는 TypeSafe/JEV provider의 실제 실행 계약(endpoint payload/response)이 제공되지 않았으므로 외부 호출을 추측해 추가하지 않았다. `JevJudgeRunner`는 provider가 구성되지 않은 경우 안전한 fallback을 반환한다.

검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 성공(5개), `node --check extension/gptweb-hub/content.js` 성공, `git diff --check` 재실행 예정.

## 2026-09-21 JEV API Contract v1 일괄 구현

동기화된 `GPT-Web-Feedback.md`, `JEV-FOOTER-CONTRACT.md`, `JEV-API-CONTRACT.md`를 기준으로 JEV scaffold를 실제 계약 흐름으로 확장했다.

- `JevJudgeRunner`가 공식 `https://api.typesafe.ai/v1/systemone`에 `jev-latest`, 원문 task/Codex 결과 state, typed questions를 전송한다.
- API key는 `TYPESAFE_API_KEY` 환경변수에서만 읽고 로그·설정·transcript에 남기지 않는다. 키가 없으면 호출하지 않고 Web fallback한다.
- NOUL/SCORE/CHOICE typed parser를 추가하고 SCORE threshold를 1-based footer 기준에서 0-based API 기준으로 정규화해 기계 비교한다.
- question ID 누락, type 불일치, 값 범위 오류, invalid choice, malformed contract와 API 오류는 PASS/FAIL을 추측하지 않고 ERROR fallback한다.
- JEV FAIL은 실패 계약과 실제 결과만 같은 Codex session에 전달하며, PASS는 즉시 Web로 보내지 않고 Codex에 `[NEXT : WEB]` 보고서 생성을 요청한 뒤 Web로 전달한다.
- Web ACTION 흐름과 Judge OFF 흐름은 유지한다. JEV retry count는 Web→Codex 라운드마다 초기화하며 JEV validation은 최대 3회다.

검증: Debug build 성공(경고 0, 오류 0), 기존 테스트 5개 통과, `node --check extension/gptweb-hub/content.js` 통과, `git diff --check` 통과. 현재 실행 환경에는 `TYPESAFE_API_KEY`가 없어 실제 API smoke test는 호출하지 않았다.

## 2026-09-21 JEV 아이콘 자산 반영

첨부된 `JEV이미지.png`를 `current-jev.png`로 등록하고, 투명 alpha를 유지한 `current-jev-gray.png`를 생성했다. Current Task의 JEV 노드는 활성 시 컬러, 대기 시 그레이스케일 아이콘을 사용하며 기존 원형 문자 아이콘과 보라색 배경은 제거했다. 원본 모서리 alpha가 0인 투명 PNG여서 별도 배경색은 추가하지 않았다.

검증: Debug build 성공(경고 0, 오류 0).

## 2026-09-23 MESSAGE 성능·JEV 전달 보강

- MESSAGE UI를 전체 문자열 재생성 방식에서 가상화된 `ListBox` 누적 항목 방식으로 변경했다. transcript용 전체 `_taskMessages`는 유지하고 화면 렌더링만 항목 단위로 분리해 긴 대화에서도 스크롤 비용을 줄인다.
- 최초 Codex, Web 후속 Codex, JEV FAIL 재시도, JEV PASS 후 보고서 생성 등 모든 Codex 실행을 `RunCodexWithJevFooterAsync`로 통합해 Judge가 활성화된 모든 호출에 footer가 붙도록 했다.
- GPT Web 전달문에 JEV 검증 안내를 추가했다. 의미 있는 구현·설계·파일·테스트 검증이 가능하면 Codex가 `[NEXT : JEV]`와 `[VALIDATION REQUEST]`를 선택하도록 유도하고, 검증 항목이 없을 때만 `[NEXT : WEB]`을 사용하도록 안내한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 성공(총 10개), `node --check extension/gptweb-hub/content.js`, `git diff --check` 통과.
- 실제 Explorer 화면에서 장시간 MESSAGE 스크롤 및 JEV ON 왕복 검증은 아직 남아 있다.

## 2026-09-23 Extension 전달 단계 telemetry 보강

- Extension에 `CLAIMED`, `TEXT_INSERT`, `SEND_BUTTON_FIND`, `SEND_CONFIRM`, `RESPONSE_START`, `RESPONSE_STABLE`, `RESULT_POST`, `RESULT_POST_RETRY`, `FAILED` 단계 보고를 추가했다.
- Bridge에 `/bridge/progress`를 추가하고 현재 Extension 진행 상태를 `/bridge/status`에도 포함했다. Worker는 각 단계 변경을 `WEB EXTENSION` 한 줄 MESSAGE 로그로 기록한다.
- composer 탐색은 최대 120초, Send 버튼·실제 user message 확인은 전체 최대 180초 동안 재시도한다. click 직후 완료로 처리하지 않고 새 user message 또는 composer 비움을 확인한다.
- 새 Extension build는 `2026-09-23.1`이며 Worker가 이전 Extension을 동기화 불일치로 감지한다.
- 검증: `node --check extension/gptweb-hub/content.js`, `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 성공(총 10개), `git diff --check` 통과.
- 잔여: 새 Extension을 Chrome에서 새로고침한 뒤 실제 화면에서 단계 로그와 Web 전송 1회 확인.

# CURRENT AUTHORITATIVE STATUS — 2026-09-22

단기 목표는 **JEV를 통한 AI 분기가 Worker에서 예측 가능하게 완료되는 것**이다. 과거의 scaffold/adapter pending 문장은 당시 상태를 기록한 이력이며 현재 판단 기준으로 사용하지 않는다.

## 단기 목표와 현재 흐름

```text
Codex CLI → Worker
  [NEXT : WEB] → Worker → GPT Web
  [NEXT : JEV] → Worker → TypeSafe JEV → Worker
      ALL PASS → 같은 Codex session에 [NEXT : WEB] 보고서 생성 요청 → Worker → GPT Web
      ANY FAIL → 같은 Codex session에 실패 계약/실제 결과만 전달 → 재작업 → JEV 재검증
      ERROR/timeout/invalid → 원래 Codex 결과와 짧은 오류를 Worker → GPT Web fallback
```

Worker는 의미 판단을 하지 않고 첫 NEXT 행, typed validation, JEV 구조화 응답의 기계적 비교와 hop 전달만 담당한다. GPT Web의 ACTION 프로토콜과 JEV의 NEXT 프로토콜은 서로 다른 parser로 유지한다.

## 현재 구현 상태

- `JEV-FOOTER-CONTRACT.md`를 임베디드 리소스로 Codex prompt에 주입한다.
- `[NEXT : WEB]`/`[NEXT : JEV]` 첫 유효행 parser와 `[VALIDATION REQUEST]` typed parser가 있다.
- NOUL/SCORE/CHOICE 질문을 `C1...` map으로 구성하고, SCORE는 1-based footer threshold를 JEV 0-based 기준으로 정규화한다.
- 공식 TypeSafe endpoint와 `TYPESAFE_API_KEY` Bearer 인증 adapter가 있다. 키가 없으면 호출하지 않고 Web fallback한다.
- JEV 응답의 answers 누락, ID 누락, type mismatch, 값 범위 오류, invalid choice는 PASS/FAIL로 추측하지 않고 ERROR fallback한다.
- JEV PASS는 Web로 직접 보내지 않고 같은 Codex session에서 `[NEXT : WEB]` + `[REPORT]`를 생성한 뒤 Web로 보낸다.
- JEV FAIL은 최대 3회까지 같은 Codex session으로 되돌리며, Web→Codex 새 라운드마다 validation count를 초기화한다.
- JEV 활성/대기 아이콘은 첨부 PNG의 컬러/투명 alpha와 생성된 grayscale 자산을 사용한다.

## 남은 단기 검증

1. `TYPESAFE_API_KEY`가 설정된 환경에서 실제 API smoke test 1회.
2. Explorer 실행본에서 Judge OFF, `[NEXT : WEB]`, JEV PASS, JEV FAIL 재작업, API error fallback의 순서 확인.
3. 실제 화면 검증 후 문서의 검증 결과를 갱신한다.

보안 규칙: API key는 소스·설정·로그·transcript·Git에 기록하지 않는다. 실제 smoke test 결과에도 키 원문을 남기지 않는다.

## 2026-09-23 취소 후 진행 애니메이션 잔류 수정

- Worker의 terminal Task 이벤트에서 중복/timeout 조건이 화면 정리보다 먼저 반환되던 경로를 수정했다.
- 취소 또는 이미 처리된 Task라도 `_awaitingWebResult`를 해제하고 Run 버튼을 복구한 뒤 `SetFlowState(false, false, false)`로 진행 애니메이션을 종료한다.
- 검증: Debug build 성공(경고 0/오류 0), 전체 테스트 10개 통과, Extension `node --check` 통과, `git diff --check` 통과.
- 실제 실행파일/Chrome 화면 검증은 아직 수행하지 않았다.

## 2026-09-23 복합 취소 경로 수정

- Run Task 버튼에서 Codex 실행 취소가 먼저 반환되어 GPT Web Task 취소와 화면 초기화가 누락될 수 있던 문제를 수정했다.
- Codex CTS와 Web Task가 동시에 활성인 경우 양쪽을 모두 취소하고 Worker 진행 상태·애니메이션을 즉시 초기화한다.
- 검증: Debug build 성공(경고 0/오류 0), 전체 테스트 10개 통과, `git diff --check` 통과.

## 2026-09-23 GPT-6 Luna 기본 모델 및 모델 선택 확장

- Worker의 기본 Codex 모델을 `gpt-6-luna`로 변경했다.
- 모델 선택 목록에 `GPT-6 Luna`, `GPT-6 Sol`, `GPT-6 Astra`, `GPT-5.6 Luna`, `GPT-5.6 Terra`, `GPT-5.6 Sol`, `GPT-5.5`를 제공한다.
- 공식 OpenAI 자료 기준 GPT-6 Luna는 입력 $0.10/1M, 출력 $0.50/1M이며 GPT-5.6 Luna는 입력 $0.20/1M, 출력 $1.20/1M이다. GPT-6 Luna는 입력 약 50%, 출력 약 58.3% 낮다.
- 두 Luna 모델은 공식 자료상 1.05M 컨텍스트, 128K 최대 출력, `medium` 기본 reasoning을 지원한다. 실제 Codex 계정별 사용 가능 여부는 CLI 계정 권한에 따른다.
- 검증: Debug build와 전체 테스트, `git diff --check`를 수행한다.

## 2026-09-23 GPT-6 Luna 인계 운영 가이드

- `GPT-6-LUNA-HANDOFF.md`를 추가했다.
- 기존 Codex session을 선택하고 모델을 `GPT-6 Luna`로 바꾼 뒤 다음 실행하면 Worker가 같은 session을 `resume`해 인계한다.
- 실행 중 모델 변경은 지원하지 않으며, 먼저 Cancel 후 기존 스레드를 다시 선택해 실행한다.
- 실제 사용 모델은 MESSAGE의 `TASK START`와 `CLI STATUS`의 model 항목으로 확인한다.

## 2026-09-23 09-B 잔여 검증 재시도

- 일반 권한 Debug 빌드는 Windows SDK 확인 중 `C:\Users\ornit\AppData\Local\Microsoft SDKs` 접근 거부로 실패했다. 동일 명령을 권한 확장으로 재실행해 성공했다(경고 0, 오류 0).
- 전체 테스트 10개 통과(Core 1, Agent 3, Server 1, Worker 5). `node --check extension/gptweb-hub/content.js`와 `git diff --check`도 통과했다.
- `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`를 기동 시도했다. 프로세스는 실행되지만 MainWindowHandle이 0이고 CUA 앱 열거 결과도 비어 있어 Explorer 화면을 조작할 수 없었다. `http://127.0.0.1:43821/bridge/status`도 연결 거부됐다. 따라서 실제 Explorer Judge ON/OFF 왕복 검증은 완료로 기록하지 않는다.
- `TYPESAFE_API_KEY`는 프로세스 환경에 존재했으나, 이 검증 시도에서는 TypeSafe 외부 API 호출을 보내지 않았다.
- 재현/검증 명령: `dotnet build ProjectHub.sln --configuration Debug --no-restore`; `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore`; `node --check extension/gptweb-hub/content.js`; `git diff --check`.
- 잔여 식별자: **09-B Explorer 화면 검증 재시도**, **09-C 증거 전달·수용 조건 고정**, **07 기존 검증 잔여**.

## 2026-09-23 취소 즉시 복구 및 Voice 전용 상태 감지

- 사용자 취소 시 진행 중인 CLI의 취소 정리를 기다리지 않고 즉시 IDLE 레이아웃으로 복원한다. 취소한 Bridge task ID를 기록해 늦게 도착한 terminal 이벤트가 복원 화면을 덮지 않게 하고, CLI 정리가 끝날 때까지 Run 버튼 재진입을 막는다. 활성 CLI와 Web task가 겹친 경우 둘 다 취소한다.
- Web Extension이 Worker task를 취소 상태로 받으면 자신이 삽입한 텍스트와 composer 내용이 정확히 일치할 때만 지워 Web 입력창을 복구한다. 취소로 전송 확인 대기가 풀려도 이를 성공으로 오판하지 않는다.
- 전송 버튼 판별에서 Voice/마이크 컨트롤을 제외한다. 입력 텍스트가 남아 있고 활성 Voice 버튼만 일정 시간 지속되면 내용을 보내지 못한 것으로 처리해 `FAILED`를 Worker에 전달한다.
- Extension build를 `2026-09-23.2`로 올리고 Worker의 동기화 기준도 함께 갱신했다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0/오류 0), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 10개 통과, `node --check extension/gptweb-hub/content.js`, `git diff --check` 통과.
- Explorer/Chrome 실제 화면 검증 및 새 Extension 새로고침은 UI 런타임 문제로 아직 수행하지 않았다. 코드 게시/배포도 하지 않았다.

## 2026-09-23 작업 중 MESSAGE 공간 확장

- 작업 중 COMMAND 행이 170px로 고정되어 입력 본문이 접혀도 여백과 하단 모델/실행 컨트롤이 공간을 차지하던 문제를 수정했다.
- 실행 중 COMMAND 행을 56px로 축소하고 입력 본문·하단 컨트롤 행을 접어 MESSAGE가 남는 높이를 사용할 수 있게 했다. 유휴 상태의 COMMAND 입력 UI는 유지한다.
- 검증: Debug build 성공(경고 0/오류 0), 전체 테스트 10개 통과, Extension `node --check`, `git diff --check` 통과.
- 실제 Explorer 화면에서 크기 확인은 UI 런타임 문제로 미수행이며 Worker 게시도 하지 않았다.

## 2026-09-23 빠른 GPT 응답과 동일 본문 응답 감지

- GPT Web 응답 감지는 본문 문자열만 비교하지 않고 assistant 메시지 개수, DOM 요소 정체성, 메시지 식별자를 기준점으로 저장·비교한다. 이전 응답과 본문이 같아도 새 assistant turn이면 새 응답으로 처리한다.
- Send 확인 대기 중 Voice 버튼만 남는 경우에도 새 사용자 메시지 또는 새 assistant turn이 빠르게 나타났는지 먼저 확인한다. 입력창이 비워졌다는 사실만으로 전송 성공 처리하지 않는다.
- 응답 본문은 새 turn이 확인된 뒤 스트리밍 종료 및 5초 안정화 확인을 거쳐 전송한다.
- Extension build와 Worker 기대값을 `2026-09-23.3`으로 맞췄다.
- 검증: `node --check extension/gptweb-hub/content.js`와 `git diff --check` 통과. `dotnet build ProjectHub.sln --configuration Debug --no-restore`는 SDK 경로 권한으로 기본 sandbox에서 실패했지만 권한 확장 재실행에서 경고 0/오류 0으로 성공했다. `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore`도 권한 확장 실행에서 10개 통과(Core 1, Agent 3, Server 1, Worker 5).
- 잔여: 실제 Chrome에서 빠른 응답·동일 본문 응답 및 Voice-only 전송 실패를 재현할 UI 검증. Extension은 게시/배포하지 않았다.

## 2026-09-23 Release 게시 및 작업 폴더 반영

- Debug 빌드와 전체 테스트 성공 후 `src/ProjectHub.Worker/bin/publish-worker.ps1 -NoRestore`로 Release self-contained EXE를 게시했다.
- 게시 스크립트가 `C:\GameProject\ProjectHub.Worker.exe`를 자동 갱신했다. 기존 실행 중 파일 잠금으로 `C:\AI-AGENT\Worker` 복사가 처음에는 실패해, 해당 Bridge task가 이미 canceled/terminal임을 확인하고 Worker를 종료한 뒤 새 EXE를 복사·재기동했다.
- 저장소 게시본, `C:\AI-AGENT\Worker`, `C:\GameProject` EXE의 SHA-256이 모두 `6424083FE1F1B2832C7813824D5FFDE1332F5129F11BBF8196A45326EECADBD1`로 일치한다.
- 새 Worker Bridge는 `ready`; Chrome의 GPTWeb-Hub 업데이트 버튼을 눌러 확장을 다시 불러온 뒤 현재/기대 Extension build가 모두 `2026-09-23.3`, 동기화 `true`임을 확인했다. 공용 확장 폴더의 manifest/content/background 해시도 소스와 각각 일치한다.
- 잔여: 실제 GPT Web에서 빠른 응답 및 동일 본문 응답 시나리오를 전송해 E2E 동작을 확인한다.

## 2026-09-23 신뢰 작업 폴더 일반 권한 실행 재시도

- 사용자가 C:\AI-AGENT\Worker를 trust 작업 폴더로 추가한 뒤, 기존 ProjectHub.Worker 프로세스를 종료하고 이 폴더의 EXE를 일반 권한으로 실행했다. PowerShell 실행 자체와 EXE 프로세스 시작은 성공했지만 두 번 모두 프로세스 종료/Bridge 연결 거부로 끝났다.
- 화면에 `ProjectHub.Worker.exe - 응용 프로그램 오류`, 예외 코드 `0xe0434352`가 표시됐다. 일반 권한 실행 sandbox와 trust 폴더 설정의 접근 거부는 재현되지 않았다.
- 현재 `ProjectHub.Worker` 프로세스는 없고 `127.0.0.1:43821/bridge/status`는 연결 거부된다. 최근 45분의 .NET Runtime/WER/Worker startup log에서 이번 실행의 예외 stack trace를 찾지 못했다. Event Log에 있는 과거 `0xe0434352` 항목은 2026-09-20 게시 하위 폴더의 누락된 `Assets\worker-icon.ico` 관련 기록이라 이번 실패 원인으로 간주하지 않는다.
- 잔여: 현재 실행 시점의 .NET 예외 상세를 확보한 뒤 원인을 수정하고 다시 게시/복사/실행한다.

## 2026-09-23 COMMAND 도구 유지 및 실제 ChatGPT composer 선택 수정

- 작업 중 COMMAND 영역을 90px로 조정했다. 입력 본문은 접되 모델/Reasoning 선택, Clear, Run Task, 사용량 표시가 있는 하단 제어행(38px)은 계속 표시한다.
- 사용자 화면의 `TEXT_INSERT` 후 Voice-only 실패를 추적한 결과, ChatGPT의 assistant 응답 안에도 편집 가능한 writing block이 있고 기존 `composer()`가 DOM 순서 첫 편집 요소를 composer로 잘못 선택했다. 실제 페이지 DOM에서 해당 블록과 `#prompt-textarea`가 같이 존재하는 것을 확인했다.
- Extension은 `#prompt-textarea`를 우선 선택하고, 대화 응답 편집 블록은 후보에서 제외한다. 전달 문구를 실제 composer에서 다시 읽어 확인한 후에만 Send 감시로 진행하며, progress detail에 선택된 입력 대상을 기록한다.
- Extension build와 Worker 기대값을 `2026-09-23.4`로 맞췄다.
- 검증: Node 구문 검사 통과, Debug build 경고 0/오류 0, 전체 테스트 10개 통과, diff check 통과. Release 게시 및 C:\AI-AGENT\Worker/C:\GameProject 복사를 완료했고 세 실행파일의 SHA-256이 일치한다. 새 Worker Bridge ready, Extension build .4 synchronized=true, 배포 확장 manifest/content/background source hash 일치를 확인했다.
- 실제 입력 전송은 사용자의 대화에 시험 메시지를 추가하므로 수행하지 않았다. 작업 중 툴바의 실제 화면 배치는 Explorer 화면 캡처 자동화가 없어 코드/빌드 및 실행 상태 확인까지 완료했다.

## 2026-09-23 GPT Web 응답의 Worker 전달 정체 조사

- 기존 `de45e57d38e74728b03ff414f40fe46c` 작업의 GPT 응답은 대화에 완료되어 있었다. Extension 전송 성공 단계가 `RESPONSE_START`에 머무르고 응답 감시기는 `WAIT_RESPONSE`만 처리하는 상태 전이 불일치를 확인해 Extension/Worker 기대 빌드를 `2026-09-23.5`로 수정했다. 복구 시 기존 사용자 메시지를 기준점과 비교해 중복 전송 없이 응답 감시를 재개하도록 했다.
- Debug build는 Windows SDK 경로 접근 제한으로 기본 권한에서 실패했으나 권한 확장 실행에서 성공(경고 0/오류 0), 전체 테스트 10개 통과, Extension `node --check` 및 `git diff --check` 통과. Release 게시와 작업 폴더 복사도 완료했고 게시본/`C:\AI-AGENT\Worker`/`C:\GameProject` EXE SHA-256이 일치한다.
- 복구 확인 중 GPTWeb-Hub의 `업데이트` 버튼을 눌렀고 기존 작업은 Bridge에 `FAILED`, `finishReason=extension_reset`으로 기록됐다. 응답은 Worker에 전달되지 않았다. Bridge는 현재 `ready`; Chrome extension은 `2026-09-23.4`, Worker 기대값은 `.5`로 동기화 `false`다.
- 코드 수정·게시 성공과 실제 기존 응답 회수 성공을 구분한다. 이미 종료된 FAILED task는 현재 API에서 재개되지 않으며 기존 대화 메시지를 재전송하지 않았다. 후속 확인은 설치된 Chrome 확장을 `.5`로 갱신한 뒤 별도 시험 요청으로 Worker 수신을 확인해야 한다.
- 잔여 식별자: **09-B GPT Web 응답 회수 E2E 및 extension reset 복구 정책 확인**, **09-B Explorer 화면 검증**, **09-C 증거 전달·AC 고정**, **07 기존 검증 잔여**.

## 2026-09-23 Footer metadata parser 보강 및 Worker startup cleanup 수정

- Footer v1의 고정 routing marker를 유지하고 NOUL의 claim/evidence/scope/counterexample continuation을 parser가 JEV instructions에 보존하도록 했다. 계약 문서와 Master 예시를 함께 갱신했으며, evidence bundle 전달은 09-C로 남긴다.
- BridgeServer의 HttpListener 시작 실패 후 Dispose 과정에서 발생한 ObjectDisposedException이 원래 시작 실패 원인을 덮는 것을 확인해 cleanup을 listening 상태에 맞게 수행하도록 보강했다.
- `dotnet test ProjectHub.sln --configuration Debug --no-restore` 통과: Core 1, Agent 3, Server 1, Worker 6. 기본 샌드박스는 Windows SDK 경로 접근 거부로 실패해 동일 명령을 권한 확장으로 재실행했다.
- Release 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 완료. 게시 실행본은 SHA-256 일치 확인.
- 일반 권한 실행에서 WPF 프로세스는 유지되고 MainWindowHandle이 생성됐지만 Bridge `127.0.0.1:43821/bridge/status` 연결은 거부됐다. UI/Bridge E2E는 미완료이며 startup 원인은 추가 관찰이 필요하다.
- 잔여: **09-B Explorer Judge OFF/ON 및 Bridge 왕복 확인**, **09-C evidence bundle/AC**, **07 기존 검증**. commit/push는 수행하지 않았다.

## 2026-09-23 Worker 실행 불가 신고 후속 확인

- 게시 EXE를 일반 권한으로 실행해 process와 ProjectHub Worker 1200x1050 main window가 생성되는 것을 확인했다. 따라서 실행 파일 자체의 즉시 크래시는 재현되지 않았다.
- Bridge `127.0.0.1:43821/bridge/status`는 연결 거부. `netsh http show urlacl`에서 해당 주소 예약이 없음을 확인해 일반 사용자 `HttpListener.Start()`가 막히는 원인으로 판명했다.
- `netsh http add urlacl url=http://127.0.0.1:43821/ user=DESKTOP-OJJF37U\ornit`를 시도했으나 관리자 권한 필요 오류(5)로 적용되지 않았다. OS 설정 변경은 관리자 승인 후 가능하다.
- 결과: GUI 시작은 확인, Bridge/Extension 연동은 미복구. 관리자 권한 예약 추가가 잔여다.

## 2026-09-23 JEV PARTIAL 및 Codex 보완 지침 회귀 기준

- 유효한 JEV threshold 미달을 구현 FAIL로 단정하지 않고 `PARTIAL`로 분류한다. malformed/provider 응답은 기존처럼 ERROR다.
- 같은 Codex session에 구체 모순만 제한적으로 수정하고, 증거 부족은 결정적 검증으로 보완하며, confidence-only는 코드 변경 금지하도록 지침을 추가했다. threshold는 고정한다.
- Footer에서 `[QID:C2]`와 같은 선택적 질문 ID를 읽고, 보완 요청은 evidence 변경 영향을 받는 원자 질문만 재제출하도록 안내한다. 기존 ID 없는 양식은 순번 기반 C1… 할당을 유지한다.
- 같은 작업에서 PARTIAL 검증은 최대 3회이며, 세 번째에도 미통과면 `JEV_PARTIAL_LIMIT`로 Web 검토에 넘긴다.
- 새 mock 회귀 항목: LOW/MEDIUM/HIGH/CRITICAL threshold 동일 batch 판정, 실패 QID만 PARTIAL에 표시, QID 보존·중복 거부, retry prompt의 PASS 보존·threshold 고정 지침.
- 09-C 잔여: 실제 question→evidence content/digest 연결, 이전 질문 PASS 보존을 포함한 결과 영속화 및 evidence 변경 시 참조 질문만 재판정하도록 Worker가 기계적으로 강제하는 기능. 이번 prompt는 이 규칙을 Codex에게 지시하며 evidence envelope은 아직 구현하지 않는다.
- 검증: `dotnet test ProjectHub.sln --configuration Debug --no-restore` 성공(기본 권한 실행은 SDK 경로 권한 거부; 권한 확장 재실행에서 Core 1, Agent 3, Server 1, Worker 10 통과).

## 2026-09-23 설정 폼·작업표시줄 아이콘 후속 반영

- 사용자가 승인한 폼을 따라 저장소/폴더, AI모델 설정, 판단 AI 주요 표기를 한글화했다. 설정창 상단 상태 카드 요약을 접고 서버 상태 카드를 서버 주소 행에 배치했다. JEV의 endpoint/timeout 입력은 화면에서 감추고 JSON 설정 테스트 버튼을 노출했다.
- 설계·관제 provider 선택에 따라 Web 카드와 Codex 스레드 카드 표시를 전환한다. GPT Web에는 Web 아이콘, 스레드에는 말풍선 아이콘, Server에는 서버 형태 아이콘을 사용했다. Window.Icon에 프로그램 worker-icon.png를 지정했고 EXE ApplicationIcon은 worker-icon.ico로 유지한다.
- `dotnet test ProjectHub.sln --configuration Debug --no-restore`: Core 1, Agent 3, Server 1, Worker 23 통과. `node --check extension/gptweb-hub/content.js`, `git diff --check` 통과. Release 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 완료. SHA-256 세 곳 일치: `00722E076184D29F8CA2C86601F05FE0839103F920C1F1E34E686A6A9AF8DEA7`. 배포본 기동 시 `ProjectHub Worker` 창과 정상 프로세스를 확인했다.
- 현재 커밋: `5460d39` (소스 변경). 원격은 fetch 후 동일 기준점임을 확인했고 clean 상태에서 `git pull --rebase origin main` 완료. 문서 변경은 별도 커밋 예정.
- 잔여: Web coordinator 실제 실행 경로는 CLI-to-CLI에 연결되지 않아 Web 선택 시 현재 preflight가 막는다. 구현 역할별 스레드 카드(implementer/high-level)와 고수준 작업 AI 카드는 아직 없다. 이 UI 변경은 부분 반영이며 11-A/B/C 완료 상태를 변경하지 않는다.

## 2026-09-23 설정 화면 첨부 이미지 불일치 수정

- 첨부 화면에서 보인 오른쪽 카드 잘림/빈 공간은 1220px 팝업에 고정 6열을 배치해 생겼다. 창을 1400×900으로 조정하고 최대 크기를 1440×960으로 제한했다. 설정 팝업은 1400×840, 내부 세로 스크롤로 제한한다.
- 저장소 및 폴더를 상단에 두고, 서버 카드는 서버 주소 행에 정렬했다. 네 역할 행을 2줄 구조(서비스 제공자 위, 모델·추론 아래)와 동일 열로 맞췄다. 설계·관제 탭은 GPT Web/OpenAI Codex CLI이며 선택에 따라 Web/스레드 카드가 바뀐다. 작업 AI와 고수준 작업 AI에 현재 작업 폴더 내 스레드 선택 카드를 제공하고 역할별 세션/프로젝트 경로를 JSON에 저장한다. 고수준은 Astra/High, 기본 OFF.
- 기존 설정과 JSON을 호환하도록 coordinator transport 기본값 Web, implementer/high-level Codex CLI로 설정했다. Web coordinator, 활성 JEV, 활성 high-level은 아직 실행 경로가 없으므로 preflight에서 명시적으로 차단한다.
- 검증: 전체 테스트 29개 통과, Node 확장 구문 검사와 diff 검사 통과. WPF 빌드 결과 실행 시 앱이 살아 있고 제목 `ProjectHub Worker`를 표시했다. UI 자동 캡처 API는 현재 세션에서 네이티브 앱을 반환하지 않아 새 팝업의 화면 캡처 검증은 불가했다.
- Release 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 완료. 세 EXE SHA-256 일치: `3C2ECBECAF10DCDBF78162145D976A14AFCA211F94F3AB45E6A0A1835099AD99`. 배포본 실행 시 `ProjectHub Worker` 창 기동을 확인했다. 이번 후속 수정의 문서 갱신·커밋·푸시는 아직 완료 전이다.

## 2026-09-23 설정 폼 정렬·ComboBox 클릭 영역 후속 보정

- 서버 카드를 390×110px로 복구하고 랙 아이콘을 적용했다. 네 AI 역할의 왼쪽 제목/아이콘을 세로 정렬했으며, 판단 AI와 고수준 작업 AI의 사용 항목을 같은 그리드 열에 맞췄다.
- 판단 AI를 provider/model, 사용, 설정 JSON 테스트가 포함된 역할 카드 행으로 재구성했다. ComboBox 템플릿의 클릭 ToggleButton을 전체 컨트롤 면적으로 확장해 값 표시 영역 어디를 눌러도 목록이 열리도록 수정했다.
- 검증: Debug build 경고 0/오류 0, 전체 테스트 29개 통과, `git diff --check` 통과. Release 게시 및 Worker/GameProject 복사 후 SHA-256 일치 확인.
- 실제 팝업 클릭/화면 비교는 네이티브 앱 캡처 인터페이스 미노출로 검증하지 못했다. 이 UI 수용 검증은 사용자가 실행 후 확인할 화면상 잔여로 남긴다.

## 2026-09-23 설정 팝업 버튼 고정 및 하단 카드 디자인

- 고수준/판단 AI의 배경을 상단 카드와 같이 흰색으로 맞추고 테두리/카드 간격을 통일했다. JSON 설정 테스트 버튼은 옅은 파란색 강조 스타일로 변경하고 버튼 왼쪽의 중복 상태 문구를 숨겼다.
- 닫기/적용 버튼을 스크롤 영역 밖 하단에 고정했다. 850px 팝업 안에 들어오도록 설정 여백을 축소하고 스크롤바를 비활성화했다.
- 검증: Debug 빌드 경고 0·오류 0, 전체 테스트 29개 통과, `git diff --check` 통과. Release EXE 게시 및 두 작업 위치 복사, 세 파일 SHA-256 일치 확인.
- 실제 화면 캡처는 네이티브 앱이 UI 캡처 세션에 노출되지 않아 미실시.

## 2026-09-23 설정 카드·판단 콤보·헤더 이동 정렬

- 네 역할을 공통 흰 카드로 배치해 아래 두 카드와 상단 카드의 배경 차이를 제거했다. 판단 AI provider/model을 Typesafe/JEV 고정 ComboBox로 표시하고 저장 계약은 기존 `jev` provider를 유지한다.
- 설정 패널은 WPF Popup이라 기본 타이틀바 드래그가 불가능했다. 헤더 마우스 capture와 화면 좌표 offset 계산으로 패널 제목을 끌어 이동하도록 추가했다.
- 검증: Debug 빌드 경고 0·오류 0, 전체 테스트 29개, diff check 통과. Release 게시/Worker 및 GameProject 복사 후 SHA-256 일치.
- 실제 native 화면 capture는 미실시.

## 2026-09-23 AI 설정 콤보/카드 크기 통일 및 파란 패널 복구

- AI 모델 설정의 파란 외곽 배경을 복구하고 각 역할 안쪽에는 흰 카드를 배치했다. 우측 상세/스레드 카드와 판단 JSON 버튼은 폭 350px, 스레드 콤보는 260×30px로 맞췄다. provider/model은 같은 유동 열을 쓰고 reasoning 폭은 170px로 통일했다. 공통 콤보 글꼴은 Segoe UI 14px다.
- 검증: Debug 빌드 0 경고/0 오류, 전체 29개 테스트 및 diff check 통과. Release EXE 게시/복사 후 세 해시 일치.
- 실제 앱 캡처로 픽셀 정렬 확인은 불가.

## 2026-09-23 필수/선택 AI 카드 기준선 수정

필수 AI 그룹과 선택 역할 카드의 안쪽 패딩을 14×12px로 맞췄다. 설계·관제 AI의 추론 라벨을 14px로 통일했으며 판단 AI의 사용 여부를 체크박스 첫 행으로 정렬하고 JEV를 기본 선택했다. 빌드 경고·오류 0, 전체 29개 테스트와 diff check 통과. 네이티브 화면 캡처는 미지원.

## 2026-09-23 역할 기준선 정렬 후속 확인

선택 영역의 blue padding을 필수 역할 그룹과 같은 14×12px로 통일했다. 설계·관제 추론 폰트를 14px로 맞추고 판단 AI 사용 여부를 checkbox 행에 정렬했으며 JEV 기본값을 지정했다. 빌드 오류 0, 전체 29개 테스트 통과. Worker/GameProject 게시본 일치 해시 `FF8AE9159BACCD88AB9C7BFE92F8E05E8BAE2280751E5CE4E6EB1F9EBE238867`. 실행본 기동 및 Bridge ready 확인, 설정 팝업 시각 검증은 미수행.
