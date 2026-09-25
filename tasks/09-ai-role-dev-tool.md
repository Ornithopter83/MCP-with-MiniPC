# 09 AI 역할 개발 도구 설계와 검증 기반

갱신일: 2026-09-23

## 목표

토큰 절약을 최우선으로 무료 ChatGPT Web 관제 → Codex Luna Medium 구현 → JEV 판단 흐름을 정리하고, 작은 작업을 지속적으로 완성하는 기준을 세운다.

## 현재 기준

- 2026-09-23 사용자 결정에 따라 **09-B Explorer 검증 이슈는 해결로 기록하고 운영 backlog/정기 관리에서 제외**한다. 같은 문제는 향후 실제 재발 시 새 증거와 함께 다시 등록한다.
- 동기화 기준 HEAD `1b70b48`; 최신 피드백의 MiniStore 한계, 09-C 근거 출처 추적, 검증 계층 구분을 반영한다.
- 기존 활성 `07-project-deployment-package.md`의 잔여 검증은 보존하며 동시에 수행하지 않는다.
- 상세 설계와 복사 양식은 [Master-Polish.md](../Master-Polish.md)를 참조한다.

## 세부 작업

### A. 현재 구현 분석과 Master 설계 문서 — 완료 (2026-09-22)

- 현재 구현과 사용자 요구의 차이, 토큰 낭비 원인, 목표 역할·상태·복구 구조를 정리한다.
- 사용자/관제/구현/JEV 양식을 제공하고 현행 파서 호환 양식과 미래 API evidence 양식을 구분한다.
- 공개 계약과 제품 소스는 변경하지 않는다.
- 사용자가 승인한 Git 동기화·문서 commit/push 범위로 완료한다.

### B. JEV v1 계약 정합성과 라우팅 보수 — 구현 완료, 사용자 결정으로 해결 처리 (2026-09-23)

- 최신 피드백에 요구된 ERROR/FAIL 구분과 입력·응답 검증, REPORT 경계, PASS 재진입 차단을 먼저 구현한다.
- Worker 전용 파서/evaluator/라우팅 픽스처 테스트를 마련한다.
- 하단 계약의 NEXT/REPORT/VALIDATION REQUEST 고정 표식과 v1 라우팅은 보존한다. NOUL 질문은 원자 주장, 선택 중요도/evidence/scope/counterexample 메타데이터를 지원하며 파서가 연속 입력을 JEV instructions에 보존한다.
- 유효 응답의 임계값 미달은 PARTIAL로 분류한다. 같은 Codex 세션에서 실제 모순·증거 부족·confidence 미달·사용자 검증 필요를 먼저 구분하고, evidence가 바뀐 원자 질문만 기존 QID로 다시 평가한다. 최대 3회 후 PASS가 아니면 검토로 보낸다.
- 회귀 기준: 중요도별 고정 임계값, 독립 질문 묶음 판정, QID 유지·중복 거부, 임계값 미달 PARTIAL 분류, Codex 점수 맞추기 방지 지침을 픽스처로 검증한다.
- 파일 evidence 전달과 digest 기반 무효화 구현은 하위 09-C에 기록한다.
- Explorer 실검증 잔여는 사용자가 해결로 처리하고 이후 관리하지 않도록 결정했다. 실제 재발하면 별도 건으로 확인한다.
- `JevContract`가 첫 NEXT/REPORT 경계, NOUL·SCORE·CHOICE 구조·범위·허용값을 검증한다.
- `JevJudgeRunner`가 TypeSafe 응답의 누락·알 수 없는 ID·타입·범위 오류를 ERROR로, 유효하지만 임계값 미달인 답을 PARTIAL로 분류한다.
- 같은 Codex 세션에서 PARTIAL triage/revalidation을 최대 3회 수행하고, PASS report-only 단계를 거치며 report-only 단계의 JEV 재진입을 차단한다.
- 모의 HTTP 처리기 기반 Worker 픽스처 테스트를 추가했다.

### C. Evidence 전달·출처·무효화 — 구현 완료 (2026-09-23)

- `JevEvidenceEnvelope`이 작업/QID/근거 ID/종류/출처 버전/내용 요약 해시/실행기/상태/시각/발췌/scope/출처 추적를 구성한다.
- Codex가 반환한 실제 텍스트 파일 artifact(최대 32개, 파일당 64 KiB, 발췌 12,000자)를 제한적으로 전달한다. 크기·문자 인코딩을 확인하고 민감값 패턴을 마스킹한다. 실행 사실은 CLI artifact만으로 추정하지 않는다.
- 자유형 Codex 결과는 별도 `SUMMARY_ONLY` evidence로 분류한다. QID의 `EVIDENCE:` 선언에 직접 근거가 없으면 JEV 점수가 높아도 `PASS`로 완료하지 않고 `PARTIAL`로 보낸다.
- evidence 봉투 구조와 JEV 결과를 `%Worker%/state/jev-evidence/<job-id>/`에 round별 및 최신 스냅샷으로 원자 저장한다. 동일한 Worker 작업의 JEV round는 작업 ID를 공유한다.
- 같은 QID의 출처 버전, 질문/수용 기준 digest, 참조 evidence 내용 요약 해시가 바뀌면 해당 원자 질문의 이전 PASS를 무효화하고 PARTIAL 재검증으로 보낸다. 해당 QID가 이미 새 증거로 이번 묶음에 재검증됐으면 추가 재검증을 요구하지 않는다. 영향받지 않은 QID의 PASS는 다음 round에도 보존한다.
- ENGINE_HEADLESS/UI_BROWSER/HUMAN_UX/JEV 계층를 별도 상태로 기록한다. 아직 관측되지 않은 계층은 `NOT_RECORDED`로 둔다.
- footer v1 wire 표식과 Worker의 기존 라우팅 형식은 변경하지 않는다.

## 진행

- 완료: `09-A`, `09-B` 구현 및 사용자 해결 처리, `09-C` evidence 봉투 구조/출처 추적/저장·digest 무효화 구현.
- Task 10-A/B/C 후속 작업은 별도 task로 활성화해 구현 완료했다. 실 TypeSafe 음성 대조 측정은 외부 요청/사용량을 발생시키므로 실행하지 않았다.
- 07의 Force Restore GUI 계속/취소 등 기존 잔여 검증을 완료로 바꾸지 않는다.

## 변경 금지

- 09-B에서는 Worker 제품 코드, 하단 계약 문서의 v1 호환 clarification, 픽스처 테스트를 변경할 수 있다. 고정 NEXT wire 표식은 바꾸지 않는다. 브라우저 확장과 배포본은 변경하지 않는다.
- 기존 Agent → Server → Supabase와 NAS 경계를 보존한다.
- 현재 문서 push 승인을 향후 자동 Git/배포의 포괄 승인으로 사용하지 않는다.

## 완료 기준

- Master에 세 가지 우선순위, 코드 근거, 실행 단계와 검증 방법이 있다.
- JEV 복사 form이 실제 현행 파서를 통과한다.
- 구현 완료·과거 검증 이력·미구현 제안을 구분한다.
- 구현·픽스처 검증·문서 상태를 함께 기록한다. commit/push는 별도 사용자 승인 범위에서만 수행한다.

## 결과와 검증

2026-09-23 09-C: `JevEvidenceEnvelope`/`JevEvidenceArchive`를 도입하고 JEV 제공자 상태에 증거와 QID 매핑을 전달한다. Codex artifact와 질문에서 명시한 작업 폴더 내 텍스트 경로를 제한 수집하며, SUMMARY_ONLY만 연결된 명시적 EVIDENCE 질문은 PARTIAL 처리한다. 저장된 이전 round와 비교해 출처 버전, 질문 요약 해시, 관련 evidence digest 변경 시 해당 QID의 PASS만 무효화한다. 검증: `dotnet test ProjectHub.sln --configuration Debug --no-restore` 통과 (Core 1, Agent 3, Server 1, Worker 14). Release 게시와 `C:\AI-AGENT\Worker`/`C:\GameProject` 복사 후 SHA-256 `A15AA486FED4EEA9FC1576B7D1764F7A5B03F029C8FF903F249D597AB5AD4231` 일치, Bridge 준비 완료 및 Extension 동기화됨=true 확인. 실 제공자 호출은 하지 않았다.

2026-09-22 09-A: `Master-Polish.md` 작성, 구현계획·현재상태의 최신 요약 갱신. 비용 절약, 관제 우선 시작, 실제 증거 기반 JEV, 복구 가능한 작업, 단계적 제공자 교체를 제안했다.

실행 명령:

```powershell
git -c safe.directory=C:/AI-AGENT/ProjectHub fetch
git -c safe.directory=C:/AI-AGENT/ProjectHub pull --rebase
dotnet run --project "$env:TEMP\ProjectHub-MasterPolish-09A\FormCheck.csproj" -- C:\AI-AGENT\ProjectHub\Master-Polish.md
git -c safe.directory=C:/AI-AGENT/ProjectHub diff --check
```

- fetch/pull 성공, 이미 최신 상태.
- 임시 오프라인 .NET harness에서 현행 `JevContract.cs`를 직접 링크해 NEXT JEV, NOUL 0.90, SCORE 4단계/정규화 1.0, CHOICE 허용값을 확인했다.
- 기존 inline PASS 단일 항목의 파싱 실패도 재현했다. 제품 코드는 수정하지 않았다.
- JSON 예제 4개 파싱, Master의 로컬 링크, 코드블록 경계 검사를 통과했다.
- 임시 harness는 저장소 밖 테스트 자료이며 영구 테스트 프로젝트가 아니다. 실제 JEV API/Explorer E2E를 대신한 결과가 아니다.
- `git diff --check` 통과. 변경은 Master·현재상태·구현계획·09 task 문서 4개로 한정한다.
- 제품 전체 빌드/테스트, 실제 API 호출, Explorer 검증과 배포는 이번 문서 범위에서 실행하지 않았다.

## 2026-09-23 후속 보강

- MESSAGE 누적 표시를 가상화 목록으로 변경해 전체 로그 재조합·대형 TextBlock 재렌더링을 제거했다.
- JEV footer가 누락되지 않도록 모든 Codex 실행 경로를 공통 실행 메서드로 통합했다.
- GPT Web에도 JEV 사용 우선 지침을 전달하도록 Web prompt를 보강했다.
- 검증: Debug 빌드 성공, 전체 테스트 10개 통과, extension `node --check`, `git diff --check` 성공.
- 잔여: 실제 Explorer 화면에서 장시간 누적 스크롤과 JEV ON 왕복 검증.

## 2026-09-23 Extension 단계 보고 및 전송 복구

- Bridge `/bridge/progress`와 Worker `WEB EXTENSION` MESSAGE 로그를 추가했다.
- Extension은 전송 단계별 상태를 보고하고 composer·Send·실제 user message 확인을 여유 있게 재시도한다.
- Extension 빌드 `2026-09-23.1`로 동기화 기준을 갱신했다.
- 검증: Node 구문 검사, Debug 빌드, 전체 테스트 10개, 차이 검사 통과.
- 실제 Chrome 화면 검증은 Extension 새로고침 후 잔여.

## 2026-09-23 취소 후 진행 애니메이션 잔류 수정

- Worker의 terminal Task 이벤트에서 중복/timeout 조건이 화면 정리보다 먼저 반환되던 경로를 수정했다.
- 취소 또는 이미 처리된 Task라도 `_awaitingWebResult`를 해제하고 Run 버튼을 복구한 뒤 `SetFlowState(false, false, false)`로 진행 애니메이션을 종료한다.
- 검증: Debug 빌드 성공(경고 0/오류 0), 전체 테스트 10개 통과, Extension `node --check` 통과, `git diff --check` 통과.
- 실제 실행파일/Chrome 화면 검증은 아직 수행하지 않았다.

## 2026-09-23 복합 취소 경로 수정

- Run Task 버튼에서 Codex 실행 취소가 먼저 반환되어 GPT Web Task 취소와 화면 초기화가 누락될 수 있던 문제를 수정했다.
- Codex CTS와 Web Task가 동시에 활성인 경우 양쪽을 모두 취소하고 Worker 진행 상태·애니메이션을 즉시 초기화한다.
- 검증: Debug 빌드 성공(경고 0/오류 0), 전체 테스트 10개 통과, `git diff --check` 통과.

## 2026-09-23 GPT-6 Luna 기본 모델 및 모델 선택 확장

- Worker의 기본 Codex 모델을 `gpt-6-luna`로 변경했다.
- 모델 선택 목록에 `GPT-6 Luna`, `GPT-6 Sol`, `GPT-6 Astra`, `GPT-5.6 Luna`, `GPT-5.6 Terra`, `GPT-5.6 Sol`, `GPT-5.5`를 제공한다.
- 공식 OpenAI 자료 기준 GPT-6 Luna는 입력 $0.10/1M, 출력 $0.50/1M이며 GPT-5.6 Luna는 입력 $0.20/1M, 출력 $1.20/1M이다. GPT-6 Luna는 입력 약 50%, 출력 약 58.3% 낮다.
- 두 Luna 모델은 공식 자료상 1.05M 컨텍스트, 128K 최대 출력, `medium` 기본 reasoning을 지원한다. 실제 Codex 계정별 사용 가능 여부는 CLI 계정 권한에 따른다.
- 검증: Debug 빌드와 전체 테스트, `git diff --check`를 수행한다.

## 2026-09-23 GPT-6 Luna 인계 운영 가이드

- `GPT-6-LUNA-HANDOFF.md`를 추가했다.
- 기존 Codex 세션을 선택하고 모델을 `GPT-6 Luna`로 바꾼 뒤 다음 실행하면 Worker가 같은 세션을 `resume`해 인계한다.
- 실행 중 모델 변경은 지원하지 않으며, 먼저 Cancel 후 기존 스레드를 다시 선택해 실행한다.
- 실제 사용 모델은 MESSAGE의 `TASK START`와 `CLI STATUS`의 model 항목으로 확인한다.

## 2026-09-23 09-B Explorer 검증 재시도

- `dotnet build ProjectHub.sln --configuration Debug --no-restore`는 기본 sandbox에서 Windows SDK 경로 접근 거부로 실패했다. 권한 확장 재실행은 성공(경고 0, 오류 0).
- `dotnet test ProjectHub.sln --configuration Debug --no-build --no-restore` 통과: Core 1, Agent 3, Server 1, Worker 5. Extension `node --check`와 `git diff --check`도 통과.
- `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`를 실행했으나 프로세스의 MainWindowHandle이 0이고 CUA가 Windows 앱을 열거하지 못했다. Bridge `http://127.0.0.1:43821/bridge/status` 연결도 거부되어 화면 왕복 검증을 수행하지 못했다.
- `TYPESAFE_API_KEY` 환경 변수 존재는 확인했으나 외부 TypeSafe 호출은 보내지 않았다.
- 검증 명령: 위 Debug 빌드/테스트, `node --check extension/gptweb-hub/content.js`, `git diff --check`.
- 잔여 식별자: **09-B Explorer Judge OFF/ON 실화면 검증**, **09-C 증거 전달·AC 고정**. Explorer 창과 Bridge가 정상 기동되는 환경에서 이어서 수행한다.

## 2026-09-23 취소 복구 및 전송 버튼 확인 보강

- 사용자 취소 상태에서 활성 CTS가 남아 있어도 Worker 레이아웃을 즉시 IDLE로 복원하고 취소된 Bridge task ID의 늦은 terminal 이벤트가 화면을 덮지 않게 했다. CLI와 Web task가 겹치면 둘 다 취소한다. 취소 정리 완료 전 Run 재진입은 막고, 완료 후 연결 상태에 따라 다시 활성화한다.
- Extension은 취소된 Worker 메시지가 아직 composer에 정확히 남아 있을 때만 지워 입력창을 복구한다. 전송 대기 루프는 task 취소를 전송 성공으로 오인하지 않는다.
- Voice/마이크 버튼은 Send 후보에서 제외한다. Send 없이 입력 텍스트와 Voice 버튼만 유지되면 약 2.25초 후 `FAILED` 전송 결과를 Worker에 보고한다.
- Extension 빌드 및 Worker 기대값: `2026-09-23.2`.
- 검증: Debug 빌드 성공(경고 0/오류 0), 전체 테스트 10개 통과, `node --check extension/gptweb-hub/content.js`, `git diff --check` 통과.
- 잔여: 빌드된 Worker 화면과 갱신한 Chrome Extension으로 실제 취소 및 Voice 전용 상태 E2E 재현. UI 런타임이 사용 가능해지면 실행하고 결과를 갱신한다. 이번 변경은 코드 게시/배포하지 않았다.

## 2026-09-23 작업 중 MESSAGE 레이아웃

- 실행 중 COMMAND 패널을 폰트/제목 줄 높이에 맞춘 56px로 축소하고 입력 본문과 하단 컨트롤 행을 접어 MESSAGE가 나머지 공간을 사용하도록 했다.
- 유휴 상태 COMMAND 입력 UI는 기존처럼 유지한다.
- 검증: Debug 빌드 성공(경고 0/오류 0), 전체 테스트 10개 통과, `node --check extension/gptweb-hub/content.js`, `git diff --check` 통과.
- 잔여: 실제 Explorer 화면에서 COMMAND 축소와 MESSAGE 확장 크기 확인. 게시/배포하지 않았다.

## 2026-09-23 빠른 GPT 응답 및 동일 응답 본문 감지

- Assistant 응답의 기준점을 본문뿐 아니라 메시지 수, DOM 요소, 메시지 키로 저장해, 같은 문자열을 반환해도 새 turn임을 감지한다.
- 전송 확인 단계에서 새 사용자 메시지나 새 assistant turn이 발견되면 Voice 버튼만 남은 상태라도 빠른 전송/응답으로 인식한다. composer가 비워진 것만으로 성공 처리하지 않는다.
- Extension 빌드 및 Worker 기대값: `2026-09-23.3`.
- 검증: `node --check extension/gptweb-hub/content.js` 및 `git diff --check` 통과. Debug 빌드는 기본 sandbox에서 SDK 경로 접근 거부됐지만 권한 확장 실행에서 경고 0/오류 0. 전체 테스트는 권한 확장 실행에서 10개 통과(Core 1, Agent 3, Server 1, Worker 5).
- 잔여: 실제 Chrome에서 빠른 답변, 이전과 동일한 답변 본문, Voice-only 전송 실패를 재현해 결과 확인. 이번 변경은 게시/배포하지 않았다.

## 2026-09-23 Release 게시·작업 폴더 복사 완료

- Release 게시 성공 후 게시 스크립트가 C:\GameProject 실행본을 갱신했다. C:\AI-AGENT\Worker 실행본이 기존 프로세스에 잠겨 처음 복사에 실패했으나, Bridge task가 terminal/canceled임을 확인하고 프로세스를 교체한 뒤 복사 및 재기동했다.
- 저장소 bin 게시 EXE, C:\AI-AGENT\Worker EXE, C:\GameProject EXE SHA-256 모두 일치: `6424083FE1F1B2832C7813824D5FFDE1332F5129F11BBF8196A45326EECADBD1`.
- Chrome 확장 재로드 후 Bridge 준비 완료 및 Extension 빌드 `2026-09-23.3` 동기화 `true`를 확인했다. 공용 확장 manifest/content/background도 소스 해시와 일치한다.
- 잔여: 실제 GPT Web 빠른/동일 본문 응답 E2E.

## 2026-09-23 trust 작업 폴더 일반 권한 실행 재시도 실패

- C:\AI-AGENT\Worker의 ProjectHub.Worker.exe를 기존 실행본 종료 후 일반 권한으로 두 차례 실행했다. 프로세스 시작은 됐지만 .NET 응용 프로그램 오류 `0xe0434352`로 종료되고 Bridge가 준비되지 않았다.
- 최근 .NET 실행 시점/WER/Worker 시작 log에서 현재 시도의 stack trace를 찾지 못했다. Event Log의 2026-09-20 과거 유사 예외는 다른 게시 하위 폴더의 worker-icon.ico 누락 건이므로 현재 원인으로 단정하지 않았다.
- 현재 프로세스/Bridge 없음. 잔여: 현재 시도 stack trace 확보 후 수정하고 일반 권한으로 재검증한다.

## 2026-09-23 Worker 하단 컨트롤 및 GPT Web composer 선택 수정/게시

- 작업 중 COMMAND 행을 90px로 조정하고 입력 본문만 접어, 모델·Reasoning·Clear·Run Task·사용량 컨트롤을 계속 표시한다.
- Voice-only 실패 원인을 확인했다. Assistant writing block과 실제 `#prompt-textarea`가 모두 편집 가능 후보였고, 기존 코드가 첫 번째 assistant block을 골랐다. `composer()`는 이제 실제 ChatGPT 입력창 ID를 우선하고 assistant message 편집 영역을 거른다. 입력 뒤 prompt가 실제 composer에 반영됐는지 검증하고 선택 대상을 progress detail에 남긴다.
- Extension 빌드/Worker 기대값 `2026-09-23.4`.
- 검증: `node --check extension/gptweb-hub/content.js`, Debug 빌드 0 warning/0 error, 전체 테스트 10개, `git diff --check` 통과. Release 게시 후 C:\AI-AGENT\Worker 및 C:\GameProject 복사. 세 EXE SHA-256 일치. Worker Bridge 준비 완료, Chrome 확장 재로드 뒤 빌드 .4 동기화됨=true, 공용 manifest/content/background 해시 일치.
- 실제 새 시험 메시지 전송은 기존 대화에 메시지를 추가하므로 실행하지 않았다. 실제 Explorer 화면 캡처 검증은 미실행.

## 2026-09-23 GPT Web 답변의 Worker 전달 정체 조사

- 원인: Extension 성공 전송 감시가 단계명을 `RESPONSE_START`로 설정했으나 `observeResponse()`는 `WAIT_RESPONSE`일 때만 동작했다. 응답이 이미 완료되어도 Worker result POST가 시작되지 않을 수 있었다.
- 수정: Extension `2026-09-23.5`에서 성공 확인 직후 `WAIT_RESPONSE`로 전환해 즉시 관찰한다. 이미 전송된 CLAIMED task는 저장된 사용자/assistant 기준점을 복원하고 새 user turn이 있으면 재전송 없이 응답 관찰을 재개한다. Worker Bridge expected 빌드도 `.5`다.
- 검증/게시: `node --check extension/gptweb-hub/content.js`, `git diff --check`, Debug 빌드(권한 확장 실행, 경고 0/오류 0), 테스트 10개 통과. Release Worker 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 후 EXE 해시 일치를 확인했다.
- E2E 결과: 기존 task `de45e57d38e74728b03ff414f40fe46c`는 CLAIMED였고 대화 답변은 완료 상태였다. 복구를 위해 GPTWeb-Hub `업데이트` 조작을 하자 task가 `FAILED / extension_reset` 처리되어 결과 회수는 검증되지 않았다. Worker Bridge는 `ready`이나 설치 Extension `.4`와 Worker 기대 `.5`가 달라 `synchronized=false`다. 실패 task는 현재 API에서 재개되지 않는다.
- 다음: Chrome 확장 관리 화면에서 설치 확장을 실제 `.5`로 갱신하고 별도 허용된 요청으로 RESULT_POST → Worker 수신을 확인한다. 기존 대화 메시지는 재전송하지 않는다. 잔여: **09-B 응답 회수 E2E 및 reset 복구**, **09-B Explorer 화면**, **09-C 증거 전달·AC 고정**, **07 기존 검증**.

## 2026-09-23 하단 계약 메타데이터 파서 보강 및 Worker 시작 정리 수정

- 하단 계약 v1의 고정 라우팅 표식를 유지하고 NOUL의 claim/evidence/scope/counterexample 연속 입력을 파서가 JEV instructions에 보존하도록 했다. 계약 문서와 Master 예시를 함께 갱신했으며, evidence bundle 전달은 09-C로 남긴다.
- BridgeServer의 HttpListener 시작 실패 후 Dispose 과정에서 발생한 ObjectDisposedException이 원래 시작 실패 원인을 덮는 것을 확인해 정리을 listening 상태에 맞게 수행하도록 보강했다.
- `dotnet test ProjectHub.sln --configuration Debug --no-restore` 통과: Core 1, Agent 3, Server 1, Worker 6. 기본 샌드박스는 Windows SDK 경로 접근 거부로 실패해 동일 명령을 권한 확장으로 재실행했다.
- Release 게시 및 `C:\AI-AGENT\Worker`, `C:\GameProject` 복사 완료. 게시 실행본은 SHA-256 일치 확인.
- 일반 권한 실행에서 WPF 프로세스는 유지되고 MainWindowHandle이 생성됐지만 Bridge `127.0.0.1:43821/bridge/status` 연결은 거부됐다. UI/Bridge E2E는 미완료이며 시작 원인은 추가 관찰이 필요하다.
- 잔여: **09-B Explorer Judge OFF/ON 및 Bridge 왕복 확인**, **09-C evidence bundle/AC**, **07 기존 검증**. commit/push는 수행하지 않았다.

## 2026-09-23 Worker ordinary-user 시작 diagnosis

The 게시된 실행 파일 remains running and creates the 1200x1050 ProjectHub Worker 주 창. The local Bridge refuses connections because Windows has no URL ACL 예약 for `http://127.0.0.1:43821/`. Adding the reservation for the current user requires 관리자 권한 상승; the attempted `netsh http add urlacl` returned error 5. GUI launch verified; Bridge and Extension 왕복 remain blocked until the URL reservation is installed with 관리자 승인.
