# ProjectHub 구현 로드맵

Updated: 2026-09-24

## 2026-09-24 11-UI-B 후속 — 현재 작업/이력 카드 역할 스타일 통일

이력 카드의 역할별 배경·아이콘 배경·글자색이 현재 작업 카드와 달랐던 문제를 수정했다. 단일 역할 팔레트를 두 화면에 공통 적용하고 Coordinator 아이콘은 GPT Web/CLI 설정과 동기화한다. 네 역할의 팔레트/아이콘 자산 회귀 테스트를 추가했다. Debug 빌드 경고 0/오류 0, 전체 53개 테스트 통과, diff check 통과. Release 게시·`C:\AI-AGENT\Worker` 복사 완료(SHA-256 `C0319B39853698CB309829C54DEA00F41FB879032D452EBFDC93FBE523EBA14A`), 커밋 `27f0c4d`. 남은 작업: `11-UI-B-EXPLORER-COLORS`의 실제 Explorer 화면 검증.

## 2026-09-24 11-UI-B 후속 — 초기 입력 대기 pipeline 카드 색상

시작 직후 NewTaskInput + Idle 상태에서는 설계 관제·작업·고수준 작업·판정 카드 모두 컬러와 full opacity로 표시한다. 실행 진입 시에는 기존 현재 단계 강조/비활성 역할 그레이스케일 규칙으로 돌아간다. 요구는 기존 UI-B 기록에 있었지만 MainWindow의 실제 색상 계산에 초기 입력 상태가 빠져 있던 불일치를 수정했다. Debug 빌드 0 경고/오류, 전체 49개 테스트, diff check 통과. Release 게시/설치 복사 완료(sha256 `E7D0567D6C7B85D68D448E1D60CCA2BEDE363D29800EFACE62A911BF7C02E22A`). 잔여 `11-UI-B-EXPLORER-COLORS`.

## 2026-09-24 11-C 후속 — 계약 라우터 및 ACTION=HQ 통합

동기화된 최신 GPT-Web-Feedback의 Worker 의미 판정 제거를 적용했다. 새 CLI 경로는 첫 ACTION/NEXT 제어행만 파싱하고 역할 응답 본문은 불투명하게 전달한다. 작업카드/구조화 보고/AC·검증증거 gate, 별도 IMPLEMENT_ROUTE 호출, 고정 3회 제한을 제거했다. 사용자 지정 6번은 `[ACTION = HQ]`로 통일해 현재 관제 역할에 `message_type` envelope로 전달하고 타입은 관제 호출 루틴이 해석한다. High-level 활성 라우트와 JEV raw-response adapter를 연결하고 요청자 AI의 같은 세션으로 판정 원문을 복귀시킨다. Debug 빌드 0 경고/오류, 전체 46개 테스트, diff check 통과. Release 게시 및 `C:\AI-AGENT\Worker` 복사 성공(동일 SHA-256 `40315A0D98DCFE88F41764CF5AB7FC485B60335EC8894823F202DC010452ED06`). `C:\GameProject`는 존재하지 않는다. 잔여: `11-C-ROUTER-EXPLORER`, `11-C-WEB-HQ-LOOP`.

## 2026-09-24 11-C 후속 — transcript·검증 증거·판정 보고 연결

첨부 09:50 transcript는 유효한 UTF-8이었고 작업 JSON의 기본 `\\uXXXX` 이스케이프가 가독성 문제였다. transcript JSON을 한글 그대로 기록한다. 검증 명령은 첫 시도 exit 1 뒤 성공한 exit 0 명령을 CLI 이중 PowerShell wrapper의 이스케이프 따옴표 때문에 놓쳤다. wrapper를 풀고 전체 명령 비교를 유지한다. 작업 AI는 구조화 결과 후 같은 세션의 읽기 전용 Footer 턴에서 관제 보고 또는 JEV 요청을 선택한다. JEV가 요청되면 실제 명령 종료 증거와 함께 판정하고 결과를 같은 작업 세션으로 돌려보낸 뒤 같은 Sol REVIEW에 전달한다. Worker는 판정 미통과 시 END를 거부한다. 완료일 2026-09-24, Debug 빌드 경고 0/오류 0, 전체 42개 테스트와 diff check 통과. 설치본 교체 없이 코드만 유지하므로 `11-C-FOOTER-EXPLORER`, `11-C-JEV-LIVE`, `11-C-DEPLOY`가 잔여다. 상세는 CurrentWork와 task 11을 참조한다.

## 2026-09-24 11-C 후속 — 현재 단계 UI와 ACTION 제어

현재 단계만 컬러로 표시하고 비활성 아이콘 배경의 대비를 높였다. 작업은 녹색, 판정은 노란색 활성 팔레트로 바꿨고 대화 이력은 아래로 추가한다. Web의 첫 줄 ACTION 형식을 참고해 CLI 관제 REVIEW가 `[ACTION=CONTINUE|PAUSE|END]` 첫 줄과 REVIEW JSON을 반환하도록 했다. Worker는 모델과 무관하게 ACTION을 파싱해 최대 3회까지 같은 작업 카드의 재작업을 이어가거나, 사용자 판단을 기다리거나, 필수 검증 증거와 모든 AC가 PASS일 때만 완료한다. Debug 빌드 경고/오류 0, 전체 39개 테스트 통과, Release 게시 성공(게시본 SHA-256 `1B894FD0F3CB9CEB7DC8C3067F924036F50F40E37511C051AABA6E85A87C82C2`). 읽기 전용 CLI 모델 호출에서 ACTION 첫 줄과 유효한 END/ACCEPT JSON을 확인했다. Explorer 앱 제어가 노출되지 않아 UI·통합 왕복은 미검증이고, 사용자 선택에 따라 설치본 교체는 보류했다. 잔여 `11-C-UI-EXPLORER`, `11-C-ACTION-E2E`, `11-C-DEPLOY`(설치 보류); 상세는 CurrentWork와 task 11을 참조한다.

## 2026-09-24 11-C 후속 — 관제 세션 연결과 검증 증거

08:01 설치본에서 Sol PLAN은 exit 0이었지만 Luna 전에 세션 ID 연결이 실패했다. 설정의 `threadSessionId`가 null이 아닌 빈 문자열이었고 Runner의 null 병합과 관제의 `??=`가 이를 기존 세션으로 취급한 것이 확정 원인이었다. ID를 정규화하고, CLI 진행 이벤트의 `exit_code: null` 파싱을 보정했다. 검증 명령은 부분 문자열 언급을 제외하고 최대 두 겹의 shell wrapper를 풀어 정확히 비교한다. 검증 명령 출력 요약을 Sol REVIEW에 전달하며 이력 판정도 실제 증거와 일치시켰다. LocalAppData 프로필 세션 루트 검색은 보조 복구 경로로 유지한다. Debug 빌드 경고/오류 0, 전체 38개 테스트 통과, Release 단일 파일 게시 성공. 사용자 승인을 받아 Worker 설치본을 교체했고 게시본/설치본 SHA-256은 `CFCF23323352A51FA6975CC656F088DEC39E1F50961E6277672A24DF20EAA0CF`로 일치한다. Explorer 실작업에서 Sol PLAN→Luna IMPLEMENT→동일 Sol 세션 REVIEW, 검증 명령 출력 `MODEL_ACCESS_OK`, 검증 PASS 및 `DONE · REVIEW ACCEPTED`를 확인했다. 잔여 `11-C-DEPLOY`, `11-C-LIVE-SESSION` 완료. 상세는 CurrentWork를 참조한다.

## 2026-09-24 후속 — 역할 모델 선택과 실행 검사 일치

설정 UI와 별도 `codex debug models` 카탈로그 사이의 이중 capability 검사를 제거했다. CLI 실행용 모델/추론 선택은 설정과 요청 인수 조합에서 공통으로 쓰는 `CodexServedModels` enum에 맡기고, 폴더·provider·transport·인증 사전검사는 유지한다. 콤보에 enum 외 저장값을 임의로 노출하지 않는다. `gpt-6-sol / high` 회귀 검증을 추가했다. Debug 빌드(경고/오류 0), 전체 32개 테스트, Release 게시가 성공했다. 새 게시 EXE SHA-256은 `FC421C2E13D514CA37C50C388333C005C4385CEF5E0FD7FEE955EEBD86ED7F04`. C:\GameProject가 없어 자동 복사는 생략됐고, 실행 중 PID 27136의 이전 EXE 교체는 자동 검토가 거부해 잔여다. 미커밋 작업 때문에 pull/rebase·최신 피드백 확인·커밋/푸시는 중단했다. 상세는 CurrentWork 후속 기록을 참조한다.

## 2026-09-24 후속 — Coordinator session correlation 및 flow pulse

첨부된 실패 transcript에서 Sol PLAN은 정상 종료·계획 반환 뒤 세션 ID가 누락되어 같은 관제 세션 REVIEW를 보장할 수 없다는 이유로 Luna 호출 전에 차단된 것을 확인했다. JSONL 이벤트 파서를 보강하고, 이벤트가 없을 때는 호출 전 snapshot과 비교하여 동일 작업 폴더/시간/originator/source에 해당하는 새 Codex CLI rollout이 단 하나일 때만 세션 ID를 복구한다. 모호하면 fail-closed 상태를 유지한다. 파이프라인/현재 작업 화살표에는 한 구간과 역방향 전환에도 보이는 pulse를 추가했다. Debug 빌드 경고 0/오류 0, 전체 34개 테스트 통과, Release 게시 성공(게시 EXE SHA-256 `C92837826EEF2C52443C2B4EEBCD526DB8F6E0C27CCCF7BF464F9B1E2470C258`). `C:\GameProject`가 없어 자동 복사는 생략됐고, 활성 Worker PID 50960의 구버전 교체는 자동 검토에서 종료 요청이 거부되어 잔여다. Native desktop 앱이 없어 Explorer 화면 수용 확인은 수행하지 못했다. 상세는 CurrentWork 최신 후속 기록을 참조한다.

## 2026-09-24 재현 — Codex 세션 경로와 Worker 프로필 불일치

07:38 재현은 rollout이 `C:\Users\ornit\.codex\sessions`에 생성됐지만 Worker가 .NET special-folder 경로 `C:\Users\CodexSandboxOffline\.codex`를 검색해 복구하지 못했다. 이후 CODEX_HOME/USERPROFILE 경로 우선순위도 보정했으나 07:45에 CODEX_HOME이 실제 CLI 기록 경로와 다른 추가 사례가 재현됐다. 세 세션 루트를 모두 검색하도록 확장해 해결했고, Debug 빌드 경고 0/오류 0, 전체 36개 테스트 통과, Release 게시 및 `C:\AI-AGENT\Worker` 복사를 완료했다. 최초 수동 복사에서는 잘못된 framework-dependent EXE를 설치했지만 단일 파일 게시 EXE로 바로잡았다. 게시본/설치본 SHA-256 `843871D45623C8850090D6B0207C438B29E9D531F04579C5987E2E330A2F492B`. native 앱 제어가 제공되지 않아 사용자 데스크톱에서 창 실행 확인은 미완료다. 실제 PLAN→REVIEW 연속 확인도 잔여다. 상세는 CurrentWork 최신 기록 참조.

07:45 재현에서 A379 설치본도 계속 실패했다. 새 증거상 CODEX_HOME과 codex.exe가 실제 rollout을 기록하는 USERPROFILE 경로가 다를 수 있는데 기존 코드는 첫 경로만 검사했다. 현재는 세 후보 세션 루트를 모두 검색하고 기존 메타데이터/CWD/시간/단일 후보 조건으로 안전하게 판정한다. CODEX_HOME과 USERPROFILE이 다른 회귀 테스트 및 최신 게시/배포 결과는 CurrentWork의 07:45 후속에 기록한다.

## 목표

Mini PC의 ASP.NET Core 서버가 개발 PC Agent들의 Git·활동 상태를 수집하고 Supabase에 영속화하는 ProjectHub v0.2를 구축한다.

흐름: `Agent → ProjectHub.Server → ProjectService/Infrastructure → Supabase`

2026-09-22 확장 목표: 기존 v0.2 기반 위에 저비용 AI Role Dev Tool을 구성한다. 우선순위는 토큰 절약 → 목표까지의 지속 실행 → 역할·모델 교체다. 상세 설계는 [Master-Polish.md](Master-Polish.md)를 참조하며, 현재 실행 정책과 공개 계약은 유지한다.

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
| 09 | [AI Role Dev Tool 설계와 검증 기반](tasks/09-ai-role-dev-tool.md) | 저비용 역할 분담·지속 실행·JEV 검증 | A/B/C 구현 완료 (2026-09-23) |
| 10 | [검증 capability·사용량 관찰·JEV 기준실험](tasks/10-verification-observability.md) | 검증 범위 분리·실측 계측·통제 fixture | A/B/C 구현 완료 (2026-09-23; 실 provider benchmark 미실행) |
| 11 | [Coordinator-first CLI-to-CLI](tasks/11-cli-coordinator-first.md) | 독립 Sol 관제·Luna 작업 CLI와 역할별 설정 | A/B/C 완료 (2026-09-23) |
| 11-UI-B | [승인 이미지 기준 메인 화면](GPT-Web-Feedback.md#2026-09-23-메인-화면-시각-정합-후속--실제-구현-476541c-vs-승인-최종-이미지) | 승인 화면 비율·5단계 흐름·입력/이력 상태 연결 | K1–K25·초기 카드색/preflight·JEV 검사 JSON 기록 및 비차단 적용 반영; K26 Explorer 화면·K27 JEV 실검증 잔여 (2026-09-24) |

## 순서

01 → 02 → 03 → 04 → 05 → 06 → 07 → 08 → 09 → 10 → 11

## 운영 규칙

- 번호 작업 하나, 세부 A/B/C 하나씩 진행한다.
- 완료 항목에는 날짜와 검증 근거를 남긴다.
- 11-UI-B-A–H는 승인 화면, 단계/경로 표시, 설정 카드 연동, 원문과 분리된 최신순 HistoryEvent, idle 전용 입력과 TaskLaunchRequest 기반 Run/Cancel을 구성했다. 빈 입력/준비되지 않은 연결은 실행할 수 없다. `dotnet build ProjectHub.sln --configuration Debug --no-restore`, `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore`(30개 통과), `git diff --check` 통과. Release EXE 게시 및 Worker/GameProject 복사 완료, 세 파일 SHA-256 `8031046C35FFBACB9A93B7D6D489D97B65CD712614C1AE5BCD1D5792A015F858` 일치. Explorer 실행 파일이 PID 41236, 창 제목 `ProjectHub`로 살아 있는 것을 확인했다. CUA 앱 목록이 비어 있어 -J 화면 캡처와 실제 상호작용 검증은 잔여다.
- 2026-09-24 K1–K25 보정: header/footer와 card 비율, 5-column pipeline connector, grayscale/current-next style, enum 기반 단계, idle 입력 ↔ history 상태, 요약 필드/새 작업 상태를 반영했다. Debug build 경고 0/오류 0, 전체 30개 테스트 및 `git diff --check` 통과. Release 게시본과 GameProject 사본은 SHA-256 `9E52E2EA7017F331D266A8554558B6A0121C1016B97DD696876458694F1728FB`로 동일하다. Worker 배포 경로는 실행 중 프로세스가 잡고 있어 이전 바이너리이며, 종료 단계는 auto-review에서 상태 손실 위험으로 거부되었다. 네이티브 UI 도구가 제공되지 않아 Explorer 3상태 캡처(K26)와 별도 JEV 수용 검증(K27)은 잔여다.
- 2026-09-24 후속: 시작/새 작업 대기 입력 상태에서는 네 역할 카드를 모두 역할색으로 표시하고 실행 후에는 기존 current/next 색상 구분을 적용한다. 하단 실행 안내와 버튼 enabled 상태는 같은 preflight 함수에서 저장된 실행 설정/연결 상태로 계산하며, 설정 적용 즉시 갱신한다. build/test/diff 결과는 CurrentWork의 후속 기록을 참조한다.
- 2026-09-24 JEV 설정 후속: Judge 활성은 CLI-to-CLI 실행 preflight 차단 조건이 아니다. `judgeEndpointValidation`에 fingerprint/status/result/time을 저장하고 현재 설정에 대해 기록이 없거나 실패면 설정 적용 시 경고하되 적용은 계속한다. 빌드 경고 0/오류 0, 전체 32개 테스트 통과; 상세 검증은 CurrentWork 상단 기록 참조.
- Release 게시 및 `C:\GameProject` 복사 완료(동일 SHA-256 `376D4094D3609A518D4A964E54DBDDDDCF01DD045E0AB8C7F611432740AB5FC9`). 실행 중인 Worker 폴더는 자동 검토에서 기존 프로세스 종료를 거부하여 갱신하지 않았다.
- 새로 발견된 범위는 별도 후보로 기록하고 임의로 흡수하지 않는다.

## 변경 금지 경계

- v0.2의 사용자 명시적 `ProjectHub_Commit_Push.cmd`는 add/commit/fetch/pull --rebase/push 후 기존 Sync/checkpoint를 실행하고, `ProjectHub_Fetch_Pull.cmd`는 dirty·detached·충돌 상태를 자동 해결하지 않고 중단한다. reset/checkout/원격 shell/자동 빌드/MCP/자체 PostgreSQL·Redis·Docker는 구현하지 않는다. Large Data/NAS는 명시적 assertion·scope·Gateway 계약 안에서만 구현한다.
- Service Role Key와 Agent API Key는 저장소에 기록하지 않는다.

## 현재 상태

현재 작업: **11-C CLI-to-CLI 계약 라우터 후속 구현 완료(2026-09-24)**. 기존 11-A/B/C 완료 이력과 UI 설정 구현은 유지한다. 최신 router follow-up은 이전 고정 작업카드·AC/검증증거 gate를 대체하며, 첫 ACTION/NEXT만 해석하고 본문은 opaque handoff 한다. `[ACTION = HQ]`는 현재 관제 역할로 통일하고 message_type 해석은 관제 루틴에 둔다. High-level 활성 실행과 JEV raw-response adapter를 연결했다. Debug 빌드 0 warning/error, 전체 46 tests, Release 게시 성공(상단 후속 SHA 참조). 미완료: Explorer/확장 실브라우저 왕복(`11-C-ROUTER-EXPLORER`, `11-C-WEB-HQ-LOOP`) 및 설치 폴더 복사(`11-C-DEPLOY`, auto-review가 배포 파일 덮어쓰기를 거부). 09-B Explorer/E2E 제외 기준, 기존 Bridge 및 07 잔여는 유지한다.

실 provider JEV benchmark는 실제 API 요청과 사용량을 발생시키므로 실행하지 않았다. 회귀 fixture 구현과 자동화된 전체 검증은 2026-09-23에 통과했다. 사용자는 향후 benchmark 실행을 요청하면 별도로 실행할 수 있다. 기존 07 잔여는 계속 유지한다.

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
## 2026-09-20 Git 기반 Web review preflight gate

- Worker는 GPT Web task 생성 직전에 `git status --porcelain`, 현재 branch, local HEAD SHA, `git ls-remote origin refs/heads/<branch>`를 읽기 전용으로 확인한다.
- working tree가 DIRTY, detached HEAD, local SHA 미확인, remote 조회 실패, remote branch 없음, local/remote SHA 불일치일 때는 Web task를 만들지 않고 `PAUSE` 및 `FINISH_PAUSED`로 원인과 확인값을 표시한다.
- local/remote SHA가 일치하면 `REMOTE_CONFIRMED`이며 그 local SHA를 `review_commit_sha`로 확정하고 Worker → GPT Web prompt에 `REVIEW_SOURCE=GIT`, `REVIEW_COMMIT_SHA`, `SYNC_STATE`를 붙인다.
- Worker는 이 gate에서 commit, push, fetch, pull을 실행하지 않는다. push command 성공은 Worker가 관측하지 않으므로 `REMOTE_CONFIRMED (Worker did not run push)`로 구분한다.
- Server의 현행 state API는 `projectId`와 `workstationId`를 요구한다. Worker target 설정에는 이 식별자 계약이 없어 Server observed SHA 비교는 `UNAVAILABLE`로 표시하며 review 진행 조건으로 사용하지 않는다.
- 검증: `dotnet build ProjectHub.sln --configuration Debug --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 성공(총 5개).
- 잔여: Release 게시본 Explorer/Chrome에서 clean, dirty, remote SHA mismatch, remote-confirmed의 실제 UI/E2E를 확인한다.

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
## 2026-09-21 Contract Gate 반영

JEV Contract Gate의 고정 NEXT 라우팅과 임베디드 footer 틀을 Worker에 반영했다. 실제 provider adapter는 별도 계약과 승인된 endpoint가 확보된 뒤 연결하며, 그 전까지는 GPT Web fallback을 사용한다. 중간검증 없이 구현을 진행한 이번 변경의 최종 검증은 CurrentWork.md에 기록한다.

## 2026-09-21 JEV API Contract v1 반영

원격에 추가된 `JEV-API-CONTRACT.md`를 기준으로 실제 TypeSafe HTTP adapter, validation parser, structured answer mechanical evaluator, PASS 후 Codex report hop을 구현했다. 실제 API smoke test와 Explorer Judge ON/OFF E2E는 `TYPESAFE_API_KEY` 및 실행 환경 준비 후 남은 검증 항목이다.

## 단기 목표 — JEV AI 분기 안정화 (2026-09-22)

현재 우선순위는 UI 확장이나 범용 workflow engine이 아니라 Worker가 JEV 계약을 통해 Codex 결과를 정확히 분기하는 것이다.

완료된 구현:

- footer 임베딩 및 Codex prompt 주입
- 첫 유효 NEXT parser와 typed NOUL/SCORE/CHOICE parser
- TypeSafe `/v1/systemone` HTTP adapter와 환경변수 인증
- 구조화 응답의 기계적 threshold/allowed-choice 판정
- PASS 후 Codex 보고서 생성, FAIL 후 동일 session 재작업, 오류 fallback
- Judge OFF 기존 흐름 보존
- JEV 컬러/그레이스케일 아이콘 자산 반영

남은 검증:

- API key가 있는 환경에서 실제 smoke test
- Explorer 화면 기준 Judge OFF/WEB/JEV PASS/JEV FAIL/error fallback E2E

기준 문서:

- `GPT-Web-Feedback.md`: 최신 제품 피드백
- `src/ProjectHub.Worker/JEV-FOOTER-CONTRACT.md`: Codex footer 및 NEXT 계약
- `src/ProjectHub.Worker/JEV-API-CONTRACT.md`: TypeSafe API request/response 계약
- `CurrentWork.md`의 `CURRENT AUTHORITATIVE STATUS`: 현재 구현과 잔여 검증의 단일 요약

## 11-UI-A 화면 수용 후속 (2026-09-23)

최근 보정에서 서버 카드 크기를 390×110px로 복구하고 랙 아이콘을 추가했다. 네 역할의 왼쪽 제목/아이콘을 세로 정렬하고, 판단 AI와 고수준 작업 AI의 사용 항목 위치를 같은 열로 맞췄다. ComboBox 템플릿은 전체 컨트롤 면적이 클릭을 받도록 변경했다. WPF 빌드(경고 0/오류 0), 테스트 29개, diff 검사를 통과했고 Release EXE를 게시·복사했다. 실제 Explorer 팝업 화면 비교와 콤보 클릭 확인은 네이티브 앱 캡처 불가로 미실시 상태다.

## 11-UI-A 팝업 하단 가시성 후속 (2026-09-23)

고수준/판단 AI를 흰색 카드로 통일하고 테스트 버튼의 강조 스타일을 보완했다. 중복 상태 문구를 숨겼으며 닫기·적용 버튼을 스크롤 뷰 밖 하단 영역에 고정했다. 설정 여백을 줄이고 팝업 높이를 850px로 설정했다. 내부 스크롤은 비활성화했다. 빌드·전체 테스트·diff check 통과 후 Release 게시/복사 완료. 화면 캡처는 현재 세션에서 네이티브 창을 확인할 수 없어 수행하지 못했다.

## 11-UI-A 카드 통일과 패널 드래그 후속 (2026-09-23)

네 AI 역할 카드를 흰 배경으로 통일하고 판단 AI의 provider/model을 Typesafe/JEV 고정 콤보박스로 표시한다. 설정 provider 저장값 `jev`는 호환 유지한다. WPF Popup에 타이틀바가 없어 제목 헤더 드래그 이벤트로 화면 좌표 delta를 Popup offsets에 적용했다. 전체 빌드·29개 테스트·diff 검사를 통과하고 게시·복사를 완료했다. 실제 UI 포인터 조작 캡처는 현재 세션에서 불가하다.

## 11-UI-A 역할 입력·배경 스타일 보정 (2026-09-23)

네 역할 바깥의 파란 패널과 안쪽 흰 카드를 복원했다. provider/model 콤보는 동일한 중앙 열 폭, reasoning은 170px, thread 콤보는 260×30px, 오른쪽 상세 카드와 JSON 테스트 버튼은 350px로 통일했다. 콤보 글꼴은 공통 Segoe UI 14px. Debug 빌드, 29개 전체 테스트, diff check 통과 후 게시/복사 완료. 세 EXE 해시 `89EC339F70067D211C022BEF515405235C01586599BC825578D283CE6952E8FC` 일치.

## 11-UI-A 필수/선택 역할 기준선 후속 (2026-09-23)

선택 역할 카드 내부 여백을 필수 AI 그룹과 같은 14×12px로 맞췄다. 설계·관제 `추론` 글꼴 14px, 판단 AI 사용 여부 라벨을 checkbox 첫 행으로 정렬하고 JEV 모델을 기본 선택 처리했다. 전체 Debug 빌드 및 29개 테스트 통과. 실제 화면 캡처 확인은 도구 미지원으로 잔여다.

## 11-UI-A 역할 카드 기준선 최종 보정 (2026-09-23)

선택 영역의 바깥 파란 여백을 필수 AI 그룹과 같은 14×12px로 조정하고, 설계·관제 추론 라벨을 14px로 통일했다. 판단 AI 사용 여부를 checkbox 첫 행에 올리고 JEV combo 기본 선택을 지정했다. 전체 Debug build 및 29개 테스트 통과. 게시 EXE와 Worker/GameProject 복사본 해시가 `FF8AE9159BACCD88AB9C7BFE92F8E05E8BAE2280751E5CE4E6EB1F9EBE238867`로 일치하며 Worker 창/Bridge 기동 확인.

## 2026-09-23 설정창 포커스·역할 카드 표시 후속 (11-UI-A)

- 설정 팝업을 열 때 배경 overlay를 먼저 표시하고 포커스를 설정 탭으로 이동한다. 메인 창의 키 입력을 차단하고 설정 팝업이 열린 동안 닫기 요청은 팝업만 닫게 했다. 팝업의 Escape 닫기도 연결했다.
- GPT Web 탭에서 coordinator model ComboBox를 비활성화했다. Server 카드의 랙 아이콘과 Codex 스레드 선택 카드의 대화 아이콘을 교체했고, 네 역할 카드의 이름 라벨을 빼고 설정 팝업 TextBlock 기본 글꼴을 14px로 통일했다.
- 검증: Debug build 경고 0/오류 0, 전체 테스트 29개 통과, git diff --check 통과. Release 게시 및 C:\AI-AGENT\Worker, C:\GameProject 복사 완료; 세 EXE SHA-256 0A219F77B9F83FC588D7E540F23F234DF4050B4929B745DCB0F26C2A7AC0BF69. GUI 픽셀 비교는 수행하지 않았다.
