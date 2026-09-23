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
