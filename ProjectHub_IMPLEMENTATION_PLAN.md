# ProjectHub 구현 로드맵

Updated: 2026-09-18

## 목표

Mini PC의 ASP.NET Core 서버가 개발 PC Agent들의 Git·활동 상태를 수집하고 Supabase에 영속화하는 ProjectHub v0.2를 구축한다.

흐름: `Agent → ProjectHub.Server → ProjectService/Infrastructure → Supabase`

## 작업지시서

| No. | 작업 | 목적 | 상태 |
| --- | --- | --- | --- |
| 01 | [목표와 개발 기준](tasks/01_goal-and-standards.md) | 요구사항·보안·수용 기준 확정 | 대기 |
| 02 | [개발환경 기반구축](tasks/02_environment-foundation.md) | 솔루션과 프로젝트 골격 구성 | 완료 |
| 03 | [Server 스켈레톤](tasks/03-server-skeleton.md) | `/api/status`와 설정 기반 마련 | 완료 |
| 04 | [Supabase 스키마와 저장소](tasks/04_supabase-schema.md) | 중앙 상태 저장 계층 구현 | 완료 |
| 05 | [Agent heartbeat와 상태수집](tasks/05-agent-state.md) | PC·Git 상태 수집 | 완료 |
| 06 | [Large Data/NAS](tasks/06-large-data-nas.md) | 대용량 데이터 계약·Gateway·업로드 | 완료 |
| 07 | [프로젝트 배포 패키지](tasks/07-project-deployment-package.md) | Setup·Sync·Restore | 검증 중 |
| 08 | [Server 설치·이전](tasks/08-server-installation-migration.md) | Windows 11+ 재설치·연결 가이드 | 대기 |

## 순서

01 → 02 → 03 → 04 → 05 → 06 → 07 → 08 → 09

## 운영 규칙

- 번호 작업 하나, 세부 A/B/C 하나씩 진행한다.
- 완료 항목에는 날짜와 검증 근거를 남긴다.
- 새로 발견된 범위는 별도 후보로 기록하고 임의로 흡수하지 않는다.

## 변경 금지 경계

- v0.2의 사용자 명시적 `ProjectHub_Commit_Push.cmd`는 add/commit/fetch/pull --rebase/push 후 기존 Sync/checkpoint를 실행하고, `ProjectHub_Fetch_Pull.cmd`는 dirty·detached·충돌 상태를 자동 해결하지 않고 중단한다. reset/checkout/원격 shell/자동 빌드/MCP/자체 PostgreSQL·Redis·Docker는 구현하지 않는다. Large Data/NAS는 명시적 assertion·scope·Gateway 계약 안에서만 구현한다.
- Service Role Key와 Agent API Key는 저장소에 기록하지 않는다.

## 현재 상태

현재 작업: GPTWeb-Hub Worker — 실제 GPT Web 다중 왕복 및 파일 전달 안정화

잔여 작업: 실제 화면 자동화 런타임이 복구되면 동일 시나리오를 화면 캡처로 재확인한다. 코드 경로와 실제 Worker/GPT Web 로그 왕복은 완료됐다.

대용량 data plane은 `Agent → NAS Gateway → NAS1DUAL`, control plane은 `Agent → ProjectHub.Server → Supabase`로 분리하며 Server는 대용량 binary를 relay하지 않는다.

06 완료 기준: 정상 Agent는 대용량 파일을 자동 hash/upload/reconcile하지 않고 heartbeat·Git 상태만 관찰한다. 사용자가 `ProjectHub_Sync.ps1`을 명시적으로 실행하면 시작 시점의 고정 manifest와 control-plane 상태를 먼저 반영하고 별도 uploader가 동일 object의 기존 resumable session을 우선 재사용한 뒤 hash, assertion 갱신, chunk/status/resume/finalize, NAS identity 확인, Supabase STAGED 및 commit SHA 기반 CHECKPOINTED 기록을 수행한다. 업로드 중 파일이 변경되면 `CHANGED_DURING_UPLOAD`으로 제외하며 Git 변경은 자동 수행하지 않는다.

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

## 2026-09-20 C:\GameProject 게시 실행 기준
- Release 게시 산출물은 `C:\GameProject\ProjectHub.Worker.exe`에 항상 복사하고, 실행과 실검증도 해당 파일을 사용한다.
- 게시 후 복사 대상은 self-contained 단일 파일인 `src/ProjectHub.Worker/bin/ProjectHub.Worker.exe`로 고정한다.

## 2026-09-20 확장 빌드 일치 기반 GPT Web READY
- Worker는 GPT Web heartbeat의 확장 버전·빌드 식별자가 내장 기대값과 일치할 때만 GPT Web 카드를 READY로 판정한다.
- 불일치 시 `UPDATE REQUIRED`를 표시하고 `Run Task`를 비활성화한다.

## 2026-09-20 루트 bin 게시본 복사 고정
- Release 게시 후 반드시 `src/ProjectHub.Worker/bin/ProjectHub.Worker.exe` 단일 파일을 `C:\GameProject\ProjectHub.Worker.exe`로 복사한다.

## 2026-09-20 전송 버튼 활성화 대기
- GPT Web 전송은 disabled 버튼을 클릭하지 않고, 현재 composer에 연결된 활성 버튼을 찾을 때까지 감시한다.

## 2026-09-20 ACTION 판정 범위 수정
- ACTION 제어행은 응답의 첫 번째 유효행만 검사하며, 본문 속 ACTION 예시나 인용은 제어행으로 해석하지 않는다.

## 2026-09-20 Worker UI 레이아웃 정리
- 상태 카드는 설정 팝업에서 표시하고, 메인 작업 영역은 MESSAGE와 COMMAND의 실행 상태별 확장·접힘을 적용한다.
- 빌드는 수행하되 실행 중 게시 EXE를 종료·복사하지 않는다.
