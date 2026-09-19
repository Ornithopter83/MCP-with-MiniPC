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
