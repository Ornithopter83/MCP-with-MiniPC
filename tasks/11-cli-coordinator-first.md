# 11 Coordinator-first CLI-to-CLI

Updated: 2026-09-23

## Goal

Add a coordinator-first CLI workflow while preserving the existing Codex → GPT Web workflow and its public ACTION/NEXT contracts.

## Policy boundary

- Required roles: coordinator and implementer, each with independent model/reasoning settings and independent CLI sessions.
- Coordinator is read-only. Implementer receives workspace-write access only to the selected Working Folder.
- Use the Codex CLI model catalog and per-model reasoning capabilities. Do not substitute an unavailable model silently or use paid/alternate fallback.
- One coordinator-issued work card per user request. Reject malformed or incomplete cards before starting the implementer.
- The implementer runs only the work card's listed validation commands. Worker checks command-execution events and exit codes; a model's prose claim alone is not execution evidence.
- Coordinator review is advisory, but Worker enforces implementer exit, required validation evidence, and a PASS result for every AC before marking the task complete.
- No automatic retry/escalation, JEV integration, crash recovery, external Git write, or Web contract change in 11-A.
- The latest feedback uses the former 10-B label for coordinator-first CLI-to-CLI; after Tasks 10-A/B/C completed, this is tracked as ProjectHub Task 11-A. The user-resolved 09-B Explorer E2E item stays out of regular management.

## A — Role settings and capability preflight (complete, 2026-09-23)

- Add CLI-to-CLI and Legacy Web execution modes; default new installs to coordinator-first while preserving an explicit legacy option.
- Persist independent coordinator/implementer provider, model, and reasoning settings. Existing settings deserialize safely. The UI currently exposes OpenAI Codex CLI only; an unknown saved provider is shown as unsupported and blocked rather than substituted.
- Read the local `codex debug models` catalog without logging or persisting its raw output; expose only listed models and supported reasoning efforts.
- Block execution for missing authentication, unsupported model/reasoning, missing Working Folder, or unsupported enabled Judge. Never silently substitute.
- Keep legacy bottom-bar model/reasoning controls and Web/JEV behavior available in Legacy Web mode.

## B — Coordinator work card and implementer execution (complete, 2026-09-23)

- Route the initial user request to the selected coordinator in a new read-only session.
- Require one schema-validated work card with goal, scope, atomic ACs, evidence requirements, and validation commands before any implementer invocation.
- Send only the user request, work card, and relevant local project context to the selected implementer in a separate new session with workspace-write sandboxing.
- Require a structured implementer result and capture role/model/session/usage separately.
- Capture command execution events from Codex JSONL and compare them to the declared validation commands.

## C — Coordinator review, UI flow, and regression (complete, 2026-09-23)

- Send implementer result and observed validation evidence to the same coordinator session in read-only mode.
- Complete only if CLI exit is successful, every required validator was observed with exit 0, and coordinator reports PASS for every AC without missing/duplicate IDs.
- Show role direction, phase, model, and decision in Current Task/MESSAGE. Cancel must stop the active role process and restore UI.
- Preserve Legacy Web ACTION/NEXT/JEV behavior.

## Verification

- Unit fixtures for catalog filtering/reasoning support, strict work-card/review parsing, command-execution extraction, and required-validation matching.
- `dotnet test ProjectHub.sln --configuration Debug --no-restore`
- `node --check extension/gptweb-hub/content.js`
- `git diff --check`
- Smoke-test only models explicitly shown as supported by the installed CLI; use a disposable workspace and record model IDs/usage. Do not claim GPT-6 Sol/Luna capability if the installed catalog omits them.
- Publish the Release executable and copy it to the configured Worker deployment locations after the build succeeds.

## Results

### 2026-09-24 11-C 후속 — 단계 카드·이력·관제 ACTION

- 현재 작업 단계만 색을 갖도록 카드 상태를 보정했다. 비활성 아이콘 원형의 대비를 높이고 활성 작업/판정 배경을 각각 녹색/노란색으로 변경했다. `HistoryEvents`는 시간순으로 끝에 추가하고 마지막 항목을 표시한다.
- 관제 REVIEW의 첫 유효행은 `[ACTION=CONTINUE]`, `[ACTION=PAUSE]`, `[ACTION=END]` 중 하나다. 그 뒤에는 기존 review 구조의 JSON을 반환한다. Web의 ACTION 의미를 적용하되 CLI REVIEW 본문은 원자 AC 검토 JSON이다. Worker는 모델 이름 대신 ACTION을 읽어 CONTINUE일 때 같은 카드·구현 세션으로 수정과 재검증을 진행하고, PAUSE는 종료/사용자 대기, END는 구현·실행 증거·모든 AC PASS를 확인한 뒤 완료한다. 부정확한 형식과 모순된 판정은 중단하며 자동 재작업은 최대 3회다. Web 계약과 Legacy Web 경로는 유지했다.
- 완료일: 2026-09-24. 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore`(경고/오류 0), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore`(39 passed), `dotnet publish src/ProjectHub.Worker/ProjectHub.Worker.csproj --configuration Release --no-restore` 성공. 읽기 전용 임시 Codex CLI 호출에서 유효한 `[ACTION=END]`/REVIEW JSON을 확인했다. 게시 EXE SHA-256 `1B894FD0F3CB9CEB7DC8C3067F924036F50F40E37511C051AABA6E85A87C82C2`. Computer Use 앱 목록에 Windows 앱이 없어 Explorer 화면·통합 작업은 미검증이고 사용자 선택에 따라 설치는 보류했다. 잔여 `11-C-UI-EXPLORER`, `11-C-ACTION-E2E`, `11-C-DEPLOY`(설치 보류).

### 2026-09-24 11-C 후속 — 관제 세션 연결과 validator 실검증

- 08:01 및 08:55 설치본에서 Sol PLAN exit 0 뒤 세션 ID 누락 차단이 반복됐다. 저장 설정의 `threadSessionId` 빈 문자열이 신규 세션 ID 추출을 가로막는 것이 확정 원인이었다. Runner와 관제에서 빈/공백 ID를 null로 정규화했다. LocalAppData에서 확인되는 사용자 프로필도 세션 루트 후보에 추가해 보조 복구를 강화했다.
- CLI 실행 진행 이벤트의 `exit_code: null`은 숫자로 읽지 않는다. validation gate는 명령의 부분 문자열 언급을 실행 증거로 인정하지 않고 정확한 명령 또는 최대 두 겹의 PowerShell/cmd wrapper만 비교한다. 실패 후 성공한 재시도는 PASS로 판정한다. 실행 출력의 제한된 요약을 Sol REVIEW에 전달하고 검증 이력도 실제 증거로 표시한다.
- 완료일: 2026-09-24. 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore`(0 warning/0 error), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore`(38 passed), `dotnet publish src/ProjectHub.Worker/ProjectHub.Worker.csproj --configuration Release --no-restore` 성공. 승인받은 Worker 설치본과 게시본 SHA-256 `CFCF23323352A51FA6975CC656F088DEC39E1F50961E6277672A24DF20EAA0CF` 일치. Explorer에서 직접 작성·전송한 작업이 Sol PLAN→Luna IMPLEMENT→동일 Sol 세션 REVIEW로 끝났고 `MODEL_ACCESS_OK`, 검증 PASS, `DONE · REVIEW ACCEPTED`를 화면과 `_20260924_091428.txt` transcript에서 확인했다. 잔여 식별자 `11-C-DEPLOY`, `11-C-LIVE-SESSION` 완료.

### 2026-09-24 재현 후속 — 서로 다른 Codex 세션 루트 검색

- 재현 transcript에서 PLAN은 정상 성공했지만 session ID 연결을 못 해 차단됐다. 동시각 rollout은 `USERPROFILE\.codex\sessions` 아래에 정상 기록되어 있었다. 앞 수정본이 설치되어 있었으므로 `CODEX_HOME`이 설정된 Worker 프로세스와 실제 CLI 기록 경로가 다른 경우가 남은 원인이다.
- `CODEX_HOME`, `USERPROFILE\.codex`, .NET user-profile 기반 경로를 모두 조사하고 경로 중복을 제거한다. 기존 rollout snapshot 비교, CLI source/originator, CWD, 시간대, 단일 후보 요건을 유지한다.
- 추가 회귀: CODEX_HOME과 실제 CLI 세션 경로가 다를 때 user-profile 루트에서 단일 rollout ID를 복구한다.
- 완료일: 2026-09-24. 검증 명령과 게시본/설치본 해시는 `CurrentWork.md` 재현 후속에 기록한다.

- Status: A/B/C complete (2026-09-23).
- Baseline recovery tag: `recovery/before-11a-cli-to-cli-2026-09-23` at `f501694469f2f1a590d1739be5e6039978c7ebde`.
- Initial local Codex model catalog snapshot omitted GPT-6 Sol/Luna. Follow-up check on `codex-cli 0.155.0-alpha.16` (2026-09-23) shows both as `visibility=list`, `supported_in_api=True`; direct ephemeral read-only calls to each model with `low` reasoning returned `OK`. Treat model availability as runtime catalog data; do not hard-code the initial snapshot.
- Implementation: expanded settings UI and persisted independent roles/modes; catalog-gated model/reasoning choices; coordinator read-only work-card call; implementer workspace-write call in its own session; same coordinator session read-only review; exact AC-set and observed validation command exit-code gates; role/model/reasoning/session and usage telemetry/message records; cancel and transcript cleanup.
- Verification: `dotnet test ProjectHub.sln --configuration Debug --no-restore` passed (Core 1, Agent 3, Server 1, Worker 23; total 28). `node --check extension/gptweb-hub/content.js` passed. `git diff --check` passed (line-ending normalization warnings only).
- CLI smoke: disposable temp directory; explicitly catalog-supported `gpt-5.6-sol` with `low`; structured output schema and session ID passed. Temporary workspace removed.
- Follow-up model support check: `gpt-6-sol` and `gpt-6-luna` both appeared in the current CLI catalog and each passed a direct `codex exec --ephemeral --sandbox read-only` call with `low` reasoning. This supersedes the earlier conclusion that these models were unsupported.
- Explorer: published `C:\AI-AGENT\Worker\ProjectHub.Worker.exe` launched with a responsive `ProjectHub Worker` window. Existing instance was stopped before launch.
- Release publish: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\src\ProjectHub.Worker\bin\publish-worker.ps1 -NoRestore` succeeded and copied the executable to `C:\AI-AGENT\Worker` and `C:\GameProject`.
- SHA-256 for the project publish executable and both deployment copies: `11E56140632E5BCCA5650B5310AC9C585532F606B25A8BC2A07466AB8FF5C8D1`.
- Next task candidate: 11-B JobRunner separation/restart recovery; not started in this task.

## 2026-09-24 follow-up — coordinator session ID recovery and flow pulse

- Investigated the Worker transcript `_20260924_010358.txt`: the Sol PLAN call returned exit code 0 and a work card, but its session ID was blank. The Worker correctly stopped before Luna because it could not guarantee that the later review would resume the same coordinator context. A matching Codex CLI rollout record existed for that time and working folder, so this was session-ID correlation failure rather than failure to produce a plan.
- Hardened JSONL `thread.started` extraction for UTF-8 BOM and property casing. If that event is absent, Worker compares pre/post-call rollout files and accepts only one newly created `codex_exec` record matching source, exact working directory, and call time. Zero or ambiguous candidates remain blocked; this preserves the same-session review contract.
- Added visible pulse/travel motion to the current-flow and pipeline arrows, including the one-edge Sol → Luna route and reverse Luna → Sol handoff.
- Verification: Debug build succeeded with 0 warnings/errors; all 34 solution tests passed (Worker 29, Core 1, Agent 3, Server 1); `git diff --check` passed. Release publish succeeded (SHA-256 recorded in `CurrentWork.md`). Explorer animation and live CLI roundtrip were not verified: Native desktop apps were unavailable, and no extra model call was issued.

### 2026-09-24 reproduction — profile path mismatch

- The user reproduced the block on the prior recovery build. The new transcript showed PLAN exit 0 and again stopped at session resolution. Its matching rollout existed under `C:\Users\ornit\.codex\sessions`.
- Worker had searched the .NET special-folder profile `C:\Users\CodexSandboxOffline` while the Codex CLI used `USERPROFILE=C:\Users\ornit`. Session root resolution now prefers `CODEX_HOME`, then the `USERPROFILE` environment variable, then the special-folder fallback.
- Verification: Debug build 0 warnings/0 errors; 35 tests passed (Worker 30, Core 1, Agent 3, Server 1); diff check passed. Release EXE hash `A379242F7027B5DA0443ADF5D20E5D3C22174B81F73B03B175E032B5AF1892C3`. Replacing `C:\AI-AGENT\Worker\ProjectHub.Worker.exe` was rejected by approval review because it requires explicit deployment authorization; installed copy and UI recheck remain pending.

## 2026-09-23 설정 선택 연동 후속 — 11-UI-A

- 사용자 정정: 역할 이름은 삭제 요청이 아니라 그대로 유지 요청이었다. 설계·관제 AI, 작업 AI, 고수준 작업 AI, 판단 AI 제목을 네 카드의 왼쪽 아이콘 위에 복원했다.
- 모델 목록이 한 개 이하인 상태에서 모델 콤보를 열면 CLI capability catalog를 재조회하고, 성공한 경우 모델별 reasoning 선택지를 갱신한다. 각 Codex 스레드 선택 콤보는 조회된 프로젝트/세션 전체를 표시하고, 선택한 프로젝트 경로를 메인 스레드/작업 폴더와 동기화해 설정 적용 시 저장한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` (0 warning, 0 error); `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` (29 passed); `git diff --check` passed.
- 남은 항목: 실제 Explorer 설정 UI에서 모델, reasoning, 스레드 변경과 적용 후 작업 폴더 저장을 확인한다. 이번 시점에서 origin/main과 동기화했고 최신 `GPT-Web-Feedback.md`를 읽었다.

## 2026-09-23 설정창 모델 목록 초기화 보완 (11-UI-A)

- CLI `debug models`의 현재 결과는 7개 모델이며 `CodexModelCatalog`가 제공하는 reasoning 목록도 모델별 지원값이다. 설정창이 카탈로그 조회가 끝나기 전에 표시될 수 있던 경로를 수정해, 설정창 열기에서 시작 설정 task를 기다린 다음 카탈로그가 0~1개일 때 재조회하고 UI 콤보를 다시 채운다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(0 warning, 0 error), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 성공(29 tests), `git diff --check` 통과.
- Explorer 설정창에서 실제 목록이 보이는지는 데스크톱 UI 접근이 없어 아직 검증되지 않았다.

## 2026-09-23 서비스 모델 enum 및 요청 파라미터 연결 (11-UI-A)

- 로컬 설치 Codex CLI의 `debug models`를 직접 조회한 현재 목록은 다음과 같다.

| 모델 enum / ID | 기본 추론 | 허용 추론 |
| --- | --- | --- |
| `Gpt6Astra` / `gpt-6-astra` | low | low, medium, high, xhigh, max, ultra |
| `Gpt6Sol` / `gpt-6-sol` | medium | low, medium, high, xhigh, max, ultra |
| `Gpt6Luna` / `gpt-6-luna` | medium | low, medium, high, xhigh, max |
| `Gpt56Sol` / `gpt-5.6-sol` | low | low, medium, high, xhigh, max, ultra |
| `Gpt56Terra` / `gpt-5.6-terra` | medium | low, medium, high, xhigh, max, ultra |
| `Gpt56Luna` / `gpt-5.6-luna` | medium | low, medium, high, xhigh, max |
| `Gpt55` / `gpt-5.5` | medium | low, medium, high, xhigh |

- UI는 enum 카탈로그에서 직접 채워져 CLI catalog 로딩 타이밍에 영향받지 않는다. `CodexModelRequest`는 `model=<id>&reasoning=<effort>` 형태로 두 설정값을 짝지으며, 실행 시 실제 CLI가 받는 `--model <id> -c model_reasoning_effort="<effort>"` 인수로 변환한다. 지원하지 않는 enum 조합은 요청 전에 거부한다. 기존 런타임 CLI capability 검증은 추가 안전 확인으로 유지한다.
- 검증: 새 테스트에서 모델 7개, `gpt-6-luna/ultra` 불허, query 문자열 및 실제 CLI 인수 구성을 확인; 전체 30개 테스트와 Debug 빌드 통과. Explorer UI 화면 검증은 도구 제약으로 수행하지 못했다.
- Release EXE 게시 및 두 Worker 배포 폴더 복사 완료. 세 SHA-256 일치: `6C14DC32491815EF2C21CAD65EF7C534733598580B1B03B27752DC25C25BACD7`.

## 2026-09-23 팝업 footer·JEV endpoint 결과 표시 (11-UI-A)

- 설정 팝업 `VerticalOffset`을 -60px로 조정해 footer 버튼을 위로 이동했다.
- 판단 AI의 비어 있던 모델 행 오른쪽에 endpoint 테스트 상태를 노출한다. 유효 응답은 파란 `Endpoint 응답 확인 완료`; HTTPS 검증 실패, provider 오류, 예외는 빨간 `Endpoint 확인 실패`다. 상세 결과는 tooltip과 MESSAGE에 표시하며 테스트 중 버튼을 비활성화한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 통과(경고 0/오류 0); 전체 30개 테스트 통과; `git diff --check` 통과. 실제 픽셀 배치 검증은 데스크톱 UI 접근 부재로 미실행.
- Release 게시 및 두 배포 경로 복사본 SHA-256 일치: `2686313D9B80FF02DD507CD10DF330D0999B99A40769A03018318E385004727D`.

## 2026-09-23 설정 UI 후속 통합 정리 (11-UI-A)

- 역할 제목 4개 보존, 설정 카드와 콤보 연동, 현재 서비스 모델/추론 enum, CLI 요청 인수 조합, JEV Endpoint 테스트 결과 표시, 팝업 상향/footer 노출까지 최근 후속 커밋(`36af426`–`22b2262`)을 한 범위로 정리했다.
- 검증 기준은 Debug build 경고/오류 0, 전체 30개 테스트, diff check 및 게시 EXE 3개 SHA-256 일치다. 실제 Explorer 팝업 화면에서 항목 선택, 버튼 노출과 JEV 결과 표시를 확인하는 수용 검증은 남아 있다.
- 동기화 확인: `git fetch origin` / `git pull --rebase` 후 최신 GPT-Web-Feedback 확인. 추가 충돌 없음.

## 2026-09-23 설정 폼·창 아이콘 후속 반영 (11-UI-A, 부분 반영)

- 사용자가 승인한 설정 폼을 반영해 저장소/폴더 및 AI모델 설정 제목과 주요 라벨을 한글화했다. 상단 상태 요약은 설정창에서 접고 서버 상태 카드를 서버 주소 행에 두었다. 판단 AI는 Typesafe/JEV로 표시하고 Endpoint·제한 시간 입력은 감춘 채 JSON 설정 테스트 버튼을 노출했다.
- 설계·관제 AI의 OpenAI Web/Codex CLI 선택에 따라 우측 Web/스레드 카드가 바뀌도록 했고, GPT Web·스레드 아이콘을 구분했다. WPF Window.Icon에 worker-icon.png를 지정했다. EXE ApplicationIcon 설정은 기존 worker-icon.ico를 계속 사용한다.
- 검증: `dotnet test ProjectHub.sln --configuration Debug --no-restore` 통과(Core 1, Agent 3, Server 1, Worker 23), `node --check extension/gptweb-hub/content.js`, `git diff --check` 통과. Release 게시/복사 후 세 EXE의 SHA-256은 `00722E076184D29F8CA2C86601F05FE0839103F920C1F1E34E686A6A9AF8DEA7`이며 배포 EXE가 ProjectHub Worker 창으로 실행되는 것을 확인했다.
- 제한/잔여: Web 선택에 따른 coordinator-first Web 실행 경로는 아직 구현되어 있지 않아 현재 CLI-to-CLI preflight가 차단한다. 역할별 thread 선택은 coordinator 카드만 표시되며 implementer/high-level 역할 카드 및 스레드 설정은 미구현이다. 따라서 UI 폼은 부분 반영으로 기록하며, 이를 11-A/B/C 완료로 승격하지 않는다.

## 2026-09-23 화면 불일치 수정 (11-UI-A 후속)

- 첨부 화면에서 발생한 우측 카드 잘림 원인은 1220px 팝업 안에 고정 6열을 넣은 레이아웃이었다. 창 기본 크기를 1400×900, 최대 1440×960으로 제한하고 설정 팝업은 1400×840로 확장했다. 모델/추론을 공급자 아래 두 줄로 재배치하고, 네 역할 모두 왼쪽 아이콘/이름, 중앙 선택값, 오른쪽 상태/스레드 카드 형식으로 맞췄다.
- 설계·관제는 GPT Web/OpenAI Codex CLI 탭에 따라 대응 카드와 아이콘을 바꾼다. 작업·고수준 역할은 각각 현재 작업 폴더에 한정된 Codex 스레드 선택을 보여 주고 선택된 세션/프로젝트 경로를 JSON에 저장한다. 고수준 역할은 Astra/High 및 OFF 기본값이다. 기존 설정 파일에서 transport가 빠진 설계·관제 역할은 Web 기본값을 사용한다.
- 회귀 검사: 설정 기본값·transport·독립 threadSessionId/threadProjectPath를 확인하는 Worker 테스트를 추가했다. 현재 검증 결과는 전체 29개 통과, `node --check extension/gptweb-hub/content.js`, `git diff --check`, WPF 앱 시작 및 1400×900 선언값 확인이다.
- 제한: 현재 실행 엔진은 CLI-to-CLI이므로 Web 관제 선택, JEV 활성화, 고수준 역할 활성화는 실행 전 차단한다. 데스크톱 캡처 인터페이스가 현재 세션에 제공되지 않아 새 팝업의 Explorer 화면 캡처는 확인하지 못했다.
- Release 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 완료. 세 실행파일 SHA-256 일치: `3C2ECBECAF10DCDBF78162145D976A14AFCA211F94F3AB45E6A0A1835099AD99`. 배포본 `ProjectHub Worker` 창 기동 성공.

## 2026-09-23 설정 폼 정렬·클릭 영역 재수정 (11-UI-A)

- 서버 상태 카드를 폭 390px·높이 110px로 키우고 랙 형태 아이콘과 서버명/연결 상태를 배치해 저장소 설정 오른쪽 큰 카드로 복구했다.
- 설계·관제, 작업, 고수준 작업, 판단 AI의 왼쪽 제목·아이콘을 세로 구조로 통일했다. 판단 AI를 역할 카드 구조로 바꾸고 Typesafe/JEV, 사용 스위치, JSON 설정 테스트를 다른 행과 같은 열 기준으로 배치했다. 고수준 작업 AI의 사용 항목도 판단 AI의 사용 항목과 같은 열에 맞췄다.
- ComboBox 템플릿에서 드롭다운 ToggleButton이 전체 컨트롤을 덮도록 바꾸고, 투명 배경 Border가 전체 클릭 hit-test를 받도록 수정했다. 화살표 glyph 자체는 hit-test에서 제외했다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0); 권한 확장 실행의 `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 29개 통과; `git diff --check` 통과.
- Release 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 성공. 게시 실행본과 두 복사본 SHA-256 일치: `1D61AEFD08787B9CC057CDC31A69F6FE47404BFF7BA9E22FC6BBA86E2B4A34E1`.
- 제한: 네이티브 데스크톱 캡처 인터페이스가 이번 세션에 앱 창을 노출하지 않아 실제 설정 팝업의 화면 캡처/클릭 E2E는 미수행이다. XAML 컴파일과 앱 publish까지만 증거로 기록한다.

## 2026-09-23 설정 팝업 하단·카드 시각 정리 (11-UI-A)

- 고수준 작업 AI와 판단 AI 카드를 위쪽 역할 카드와 동일한 흰 바탕·테두리 스타일로 통일했다. 판단 AI의 JSON 설정 테스트 버튼을 옅은 파란 강조 버튼으로 다듬고 버튼 앞의 중복 상태 문구를 화면에서 제거했다.
- 닫기/적용 버튼을 스크롤 컨테이너 밖의 팝업 하단 행으로 이동했다. 팝업 높이는 850px로 고정하고 설정 영역의 상하 여백과 행 간격을 줄여 기본 화면에서 콘텐츠와 버튼이 함께 보이도록 했다. 내부 세로 스크롤 표시는 비활성화했다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 경고 0/오류 0; `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 29개 통과; `git diff --check` 통과.
- Release 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 완료. 세 실행 파일 SHA-256: `2495676D3EAB3DA2220E7A82A18AF86E261A0A94AA9E3FFE177C4F606DE6B58F`.
- 제한: 실제 Explorer 화면 배치 확인은 이번 세션에서 네이티브 앱 화면 캡처가 불가능해 수행하지 않았다. 크기 수용 여부의 최종 화면 확인은 실제 실행 화면 증거가 필요하다.

## 2026-09-23 설정 카드 정렬·판단 AI 콤보·팝업 드래그 (11-UI-A)

- 첫 두 역할만 둘러싸던 연한 파란 외곽 그룹을 제거하고, 네 역할을 동일한 흰 배경/테두리 카드로 정렬했다. 고수준과 판단 AI도 provider/model 콤보박스를 노출했고, 판단 AI 옵션은 Typesafe/JEV 고정 목록이다. Typesafe 표시는 설정 저장 시 기존 provider 값 `jev`로 유지한다.
- `Popup`에는 네이티브 타이틀바가 없어 제목 드래그가 동작하지 않았다. 제목 헤더에서 포인터를 캡처하고 화면 좌표 이동량을 Popup의 수평/수직 offset에 적용해 이동하도록 했다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 경고 0/오류 0; `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 29개 통과; `git diff --check` 통과.
- Release 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 완료. 세 EXE SHA-256: `79FFC737AFA8A09D15E217E803C17B68D20064AE328F128227DD050F9F236BFE`.
- 제한: 실제 창에서 카드/드래그 동작을 캡처로 확인하는 네이티브 UI 도구가 현재 세션에 없어 build/test 수준까지 검증했다.

## 2026-09-23 역할 콤보·카드 폭 및 파란 패널 스타일 재적용 (11-UI-A)

- AI 모델 설정의 파란 외곽 배경과 흰 역할 카드를 다시 적용했다. 고수준/판단 AI에도 파란 외곽 여백과 안쪽 흰 카드가 보이게 했다.
- 역할 중앙의 provider/model 입력은 동일한 유동 열 폭, reasoning 콤보는 170px 열로 정렬했다. 구현 provider 콤보가 reasoning 영역까지 늘어나지 않도록 model 열 폭으로 제한했다. Thread 콤보는 260×30px로, 우측 역할 카드/JSON 버튼은 350px로 맞췄다. ComboBox 공통 글꼴은 기존 Segoe UI 14px를 유지한다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 경고 0/오류 0; `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 전체 29개 통과; `git diff --check` 통과.
- Release 게시와 Worker/GameProject 복사 완료. 세 EXE SHA-256: `89EC339F70067D211C022BEF515405235C01586599BC825578D283CE6952E8FC`.
- 제한: 네이티브 앱 화면 캡처 미지원으로 최종 픽셀 비교는 미수행.

## 2026-09-23 필수·선택 역할 카드 정렬 보정 (11-UI-A)

- 고수준/판단 AI의 파란 바탕 안쪽 여백을 필수 AI 그룹과 동일한 14×12px로 맞춰 흰 카드 시작선과 내용 기준선을 정렬했다.
- 설계·관제 AI의 `추론` 라벨을 다른 역할과 같은 14px로 통일했다. 판단 AI `사용 여부` 라벨을 체크박스와 같은 첫 행으로 이동하고, 모델 JEV를 콤보 기본 선택값으로 지정했다.
- 검증: Debug 빌드 경고 0/오류 0, 전체 29개 테스트 통과, `git diff --check` 통과.
- 화면 캡처 검증은 네이티브 앱 접근 미지원으로 수행하지 않았다.

## 2026-09-23 역할 영역 기준선 세밀 정렬

- 선택 영역의 바깥 파란 카드 패딩을 필수 역할 그룹과 같은 14×12px로 설정해 안쪽 흰 카드 시작선을 맞췄다.
- 관제 AI `추론` 라벨은 14px, 판단 AI의 `사용 여부` 라벨은 checkbox와 같은 1행으로 정렬했다. JEV 모델은 콤보 기본값으로 선택되도록 설정했다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 29개 통과, `git diff --check` 통과. Release 게시 후 Worker와 GameProject에 복사했고 세 SHA-256 일치: `FF8AE9159BACCD88AB9C7BFE92F8E05E8BAE2280751E5CE4E6EB1F9EBE238867`.
- 파일 잠금 원인이던 기존 Worker는 Bridge에 active task가 없음을 확인하고 종료 후 갱신했다. 게시본 재기동 후 `ProjectHub Worker` 창과 Bridge ready/Web connected를 확인했다. 설정 팝업 픽셀 캡처는 이번 세션에서 미지원이다.

## 2026-09-23 설정창 입력 차단·카드 표시 후속 (11-UI-A)

- 팝업 표시 시 배경 차단 overlay를 먼저 켜고 키보드 포커스를 설정 탭에 둔다. 메인 창의 키 입력은 무시하고 설정 팝업이 열린 동안 메인 창 닫기 요청은 설정창만 닫는다. 설정 팝업의 Escape 닫기를 추가했다.
- coordinator가 GPT Web이면 모델 콤보를 비활성화한다. 서버 상태 카드는 랙/표시등 아이콘으로, Codex 스레드 카드는 대화 아이콘으로 바꿨다. 네 역할 제목 문구를 카드에서 제거하고 팝업 기본 글꼴을 14px로 통일했다.
- 검증: Debug build 경고 0/오류 0; 전체 테스트 29개 통과; git diff --check 통과. Release 및 Worker/GameProject 복사 성공, 세 SHA-256 0A219F77B9F83FC588D7E540F23F234DF4050B4929B745DCB0F26C2A7AC0BF69.
- 비고: Explorer 설정 팝업의 실제 시각 캡처 검증은 수행하지 않았다. 활성 실행 경로 잔여는 변경하지 않았다.
