# Master-Polish — 저비용 AI Role Dev Tool 설계

작성·기준일: 2026-09-23 (KST)
문서 작업: **09-A 완료** · 제품 구현 후속: **09-C, 10-A/B/C, 11-A 완료**
검토 기준: 2026-09-23 동기화된 GitHub `main`과 이후 로컬 완료 작업을 대조. 09-B 실화면 잔여는 사용자 결정으로 해결 처리하고 정기 관리에서 제외한다.
2026-09-23 갱신 기준: GitHub `main`의 `AGENTS.md`, 구현계획, 최신 `CurrentWork.md`, 활성 09 task, Master, 최신 `GPT-Web-Feedback.md`를 다시 대조했다.

> **개정 목표(2026-09-23): ProjectHub는 네 가지 AI 역할을 설정창에서 독립적으로 구성하는 CLI-to-CLI 중심의 저비용 개발 시스템이다.** 필수: **설계·관제 AI**(예: GPT-6 Sol CLI), **작업 AI**(예: GPT-6 Luna Medium CLI). 선택: **작업 판단 AI**(기존 JEV 연결 또는 향후 AI 판단 어댑터), **고수준 작업 AI**(어려운 구현을 위한 별도 모델). Worker가 상태·권한·예산·증거·복구와 독립 세션을 관리한다. ChatGPT Web/Extension은 기존 호환 경로로 보존하되 신규 기본 관제 경로가 아니다. 토큰 절약 → 목표까지의 지속성 → 교체 가능한 역할·모델 순서로 투자한다.
>
> 2026-09-23 기준 11-A coordinator-first CLI 흐름과 필수 역할 UI를 구현했다. 현재 `codex-cli 0.155.0-alpha.16` catalog와 직접 실행에서 GPT-6 Sol/Luna가 확인됐다. 실행 시에는 항상 설치된 CLI catalog로 지원 여부를 판정하고 자동 대체하지 않는다. 선택적 판단 AI·고수준 AI, 지속 실행·재시작 복구 등은 아직 별도 backlog다. 기존 ACTION/NEXT 공개 계약, Agent/Server/NAS 동작과 Git 승인 정책은 변경하지 않는다.

## 1. AI가 매번 먼저 읽을 짧은 운영 지침

1. 원래 요구와 완료 조건을 고정하고 **번호 작업 하나 + A/B/C 하나**만 구현한다. 새로운 요구는 대기 목록에 둔다.
2. 목표 기본값: 필수 **설계·관제 AI** = OpenAI GPT-6 Sol CLI, 필수 **작업 AI** = OpenAI GPT-6 Luna Medium CLI. **작업 판단 AI**와 **고수준 작업 AI**는 기본 OFF다. 모든 역할의 공급사·모델·추론 설정을 설정창에서 독립 관리하며, CLI 실행 전 실제 지원 여부를 확인한다. 이는 목표값이며 현재 구현 완료 상태를 뜻하지 않는다.
3. 최초 사용자 지시는 설계·관제 AI에 먼저 전달한다(목표 모드). 설계·관제 AI가 작업 범위·AC·검증 명령을 고정하고, 작업 AI는 이를 임의로 완화하지 않는다. 현재 제품의 Codex-first 동작은 11-A에서 CLI-to-CLI 기본 모드로 보완하며 Legacy Web 경로를 보존한다.
4. Codex는 관련 파일과 필요한 구간만 읽고 수정한다. 전체 저장소, 누적 로그, Master 전문을 매 라운드 재전송하지 않는다.
5. 같은 작업의 보완은 같은 Codex session을 쓴다. session 재사용이 과거 문맥 비용을 없애 주지는 않는다.
6. 빌드·테스트·파일·exit code는 로컬 도구로 확인한다. 선택적 작업 판단 AI(JEV 연동 포함)는 실제 전달된 증거의 의미를 평가하며 테스트 실행을 대체하지 않는다. 비활성화한 경우 로컬 검증을 건너뛰지 않는다.
7. 작은 수정은 로컬 검증으로 끝낼 수 있다. JEV는 의미 판단이 필요한 변경에만 쓴다. 질문은 개수를 억지로 줄이지 말고 **독립적으로 참/거짓 또는 상태를 판정할 수 있을 때까지 최대한 원자화**한 뒤 관련 질문을 가능한 한 한 호출에 묶는다.
8. JEV threshold 미달은 우선 `PARTIAL`로 분류한다. 실제 모순이 확인될 때만 구현을 보완하고, 증거 부족은 증거 수집, confidence만 낮으면 코드 변경 없이 검토한다. provider/contract 오류는 `ERROR`다.
9. 선택된 설계·관제 provider(CLI 또는 기존 Web)가 일시적으로 불가능하면 상태를 저장하고 정책에 따라 기다린다. 승인되지 않은 타 provider·고수준 모델로 임의 전환하지 않는다. 사전 승인된 작업 범위·예산 안에서만 이어간다.
10. 모든 필수 완료 조건에 최신 증거가 있을 때만 END한다. 미실행은 PASS가 아니다.
11. 사용자 Git 승인을 기억하되 다른 작업까지 확대하지 않는다. 이번 문서의 commit/push 승인은 미래 자동 push의 포괄 승인이 아니다.
12. 비밀값·자격증명·민감 URL을 프롬프트, Git, 로그, 증거 묶음에 넣지 않는다.

이 절은 **목표 운영 정책**이다. 현재 Worker가 모두 강제하는 것은 아니다. 특히 3·6·8·9·10은 아래 구현 단계가 필요하다.

## 2. 현재 구현에서 확인한 것

문서 이력과 코드가 다르면 현재 코드를 기준으로 판단한다. `CurrentWork.md`의 2026-09-22 상단에는 실제 JEV HTTP 200 smoke 기록이 있지만, 뒤쪽 과거 authoritative 섹션에는 smoke가 잔여라고 적혀 있다. 최신 기록을 우선하며 이번에 API를 다시 호출한 것으로 기록하지 않는다.

| 영역 | 현재 확인 결과 | 근거 |
| --- | --- | --- |
| 기반 플랫폼 | Agent → Server → Supabase, NAS data plane은 별도. AI Worker의 기본 실행에 Server/Git online은 필수가 아님 | `ProjectHub_IMPLEMENTATION_PLAN.md`, `GitReviewGate.cs`, `RunTask_Click` |
| Worker | WPF/.NET 9, 단일 인스턴스 mutex, CLI·Web·JEV 연결과 화면 표시 | `App.xaml.cs`, `MainWindow.xaml.cs` |
| 실행 시작 | 일반 입력과 `[ACTION=BEGIN]` 모두 **Codex부터** 실행. BEGIN은 본문을 추출할 뿐 Web-first 분기가 없음 | `MainWindow.xaml.cs: RunTask_Click` |
| Codex | 모델/추론 선택, `exec --json`, 명시적 session resume, workspace-write/read-only, 종료 시 stdout/stderr 수집 | `CodexCliRunner.cs: RunAsync` |
| Web 반복 | ACTION CONTINUE/PAUSE/END, 후속 지시를 같은 Codex session으로 전달. 30분 무활동 종료 | `ParseWebAction`, `RunWebResponseThroughCodexAsync`, `CheckJobInactivity` |
| 브리지 | conversation binding, claim/lease, 결과 저장, 이미 완료된 결과의 중복 제출 처리, JSON 파일 저장 | `BridgeServer.cs` |
| 브라우저 | composer 입력·전송·응답 수집, 재전송 경계, SPA conversation generation 확인 | `extension/gptweb-hub/content.js` |
| JEV | `jev-latest` HTTP adapter, 환경변수 키, NOUL/SCORE/CHOICE, PARTIAL triage·PASS 후 Codex 보고·Web review fallback | `JevJudgeRunner.cs`, `JevContract.cs`, `RouteCodexResultAsync` |
| 비용·payload 계측 | Codex/JEV/Web 호출별 usage-known, provider/model/purpose, payload byte·digest, latency와 retry를 JSONL로 저장. Web/JEV usage 미제공은 unknown이며 Codex cumulative snapshot 중복 합산을 방지 | `UsageTelemetry.cs`, `CodexCliRunner.cs`, `JevJudgeRunner.cs` |
| 증거·영속화 | 제한된 Codex text artifact를 provenance/digest/QID mapping과 round archive로 저장. 전체 CLI Job의 session/phase crash recovery는 별도 후속 후보 | `JevEvidence.cs`, `JevJudgeRunner.cs`, `BridgeServer.SaveState` |
| 자동 테스트 | Core 1, Agent 3, Server 1, Worker 16개. JEV 계약·PARTIAL·evidence digest·usage·6개 negative-control fixture 포함 | `tests/` |
| 검증 상태 기록 | ENGINE_HEADLESS/UI_BROWSER/HUMAN_UX/JEV 계층을 분리한다. 09-B Explorer 잔여는 사용자 결정으로 정기 관리에서 제외했으며, JEV negative-control provider 실측과 전체 Job crash 복구는 미완료 | `JevEvidence.cs`, 최신 `CurrentWork.md` |

기존 구현계획에는 Worker의 자동 빌드 도입 제외 경계가 있다. 이 설계의 당장 가능한 로컬 검증은 **Codex가 작업 카드에 지정된 명령을 실행하고 Worker가 결과를 수집하는 방식**이다. Worker 자체가 빌드를 예약·실행하는 기능을 추가하려면 해당 후속 task에서 기존 정책과 범위를 먼저 명시적으로 변경해야 한다.

좋은 기반은 그대로 쓴다. 특히 Worker를 중계·상태 주체로 두고 Extension을 브라우저 어댑터로 분리한 점, 동일 session 보완, typed JEV 결과, 선택형 Server는 목표에 맞는다. 지금 전면 재작성이나 범용 그래프 편집기를 만들 이유는 없다.

## 3. 비용과 지속성을 막는 실제 차이

### P0 — 판단에 필요한 증거와 계약 일치

- **JEV의 검증 대상이 비어 있을 수 있다.** `JudgeRequest`에는 Files/ReviewSource/ReviewCommitSha가 있지만 실제 HTTP state는 task, codex_result, round, working_directory만 보낸다. footer대로 NEXT JEV 결과에 질문만 담으면 JEV는 코드·diff·테스트 결과를 받지 못한다. 로컬 경로 문자열만으로 원격 JEV가 파일을 읽을 수 없다. ‘현재 구현이 맞는가’에 대한 PASS를 완성 증거로 쓰면 안 된다.
- **문서 예제와 parser 호환성을 회귀 검사한다.** 현재 parser는 inline PASS와 다음 줄 PASS를 모두 파싱한다. 새 권장 양식은 PASS를 별도 줄에 두고 optional EVIDENCE/SCOPE/COUNTEREXAMPLE continuation을 JEV instructions에 보존한다. metadata 참조만으로 Worker가 파일 내용을 JEV에 첨부하는 것은 아니며, 실제 evidence bundle 전달은 09-C 범위다.
- **JEV threshold 미달과 구현 결함은 다르다.** 유효 응답의 미달은 PARTIAL로 Codex triage에 보내고, question ID 누락/type mismatch/잘못된 값은 ERROR로 분리한다. 실제 코드 결함은 결정적 모순을 확인한 뒤에만 수정한다.
- **형식 검증이 부족하다.** NEXT WEB의 REPORT 필수, SCORE 연속 번호·threshold 범위, CHOICE 허용값의 정의 포함 여부 등을 강제할 필요가 있다. ACTION/NEXT의 첫 유효행 원칙은 보존한다. 본문의 코드·인용에 태그가 등장하는 것과 두 제어 명령을 제출하는 것은 구분한다.
- **fallback 사유가 관제에 빠질 수 있다.** JEV 오류는 MESSAGE에 기록하지만 Web prompt는 원 Codex 결과 중심이다. `JEV_ERROR`, 실패한 항목, 시도 횟수를 짧은 기계 필드로 함께 보내야 Web이 같은 요청을 반복하지 않는다.

### P1 — 불필요한 Codex 호출과 반복

- JEV ON이면 매 CLI 호출에 긴 footer 전문을 붙인다. 최초 계약과 짧은 후속 상기문으로 나눌 여지가 크다.
- JEV PASS 뒤 보고서 작성만을 위한 Codex 호출이 **최소 한 번 더** 필요하다. 현재 v1 계약을 지키기 위한 동작이므로 지금 삭제하지 않는다. 나중에 구현 결과와 검증 요청을 분리 저장하는 v2 계약이 검증되면 템플릿 보고로 대체한다.
- PASS 직후 `_judgeRound=0`으로 만들고 재귀 라우팅한다. 보고서 요청에 Codex가 다시 NEXT JEV를 출력하면 PASS→보고 요청→JEV가 반복될 수 있다. 보고 전용 phase에서 WEB+REPORT만 허용하고 다시 JEV를 호출하지 않아야 한다.
- PARTIAL 뒤 Codex는 evidence 보완이 불가능하거나 사용자 확인이 필요하면 NEXT WEB을 선택할 수 있다. PARTIAL을 PASS나 구현 결함으로 오인하지 말고 미해결 상태를 보고한다.
- usage는 여러 위치의 usage/token_usage를 모두 더한다. provider가 누적 snapshot 또는 별칭을 함께 주면 중복 계상될 수 있다. reasoning이 output에 포함되는지 확인하지 않고 더하는 total fallback도 점검 대상이다. 실제 JSONL fixture로 재현 후 정규화한다.

### P1 — 계속 실행하는 것과 복구하는 것은 다름

- bridge-state 저장만으로 Codex 실행 중 Job 전체를 복원할 수 없다. 목표·활성 단계·session·증거·승인 범위까지 함께 저장해야 한다.
- CLI는 현재 종료 후 stdout을 처리한다. 정상적으로 오래 작업하면서 중간 출력을 내도 Worker watchdog이 활동을 인지하지 못할 수 있다. CLI JSONL 진행 이벤트를 별도로 관찰해야 한다.
- 30분 timeout은 활동이 있는 무한 개선 루프의 비용을 막지 못한다. 반대로 Web 사용 제한을 기다리는 정상 작업을 종료할 수 있다. **진행 감지·대기·예산·의미 있는 개선량**을 나눠야 한다.
- 긴 누적 문서는 현재 상태 파악 비용을 늘린다. 검토 시 feedback 5,339행, CurrentWork 989행, 구현계획 487행이었다. 과거 기록을 보존하면서 최신 요약을 짧게 고정할 필요가 있다.

### P2 — 교체 가능성

모델 선택 UI는 있으나 coordinator/implementer/judge의 공통 provider 계약은 없다. 현재 실행 로직이 `MainWindow.xaml.cs` 약 1,471행에 모여 있다. 먼저 작은 실행 코어로 옮기고 안정화한 뒤 역할 어댑터를 추가한다. 교체 가능성을 이유로 현재 안정화보다 추상화 작업을 앞세우지 않는다.

## 4. 목표 구조와 역할 책임 — 개정된 네 역할

명칭은 모델명이나 회사명이 아닌 **책임**을 기준으로 고정한다. 각 역할별 공급사·모델·추론 설정은 독립적이다.

| AI 역할 | 필수 여부 | 책임 | 초기 목표 구성 |
| --- | --- | --- | --- |
| **1. 설계·관제 AI** | 필수 | 사용자 요구 해석, 설계, 하나의 작업 카드·AC 발행, 결과 검토, 재분해, 최종 완료 제안 | OpenAI GPT-6 Sol CLI |
| **2. 작업 AI** | 필수 | 지정 범위 코드 구현·수정, 도구 실행, deterministic validator, 원본 증거 생성 | OpenAI GPT-6 Luna CLI / medium |
| **3. 작업 판단 AI** | 선택 | AC 대비 증거의 의미 판단, 누락·모순 분류, 원자 질문 검증. 소스 직접 변경 금지 | 기본 OFF; 켤 때 기존 JEV 엔진 연결 가능 |
| **4. 고수준 작업 AI** | 선택 | 설계·관제 AI가 승인된 특정 고난도 구현/분석 작업을 위임하는 별도 작업자 | 기본 OFF; 모델 별도 선택 |

```text
사용자 요구
  → ProjectHub Worker (원본 상태·권한·예산·세션 관리)
  → 설계·관제 AI (전용 읽기 중심 CLI 세션)
      → 한 작업 카드와 고정 AC
      → 작업 AI (별도 쓰기 허가 CLI 세션)
      → 로컬 deterministic validator (실제 실행)
      → [옵션] 작업 판단 AI (원본 evidence에 대한 JEV/선택한 판단 백엔드)
      → 설계·관제 AI (PASS/FAIL/증거 부족을 구분해 다음 카드 또는 종료)
      ↳ [옵션·설정된 권한/예산 충족 시에만] 고수준 작업 AI
           → 실행 결과와 검증 증거를 관제로 반환
```

**Worker는 AI가 아니다.** 실행 순서, 작업별 배타적 쓰기 권한, 결과 중복 차단, 모델/세션 snapshot, 예산, 체크포인트, 복구와 완료 조건을 강제한다. 설계·관제 AI가 직접 임의 shell/Git push/권한 상승을 행사하는 구조가 아니다.

**독립 세션:** 설계·관제 AI, 작업 AI, 선택된 고수준 작업 AI는 각자 별도 CLI 세션을 가진다. 같은 모델을 두 역할에 할당해도 세션은 공유하지 않는다. 한 역할 내부의 같은 작업 보완은 가능하면 같은 세션을 resume하고, 역할 사이에는 요약된 작업 카드와 evidence packet만 전달한다.

**쓰기 경계:** 설계·관제 AI는 기본 읽기 전용, 작업 AI만 승인된 범위에서 쓰기 가능. 고수준 작업 AI는 단순 추가 권한이 아니며, 승인된 위임 범위에서만 쓰기 가능하다. 같은 파일/작업 폴더에 두 작업자가 동시에 쓰지 않도록 Worker의 하나의 write lease를 적용한다. 외부 시스템 변경 및 Git commit/push는 기존 명시적 승인 정책을 유지한다.

**작업 판단 AI OFF:** deterministic validator는 필수 수용 조건에 따라 그대로 시행한다. 의미 판단이 필요한 상태인데 판단 AI를 끈 경우 설계·관제 AI가 evidence 부족 또는 사용자 확인을 구분하며, '판단 AI가 없으니 PASS'로 해석하지 않는다. ON일 때도 JEV `ALL_PASS`가 실제 브라우저/사용자 UX 검증까지 완료시킨 것은 아니다.

**기존 Web 경로:** ChatGPT Web·브라우저 Extension의 공개 wire `[ACTION]`, `[NEXT : WEB|JEV]`와 과거 구현을 제거하지 않는다. 신규 CLI 모드는 별도의 내부 구조화 메시지 계약을 사용하고 Web 태그 의미를 재해석하지 않는다. 현재 제품 구현상 Web/Codex-first 동작은 후속 작업에서 이행한다.

## 5. 토큰 절약 정책

### 5.1 비용을 정의하는 방법

목표 지표는 **완료된 수용 조건 1개당 사용량과 재작업 횟수**다. 짧은 답변 자체가 성공 지표는 아니다. 한도를 아끼려고 검증을 생략해 오답을 쌓으면 전체 비용이 커진다.

기록 제안: role/provider/model/reasoning, job/step/call ID, input/cached input/output/reasoning, 호출 목적, latency, 재시도 원인, known/unknown 사용량.

- Codex와 JEV 사용량을 분리하고 총합은 참고로만 표시한다. 모델이 다르면 토큰당 비용도 다르다.
- 캐시 입력은 input의 일부인지 provider 계약을 확인한다. 중복해서 total에 더하지 않는다.
- usage가 없으면 `unknown`이다. 0으로 표시하거나 계정 5시간/주간 잔량을 추정하지 않는다.
- ChatGPT Web의 내부 토큰·계정 한도는 Worker가 알 수 없다. 메시지 횟수·문자 수·대기 시간만 별도 기록한다.
- API 금액은 확인된 단가·버전·시각이 있을 때만 추정 표시한다. 구독 한도와 API 비용을 같은 단위로 환산하지 않는다.

### 5.2 매번 전달할 내용

| 전달 | 포함 | 제외 |
| --- | --- | --- |
| Web → Codex | 작업 ID, 목표 한 문장, 수정 범위, 고정 AC, 검증 명령, 결과 형식 | Master 전문, 이전 로그 전체 |
| Codex → Worker | 변경 파일, AC별 결과, 실행 명령·exit code·증거 ID, 미완료·차단 사유 | 장황한 자기평가, 전체 stdout 반복 |
| Worker → JEV | 현재 AC, 관련 diff/test 증거 index, 실제 검증 결과, 원자화된 typed 질문 묶음 | 전체 저장소, unrelated 로그, 자격증명 |
| Worker → Web | 현재 단계 결과, 실패 항목, JEV 상태, 짧은 증거, 다음 판단점 | 이미 읽은 모든 이력 |
| 실패 → Codex | 실패 AC·실제 값·관련 증거·한 가지 수정 목표 | 모든 통과 결과의 재전송 |

초기 튜닝값 제안: 작업 카드 800~1,500자, 결과 요약 600~1,200자, JEV 증거 약 2,000~6,000자. 이는 **현재 강제 제한도 토큰 환산식도 아니다**. 정확한 검증에 필요한 코드·오류를 잘라 버리지 않으며 초과 시 명시적으로 추가 구간을 요청한다.

### 5.3 비용을 줄이는 실행 순서

1. 관제가 모호함을 제거하고 작은 작업 카드 하나를 만든다.
2. Codex가 구현과 해당 범위 검증을 한 번에 한다. 별도의 ‘계획만’, ‘진행 확인만’ CLI 호출을 만들지 않는다.
3. 로컬 검증 실패면 실패 로그만 Codex에 전달한다. 명백한 컴파일 오류를 JEV에 묻지 않는다.
4. 의미 판단이 필요한 경우에만 JEV 한 번에 여러 질문을 보낸다.
5. 동일 증거+동일 rubric+동일 모델 revision의 재판정은 기존 결과를 사용한다. 파일 hash/AC/모델이 바뀌면 무효화한다. `jev-latest`처럼 변경 가능한 alias만으로 영구 캐시하지 않는다.
6. 설계·관제 AI는 시작·작업 경계·복잡한 실패·최종 완료에 관여한다. 승인된 카드 내부의 사소한 보완마다 상위 관제 호출을 반복하지 않는다. 기존 Web 모드에서도 같은 규칙을 적용한다.
7. 같은 작업은 resume한다. 문맥이 커지거나 독립된 새 작업이면 `현재 상태 + 결정 사항 + 다음 작업 + 증거 위치`로 짧게 인계해 새 session을 만든다.

설계·관제 AI가 처음부터 별도 GPT-6 Sol을 사용하는 것은 **기본 관제 설정**이지 작업 AI의 무조건적 모델 상향이 아니다. 같은 구현 실패가 반복되면 관제가 먼저 작업 카드를 좁힌다. 선택적 **고수준 작업 AI** 호출은 활성화·명시된 위임 규칙·예산 승인·독점 write lease가 모두 충족될 때만 가능하다. 기본 정책: `allow_paid_fallback=false`, `allow_automatic_escalation=false`.

## 6. 연속 실행과 복구 설계

### 6.1 무중단의 수용 가능한 정의

정상 범위에서는 사람이 다음 버튼을 누르지 않아도 계속 진행한다. 외부 서비스 제한·로그인 만료·네트워크 단절은 없다고 가정하지 않는다. **대기 후 같은 작업으로 돌아오며 중복 수정·중복 전송이 없어야 한다.** 승인이 필요한 외부 변경이나 예산 소진은 이유를 보존해 대기한다.

신규 CLI-to-CLI 목표 모드에서는 지정된 관제 CLI 가용성을 확인하며, 기존 ChatGPT Web은 호환/선택 모드로 유지한다. 어떤 provider든 무제한 가용성·자동 전환을 전제하지 않고 로그인·사용 제한을 우회하거나 사용자가 허가하지 않은 유료 경로로 전환하지 않는다.

### 6.2 상태 머신 제안 — 기존 wire protocol은 유지

```text
NEW → PLAN_PENDING → IMPLEMENTING → VERIFYING
                                   ├─ 실패 → REWORK → IMPLEMENTING
                                   ├─ 의미 검증 필요 → JUDGING
                                   └─ 검증 충분 → REVIEW_PENDING
JUDGING → PASS → REPORT_PENDING → REVIEW_PENDING
        → PARTIAL → TRIAGE
        → INSUFFICIENT_EVIDENCE → COLLECT_EVIDENCE
        → ERROR → WAITING_PROVIDER / REVIEW_PENDING (정책에 따라)
REVIEW_PENDING → 다음 승인된 카드 / COMPLETED
어느 단계든 → WAITING_APPROVAL / WAITING_BUDGET / CANCELED
재시작 → RECOVERING → 마지막 확인된 안전 단계
```

이 상태 이름은 새 내부 설계다. 기존 `[ACTION=CONTINUE|PAUSE|END]`, `[NEXT : WEB|JEV]`를 다른 태그로 바꾸지 않는다. `END`는 Worker가 남은 필수 AC, 최신 증거, 실패 미해결 여부를 확인한 뒤 완료로 반영한다. 의미 해석은 관제/JEV에 맡기고 Worker는 상태표의 불일치만 거부한다.

### 6.3 최소 영속 데이터

새 DB 없이 먼저 `Worker/state/jobs/<job-id>.json`과 순차 event journal을 사용한다. 경로는 제안이며 현재 전체 Job 저장 기능은 없다.

```json
{
  "schema_version": 1,
  "job_id": "job-example",
  "step_id": "09-B",
  "phase": "JUDGING",
  "goal_ref": "goal.json",
  "acceptance_revision": "ac-v1",
  "provider_bindings": {"implementer": "codex-luna-medium", "judge": "jev"},
  "session_id": "local-session-reference",
  "web_binding_ref": "local-binding-reference",
  "last_completed_call_id": "call-004",
  "pending_call_id": "call-005",
  "judge_attempts": 1,
  "last_progress_at": "2026-09-22T00:00:00Z",
  "artifact_manifest_ref": "evidence/manifest.json",
  "budget_ref": "budget.json",
  "authorization_ref": "authorization.json",
  "resume_at": null,
  "finish_reason": null
}
```

저장에는 비밀값이나 민감 URL을 넣지 않는다. runtime 데이터는 Git 제외가 전제다. 설정 변경과 실행 중 binding을 분리해 한 Job의 작업 폴더·session·provider·권한이 도중에 바뀌지 않게 한다.

호출 전 요청 hash와 pending call을 원자적으로 저장한다. 응답 수신 후 결과를 저장하고 다음 phase로 이동한다. 시작 시 journal을 대조해 마지막 완료 전이를 복구한다. 파일 손상은 조용히 새 Job으로 시작하지 않고 복구 가능한 사본/오류 상태를 남긴다.

### 6.4 재시도와 대기

| 상황 | 처리 제안 |
| --- | --- |
| 결과 POST 응답만 유실 | 동일 task/lease/result ID로 결과 제출 재시도 |
| Web Send 성공 여부 불명 | conversation과 기존 user message/assistant baseline 확인. 같은 prompt를 즉시 다시 보내지 않음 |
| CLI 실행 중 Worker 종료 | 프로세스/session·실제 diff·완료 기록을 먼저 대조. 증거 없이 재실행하지 않음 |
| 429 / 일시 5xx / 연결 실패 | Retry-After 우선, 없으면 예: 5초→15초→60초. 호출 예산 안에서 제한 재시도 후 WAITING_PROVIDER |
| 401 / 키 없음 | 자동 반복 호출 중단, 자격증명 필요 상태. 준비 후 동일 단계 재개 |
| JEV의 유효한 PARTIAL | 현재 Web 작업 라운드 기준 총 3회 검증까지. threshold 미달만으로 구현 결함을 단정하지 않고, 구체 모순·증거 부족·confidence 미달을 분리한다. 세 번째에도 PASS가 아니면 사용자/관제 검토로 넘긴다. |
| evidence 변경 후 JEV 재검증 | 변경된 evidence를 참조하는 원자 질문만 다시 판정한다. 영향받지 않은 질문의 PASS는 유지한다. |
| JEV malformed 응답 | ERROR. 구현 실패로 취급하지 않고 원본 보존 + 짧은 오류를 Web에 전달 |
| 같은 실패 지문 2회, 증거 변화 없음 | 기본 튜닝값: Web에 작은 재분해 요청 1회. 다시 실패하면 BLOCKED 상태와 재개 입력 보존 |
| 예산 소진 | WAITING_BUDGET, 승인 없는 모델 상향·추가 사용 금지 |
| 사용자 Cancel | 실행 취소·상태 저장. 자동 재개하지 않음 |

30분 한 값 대신 provider timeout, 실제 진행 event, 전체 작업 예산을 분리한다. heartbeat가 온다는 이유만으로 구현 진척이 있다고 보지 않는다. 반대로 CLI 도구 실행·새 검증 결과는 활동으로 인정한다. 초기 예산 수치는 작은 대표 과제 3~5건을 측정한 뒤 사용자와 설정한다.

## 7. 바로 사용할 입력 양식

### 7.1 사용자 요구 양식

```text
만들 프로그램/기능:
누가 어떤 상황에서 사용하는가:
이번에 끝낼 범위:
완료 조건 1:
완료 조건 2:
수정하면 안 되는 기능/파일:
확인 가능한 실행 방법:
작업 폴더:
기본 구현 모델: Codex Luna / Medium
Git/배포 권한: 이번에는 없음 / 구체적으로 승인한 범위
자동 진행: 위 범위 안에서 구현·검증·수정 반복
중단 조건: 권한 필요, 외부 서비스 대기, 설정한 예산 소진
```

당장 모든 칸을 채울 필요는 없다. 관제가 누락을 정리하되 로컬 코드에서 확인할 수 있는 사항을 사용자에게 반복해서 묻지 않는다.

### 7.2 기존 Web 관제 모드의 임시 실행 양식(레거시)

**아래 BEGIN 우회 양식은 현재 제품/과거 Web 모드의 호환 예제이지, 새로운 기본 CLI-to-CLI 설계의 시작 절차가 아니다.** 신규 모드는 11-A의 Coordinator-first 라우팅을 구현한다.


**현재 BEGIN도 Codex-first다.** 다음은 구현을 바로 시작하지 않도록 첫 CLI를 짧은 인계 전용으로 쓰는 임시 운영법이다. 첫 호출 비용은 남는다. Judge ON에서 사용한다.

Worker COMMAND:

```text
[ACTION=BEGIN]
이번 첫 응답은 관제 인계만 수행한다. 도구 실행과 파일 변경을 하지 말고,
아래 요구를 빠짐없이 짧게 정리하여 [NEXT : WEB]과 [REPORT]로 응답하라.
이 제한은 최초 인계 1회에만 적용한다. 이후 관제의 명시적 작업 카드 범위에서 구현한다.

[사용자 요구]
여기에 7.1 양식으로 실제 요구를 작성한다.
```

Worker의 GPT Web 추가 지침:

```text
너는 이 작업의 관제다. 기본 구현자는 Codex Luna Medium이며 토큰 절약을 우선한다.
원래 요구와 완료 조건을 고정하고 한 번에 번호 작업 하나, A/B/C 하나만 지시한다.
응답 첫 줄은 [ACTION=CONTINUE], [ACTION=PAUSE], [ACTION=END] 중 하나다.
CONTINUE에는 목표, 수정 범위, AC, 검증 명령, 결과 형식을 짧게 쓴다.
수정 가능한 실패는 같은 범위에서 보완시킨다. 증거 부족과 API 오류를 코드 실패로 간주하지 않는다.
테스트 미실행을 PASS로 기록하지 말고 필요한 증거 수집을 먼저 지시한다.
모든 필수 AC의 최신 증거가 있을 때만 END한다. 유료 모델 상향이나 Git 권한을 만들어내지 않는다.
```

명령 전체에 `읽기 전용`/`read-only`를 넣으면 현재 Worker가 Task 전체 sandbox를 read-only로 고정할 수 있다. **최초 인계만 제한하려면 위처럼 단계 범위를 명시한다.** 궁극적으로는 시작 모드 `관제에서 시작`을 구현해 최초 CLI 인계 호출 자체를 없앤다.

### 7.3 관제 → 구현 AI 작업 카드

```text
[ACTION=CONTINUE]
작업: 09-B
목표: 잘못된 JEV 응답을 구현 FAIL과 분리한다.
먼저 읽기: AGENTS.md, 최신 상태 요약, 활성 task, 관련 JEV 계약 구간.
수정 범위: JevJudgeRunner의 응답 평가와 그 전용 테스트.
유지: ACTION/NEXT 형식, Judge OFF, 정상 응답 PASS/FAIL 의미.
AC-1: 누락된 question ID와 type mismatch는 ERROR로 분류된다.
AC-2: 정상 범위의 유효한 답만 threshold와 비교한다.
검증: 정상/누락/범위 밖 응답 fixture 테스트와 관련 build.
완료 응답: 변경 파일, AC별 실제 결과, 명령/exit code, 잔여 사항을 짧게 보고.
구현 범위 밖 문제는 대기 항목으로만 기록한다.
```

위 카드는 형식 예다. 실제 09-B 범위·테스트 프로젝트 생성 범위는 활성 task에서 확정한다. 검증 명령을 `테스트해라`로 두지 말고 해당 프로젝트에 존재하는 정확한 명령으로 치환한다.

## 8. JEV에게 전달할 명령 form

### 8.1 먼저 알아둘 구분

**A. 사람이 쓰는 Worker footer**는 NEXT/VALIDATION REQUEST와 PASS 기준을 포함한다.
**B. 실제 JEV API 요청**은 model/state/questions다. Worker가 A를 B로 변환하고 PASS를 자체 비교한다. PASS 문자열 자체는 API의 판정 필드가 아니다.

JEV는 질문과 제공된 상태를 평가한다. `0.90`은 질문에 대한 yes 판단값 문턱이며 프로그램이 90% 완성됐다는 뜻이 아니다. 날씨 자료 없이 ‘오늘 비 올 확률을 0~1로 말해라’는 재현 가능한 연결 시험이 아니다. 현재 Test 버튼의 날씨 질문은 고정된 작은 state에 대한 예/아니오 질문으로 교체하는 것을 권한다. [공식 NOUL 설명](https://docs.typesafe.ai/primitives/noul)

### 8.2 현재 parser와 호환되는 복사 양식

새 질문은 PASS를 별도 줄에 두는 다중 행 형식을 우선한다. 과거 inline PASS 입력은 계속 허용한다. 아래 질문은 형식 예이며, Worker가 evidence bundle을 전달하기 전에는 경로/참조만으로 JEV가 로컬 파일을 읽는다고 간주하지 않는다. 코드블록 테두리를 제거한 본문만 Codex의 최종 응답으로 사용한다.

<!-- FORM:JEV_CURRENT_START -->
```text
[NEXT : JEV]

[VALIDATION REQUEST]

- NOUL | [HIGH] ResetRound()은 생명 손실 뒤 현재 Brick 배치를 유지하는가?
  EVIDENCE: GameWorld.cs / ResetRound() — LoadStage()를 호출하지 않음.
  SCOPE: 생명 손실 후 같은 Stage를 재시작하는 경로.
  COUNTEREXAMPLE: Brick 목록을 비우거나 다시 만들면 NO.
  PASS: YES >= 0.80

- SCORE | 제공된 변경 증거에서 허용 범위를 벗어난 정도는 어느 수준인가?
  1 = 모든 변경이 허용 범위 안에 있다
  2 = 요구 동작에 필요한 작은 인접 변경이 있다
  3 = 요구하지 않은 동작 변경이 있다
  4 = 목표가 다른 변경이 포함되어 있다
  PASS: SCORE <= 2.0

- CHOICE | AC-1을 판정할 증거의 상태를 분류하라.
  SUFFICIENT = 현재 결과와 일치하는 직접 증거가 있다
  INSUFFICIENT = 증거가 없거나 누락되어 판정할 수 없다
  CONTRADICTORY = 주장과 실제 증거가 충돌한다
  PASS: SUFFICIENT
```
<!-- FORM:JEV_CURRENT_END -->

현재 parser는 등장 순서로 C1/C2/C3를 부여하고, 질문 다음의 continuation line을 JEV `instructions`에 보존한다. NOUL의 `EVIDENCE`, `SCOPE`, `COUNTEREXAMPLE`는 현재 질의 지침 텍스트로 전달될 뿐 Worker가 참조된 파일이나 로그를 읽어 API state에 첨부하지는 않는다. 실제 증거 수집과 질문별 evidence 연결은 **09-C**에서 다룬다. SCORE의 위 표기는 1-based이며 API criteria는 0-based다. 예시의 `<= 2.0`은 API 값 `<= 1.0`과 비교한다. `INSUFFICIENT`를 증거 수집 단계로 따로 보내는 것도 09-C 이후 설계다.

검증 질문의 **개수 자체를 목표로 제한하지 않는다. 질문 원자성이 우선**이다. 하나의 질문에 ‘보안·성능·완성도 모두 충분한가’처럼 독립적으로 실패할 수 있는 명제를 합치지 않는다. 각 명제가 별도로 반증될 수 있다면 C1/C2/C3처럼 끝까지 분리하고, 관련 원자 질문을 동일 JEV 요청에 batch한다.

질문 작성 기본형은 `CLAIM + EVIDENCE + SCOPE + COUNTEREXAMPLE`이다.

- **CLAIM**: 이번 질문에서 판정할 단일 주장.
- **EVIDENCE**: 관련 코드 위치, diff, deterministic test, 실행 로그 등 JEV가 실제로 볼 근거.
- **SCOPE**: 이번 질문에서 판정할 범위와 판정하지 않을 범위.
- **COUNTEREXAMPLE**: 어떤 관찰이 나오면 NO/비허용 상태인지.
- 질문에 증거가 필요한데 evidence reference가 없으면 코드를 먼저 고치지 말고 증거 부족 여부를 분리한다.

질문 타입도 목적에 맞춰 구분한다.

- **NOUL**: 하나의 사실을 YES/NO로 판정할 때.
- **CHOICE**: `SUPPORTED / PARTIAL / INSUFFICIENT / CONTRADICTORY`처럼 증거 상태·원인·변경 성격을 분류할 때.
- **SCORE**: 범위 이탈·위험도처럼 순서가 있는 정도를 rubric으로 평가할 때만 사용한다. 사실 확인을 SCORE로 대신하지 않는다.
- build exit code, 파일 존재, count, hash, timer, 상한 비교처럼 deterministic하게 계산 가능한 사실은 로컬 도구가 원본이며, JEV는 그 증거가 AC를 의미적으로 뒷받침하는지를 평가한다.

NOUL 중요도별 초기 threshold 정책은 다음을 기본값으로 쓴다. 숫자는 ‘프로그램이 그만큼 맞다’는 완성도 비율이 아니다.

| 중요도 | 기본 PASS |
| --- | --- |
| LOW | `YES >= 0.60` |
| MEDIUM | `YES >= 0.70` |
| HIGH | `YES >= 0.80` |
| CRITICAL | `YES >= 0.90` |

threshold와 중요도는 검증 전에 고정하고 실패 후 통과시키려고 낮추지 않는다. deterministic 검증이 PASS인데 JEV confidence만 낮고 구체적인 contradiction이 없다면 점수 맞추기식 코드 변경을 금지한다.

TypeSafe 공개 API 문서에서 `questions`는 `map<string, Question>`으로 정의되지만 현재 공개 문서에는 **한 요청의 질문 개수 최대값이 명시되어 있지 않다**. Choice의 최대 255개 option과 Score의 최대 10개 level을 질문 수 상한으로 오해하지 않는다. 따라서 ‘256개까지 가능’ 같은 수치를 계약에 고정하지 말고, 실제 provider 한도는 별도 smoke test에서 32→64→128→256처럼 단계적으로 측정해 기록한다. 한도가 확인되더라도 질문을 합쳐 원자성을 깨지 말고 필요하면 여러 batch로 나눈다.

BrickBreaker 실험에서는 동일한 MultiBall 질문이 근거 위치 없이 전달됐을 때 낮은 NOUL과 `INSUFFICIENT`가 나왔으나, 코드 줄·validator·runtime PASS evidence를 질문에 직접 연결한 재검증에서는 전체 PASS가 나왔다. 이 경험을 근거로 **질문을 더 넓게 만드는 것이 아니라 질문마다 근거를 더 정확히 붙이는 방향**을 기본으로 한다.

### 8.3 증거를 포함한 API form — 목표 예시

아래는 TypeSafe API의 model/state/questions 형태를 따르는 **가상 예시**다. 현재 Worker의 serializer는 이 evidence state를 아직 만들지 않는다. 이 JSON은 Worker COMMAND나 설정에 그대로 붙여 넣는 양식이 아니다.

```json
{
  "model": "jev-latest",
  "state": {
    "task": "빈 이름을 저장하려 하면 안내하고 파일을 생성하지 않는다.",
    "acceptance": [{"id": "AC-1", "text": "빈 이름이면 파일을 생성하지 않는다."}],
    "evidence": {
      "example_only": true,
      "revision": "example-revision-3",
      "diff_excerpt": "if (string.IsNullOrWhiteSpace(name)) return ValidationError;",
      "test": {"name": "EmptyNameCreatesNoFile", "status": "passed", "exit_code": 0},
      "known_gaps": ["실제 화면 검증은 아직 수행하지 않음"]
    }
  },
  "questions": {
    "C1": {
      "type": "noul",
      "instructions": "제공된 코드와 테스트 증거는 AC-1의 동작을 뒷받침하는가? 화면 검증 완료 여부까지 추론하지 말라.",
      "criteria": {"true": "직접 근거가 있으며 모순이 없다", "false": "근거가 없거나 모순이 있다"}
    },
    "C2": {
      "type": "choice",
      "instructions": "AC-1의 코드 수준 판정을 위한 증거 상태를 분류하라.",
      "criteria": {
        "SUFFICIENT": "직접 관련된 코드와 검증 결과가 있다",
        "INSUFFICIENT": "판정에 필요한 자료가 없다",
        "CONTRADICTORY": "코드와 검증 결과가 서로 모순된다"
      }
    }
  }
}
```

전송은 `POST https://api.typesafe.ai/v1/systemone`, Bearer 인증은 `TYPESAFE_API_KEY` 환경변수에서만 읽는다. payload와 인증 헤더를 섞어 로그에 남기지 않는다. 위 C1/C2 결과 비교 정책은 Worker에 별도로 둔다. [공식 API 구조](https://docs.typesafe.ai/api)

가상 응답 예:

```json
{
  "model": "provider-returned-model-revision",
  "answers": {
    "C1": {"type": "noul", "noul": 0.94},
    "C2": {"type": "choice", "choice": "SUFFICIENT", "confidence": 0.91,
      "probabilities": {"SUFFICIENT": 0.91, "INSUFFICIENT": 0.07, "CONTRADICTORY": 0.02}}
  },
  "usage": {"input_tokens": 420, "output_tokens": 60}
}
```

confidence가 0.91이라는 이유만으로 코드가 91% 맞다고 표시하지 않는다. 고위험 변경은 별도 도구 검증·사람 승인 기준을 유지한다. 질문·모델·문턱값은 실제 정답이 있는 작은 회귀 사례로 보정한다.

### 8.4 JEV v2 내부 전달 계약 제안

AI에게 자유 형식 로그 전체를 다시 쓰게 하지 않는다. Worker가 실행 증거를 원문 추출하고, 구현 결과 artifact와 검증 질문을 별도로 저장한다.

```text
EvidenceBundle
- schema_version, job_id, step_id, acceptance_revision
- base_commit_sha 또는 NONE, working_tree_digest
- 변경 파일 manifest, 허용된 diff 구간
- checks: command_id, exit_code, status, log_ref, evidence_digest
- UI 증거: artifact_ref, 실행 시각, 검증 대상 build ID
- known_gaps, truncated_fields, secret_redaction_status

JudgeEnvelope
- evidence_bundle_id + 질문/threshold/rubric revision
- validated_answers + provider model revision + usage
- decision: PASS / FAIL / ERROR / INSUFFICIENT_EVIDENCE
```

내용에 의미를 덧붙이는 요약은 AI가 맡고, Worker는 allowlist 파일·크기·hash·명령 결과를 기계적으로 수집한다. 기존 footer는 그대로 읽고, evidence는 별도 내부 envelope로 공급하는 호환 경로부터 만든다. v2가 안정화되기 전까지 **v1 PASS→Codex REPORT→Web** 동작은 유지한다.

v2 완료 후에만 저장된 구현 결과로 짧은 Web 보고를 만들고 PASS 후 Codex 보고 전용 호출을 제거한다. 이 변경은 최신 피드백의 v1 규칙을 바꾸는 별도 작업이므로 계약 문서와 회귀 검증을 함께 갱신해야 한다.

## 9. 역할별 설정과 Provider 교체 설계 — 개정안

**UI 요구:** 기존 메인 화면 하단의 단일 모델·추론 선택은 신규 목표에서 설정창 `AI 역할 설정`으로 이동한다. 각 역할 카드에 필수/선택 여부, ON/OFF(선택 역할만), 공급사, 실제 사용 가능한 모델, 해당 모델이 지원하는 추론 수준을 별도 표시한다. 설정 변경은 실행 중인 역할 세션의 모델을 바꾸지 않으며 다음 **새 Job 또는 안전한 새 단계**에만 적용한다. 새 모델로 바꿀 경우 세션 호환 여부를 확인하고 필요 시 짧은 인계 packet으로 신규 세션을 생성한다.

**모델 목록:** 초기 AI 공급사/모델 카탈로그는 **OpenAI(ChatGPT 계열)**만 표시한다. UI 목록의 모델명은 제품의 지원 보장이 아니라 후보이며 실제 Codex CLI 권한·모델 ID·reasoning 지원을 실행 전 점검한다. 향후 타사 추가를 위해 `provider_id`, `model_id`, `capabilities`를 분리하지만 타사 이름/미지원 옵션을 현재 UI에 노출하지 않는다.

**작업 판단 AI 예외:** JEV는 ChatGPT 모델이 아니라 **기존 전문 판단 엔진 연결**이다. 'AI 공급사' 목록에 다른 AI 업체를 임의로 추가하는 대신 판단 카드의 `판단 방식: JEV 연결 / 향후 OpenAI 판단 모델`로 구분한다. JEV 모드에서는 JEV 모델·timeout·인증을 해당 엔진 설정으로 표시하며 OpenAI 모델·reasoning 선택값이 JEV를 구동한다고 오해시키지 않는다. OpenAI 기반 판단 모델은 실제 어댑터 구현·검증 후 활성화한다.

| 내부 경계 | 최소 책임 |
| --- | --- |
| `ICoordinatorAdapter` | 설계·리뷰, 전용 CLI 세션, read-only, 카드·AC 반환, cancel/progress/usage |
| `IImplementerAdapter` | Luna 등 선택 모델 실행, 독립 session resume, 도구·테스트, 실제 증거 반환 |
| `IJudgeAdapter` | 선택적 JEV/향후 OpenAI 판단, typed 평가, timeout, model revision·usage |
| `IAdvancedImplementerAdapter` | 선택적 고수준 작업 위임, 독립 세션, 제한된 쓰기 lease, 결과 반환 |
| `JobRunner` | coordinator-first phase, AC/권한/예산/상태 복구와 중복 방지. WPF와 분리 |
| `EvidenceCollector` | 질문-증거 연결, source/diff/test/log digest, 비밀값 차단, freshness |

다음은 **미구현 목표 설정 형식**이다. 실제 모델·계정 지원은 실행 시 확인한다.

```json
{
  "schema_version": 2,
  "execution_mode": "cli_to_cli",
  "roles": {
    "coordinator": {
      "required": true, "enabled": true,
      "provider_id": "openai", "transport": "codex-cli",
      "model_id": "gpt-6-sol", "reasoning": "high", "permission_profile": "read-only"
    },
    "implementer": {
      "required": true, "enabled": true,
      "provider_id": "openai", "transport": "codex-cli",
      "model_id": "gpt-6-luna", "reasoning": "medium", "permission_profile": "approved-workspace-write"
    },
    "judge": {
      "required": false, "enabled": false,
      "backend": "jev", "engine_model": "jev-latest"
    },
    "advanced_implementer": {
      "required": false, "enabled": false,
      "provider_id": "openai", "transport": "codex-cli",
      "model_id": "gpt-6-sol", "reasoning": "high", "permission_profile": "delegated-write-only"
    }
  },
  "policy": {
    "allow_paid_fallback": false,
    "allow_automatic_escalation": false,
    "max_concurrent_workspace_writers": 1
  }
}
```

역할별 세션 ID, 실행 단계의 모델·추론·권한 snapshot 및 usage는 Job과 함께 저장한다. provider capability에는 `resume/tools/structured_output/progress/usage` 등을 포함하며 지원하지 않는 옵션은 disable·명시적 오류 처리한다.

**기존 Web 호환:** 현재 UI·브리지·ACTION/NEXT 계약은 09-B 실화면 검증을 먼저 끝내고 보존한다. 새 설정창과 CLI-to-CLI 어댑터는 이후 해당 작업에서 구현한다. 현재 제품에 새 JSON schema나 네 역할 설정창이 이미 있다고 기록하지 않는다.

## 10. 문서·증거 관리

| 문서/데이터 | 역할 | 재전송 정책 |
| --- | --- | --- |
| `AGENTS.md` | 변경 권한과 기본 정책 | 세션 시작 및 변경 시 |
| `Master-Polish.md` | 전체 설계, 양식, 우선순위 | 처음 설계 이해 시 또는 관련 절만 |
| `CurrentWork.md` | 최상단 현재 작업·완료/잔여 ID·검증 사실 | 작업 시작 시 최신 요약 |
| 활성 `tasks/*.md` | 현재 A/B/C 범위·AC·검증 명령 | 해당 작업 시작 시 |
| `GPT-Web-Feedback.md` | 동기화된 보조 지시/이력 | 승인된 fetch/pull 뒤 최신 추가분 분석 |
| runtime evidence/journal | 실행 사실·복구·상세 로그 | 필요한 구간만, Git 제외 |

후속 문서 정리 작업에서는 CurrentWork의 상단에 유일한 현재 요약을 두고 나머지 이력을 보존·분리한다. 검증 결과는 ‘계획/미실행/코드검토/단위/대체 검증/실화면’으로 구분한다. 빌드 성공으로 화면 성공을 대신 쓰지 않는다.

Git 리뷰에는 commit만으로 작업 중 변경을 대표시키지 않는다. 시작 commit + 현재 diff digest + 검증한 파일 digest를 묶는다. 검증 후 파일이 바뀌면 관련 증거를 무효화한다. Git/Server가 없어도 local evidence로 개발 가능하게 유지한다.

Endpoint 보안도 실제 코드 개선 항목이다. 현재 custom HTTPS endpoint에 같은 TypeSafe key를 보낼 수 있다. 공식 host allowlist와 provider별 별도 credential binding을 적용하고, 다른 host로 redirect될 때 credential 전달을 허용하지 않도록 검증한다. subprocess에도 필요한 환경변수만 전달해 Judge 키가 구현 도구에 불필요하게 상속되지 않도록 한다.

## 11. 단계별 실행 순서와 완료 기준

09-A/B/C 구현은 완료했다. 사용자는 09-B Explorer 실화면 E2E 잔여를 해결로 처리하고 정기 관리에서 제외했다. 09-C evidence envelope과 별도 Task 10의 capability, 계측, fixture 구현도 완료했다. 실 provider benchmark는 아직 실행하지 않았으며, 아래의 나머지 backlog는 전체 동시 구현 지시가 아니다. 한 단계의 완료 증거를 먼저 고정한 뒤 다음 하나만 활성화한다. 07 배포 패키지의 기존 잔여 작업은 별도로 보존한다.

| 순서/ID | 범위 | 완료 기준과 증거 |
| --- | --- | --- |
| **09-A 완료** | 현재 분석 + Master + 양식 | 문서·코드 근거 대조 및 parser harness 검증 완료 |
| **09-B 완료/관리 제외** | JEV v1 계약 정합성과 라우팅 보수 | Worker fixture 완료. Explorer 실화면 잔여는 사용자 결정으로 해결 처리; 재발하면 새 이슈로만 등록 |
| **09-C 완료** | 실제 증거 전달과 AC 고정 | evidence envelope/provenance/QID mapping/digest 기반 무효화 및 validation layer 기록 완료 |
| **Task 10-A 완료** | 검증 capability/layer 분리 | 도구 발견과 실제 validator 실행 결과 구분, ENGINE/UI/HUMAN/JEV 계층 별도 기록 |
| **Task 10-B 완료** | 호출별 token/payload 계측 | usage 중복 방지, unknown 보존, prompt/footer/evidence bytes·digest·latency 기록 |
| **Task 10-C 완료** | JEV 음성 대조 fixture | 6개 격리 사례 로컬 회귀 완료; provider 판별률은 미측정 |
| **11-A 완료 (2026-09-23)** | CLI-to-CLI 관제 우선, 역할별 provider/model/reasoning 및 capability 설정, 구현 결과 관제 검토 | 구조화 work card, 별도 workspace-write implementer, 동일 read-only coordinator review, observed exit-code gate; 28 tests, supported-model CLI smoke, Release Explorer launch |
| **11-B 후보** | JobRunner 분리와 재시작 복구 | 역할별 session/phase/model snapshot 복구, 중복 side effect 0, 기존 Web 회귀 없음 |
| **12-A 후보** | 대기·예산·반복 진척 | 제한/인증/timeout 대기·재개와 동일 실패 재분해, 무승인 사용량 증가 0 |
| **12-B 후보** | v2 evidence/report 전달 | 계약 호환 검증 후 report-only 호출 절감 및 정상 완료 증거 |
| **12-C 후보** | 선택 역할·provider 확장 | 필수 역할 회귀 뒤 mock 교체·lease·승인·budget 검사; 타사 실연동은 별도 승인 |

각 B/C도 너무 크면 활성화 전에 하위 검증 사례를 정리하되 동시에 다른 번호 작업을 열지 않는다. 디자인 polish는 오류 원인을 이해하고 재개하는 UI를 우선한다. 상태 카드에 현재 단계, 대기 이유, 다음 재개 조건, 이번 작업 사용량을 보여주고 기술적 상세는 펼침 영역에 둔다.

### 11.1 2026-09-23 현재 다음 진행

Master의 “번호 작업 하나 + A/B/C 하나” 원칙에 따라 다음 순서를 지킨다.

1. **09-B Explorer 실화면 E2E 잔여는 사용자 결정에 따라 해결 처리했고 정기 관리에서 제외한다.** 재발 시 새 증거로 다시 등록한다.
2. 09-C evidence envelope, provenance, QID mapping, digest 기반 무효화 구현이 완료됐다.
3. Task 10-A/B/C의 검증 layer 분리, 호출별 usage/payload 계측, 6개 JEV 음성 대조 fixture 구현이 완료됐다.
4. 실 provider의 batch 한계 및 음성 대조 판별률은 실제 요청·사용량이 없어 미측정이다.
5. Task 11-A coordinator-first CLI-to-CLI 구현을 완료했다. 다음 후보는 11-B JobRunner 분리·재시작 복구이며, 07 잔여 검증은 별도로 보존한다.

### 권장 검증 매트릭스

- Parser/evaluator: 정상 NOUL/SCORE/CHOICE, 빈 질문, inline PASS, 연속되지 않은 SCORE 번호, threshold 범위, 정의되지 않은 CHOICE, ID 누락, type mismatch, 잘못된 JSON, 범위 밖 숫자.
- 상태/비용: PASS 보고가 다시 JEV 요청, FAIL 후 WEB 전환, 같은 오류의 재등장, 중복 result/usage, 취소 직후 늦은 응답, budget unknown.
- 전송/복구: Web 새로고침·conversation 전환·전송 확인 불명, Worker crash, 네트워크 복구, 429/401/timeout.
- 실사용: **빌드된 실행파일을 Explorer에서 실행하고 실제 화면에 메시지를 작성·전송**하여 Judge OFF, NEXT WEB, JEV PASS/FAIL 보완, ERROR fallback을 확인한다. 불가능할 때만 CLI/API 대체 검증으로 기록한다.
- 품질/비용 비교: 같은 난이도의 작은 수정 3~5개에서 완료 AC당 Codex usage, JEV 호출, 재작업 수, 사람 개입 수, 거짓 PASS를 비교한다. 절약률은 측정 전 약속하지 않는다.

## 12. 이번 문서 작업의 검증 기록

검증 대상은 설계 문서와 복사 양식이다. 제품 소스·공개 계약·배포본은 변경하지 않는다.

- 저장소 동기화: 성공, 기준 HEAD `74fcc6a8bef9796ebc3355ebcb34a4bd37f0a804`.
- 최신 feedback 분석: 최종 JEV 본 구현 지시와 현재 코드의 차이를 3절에 기록.
- JEV 양식 검증: 임시 오프라인 .NET harness에서 실제 `JevContract.cs`로 NEXT JEV, 3개 질문, NOUL 0.90, SCORE 정규화 1.0, CHOICE 허용값 검사를 통과했다. inline PASS 단일 항목의 파싱 실패를 재현했다. JSON 예제 4개·로컬 링크·코드블록 경계도 통과했다. 명령은 [09 작업 기록](tasks/09-ai-role-dev-tool.md)에 남긴다.
- `git diff --check` 통과. 문서 4개만 변경했으며 소스 변경은 없다.
- 빌드/제품 전체 테스트/Explorer/API 재호출: 이번 문서 작업에서는 수행하지 않음. 기존 성공 이력과 분리한다.
- 잔여 제품 작업: Task 10 음성 대조군의 실 provider 측정은 실행되지 않았다. 사용자가 요청하면 사용량 확인 뒤 별도로 수행한다. 09-B는 사용자 결정으로 정기 관리에서 제외했으며 07 기존 잔여 실검증은 유지한다.

## 13. 참고와 적용 경계

- 저장소 기준: [작업 정책](AGENTS.md), [구현 계획](ProjectHub_IMPLEMENTATION_PLAN.md), [현재 상태](CurrentWork.md), [동기화된 피드백](GPT-Web-Feedback.md), [JEV footer v1](src/ProjectHub.Worker/JEV-FOOTER-CONTRACT.md), [JEV API 계약 v1](src/ProjectHub.Worker/JEV-API-CONTRACT.md).
- [Codex 비대화형 실행 공식 문서](https://learn.chatgpt.com/docs/non-interactive-mode): JSONL, session ID resume, 구조화 출력. resume는 문맥 비용 무료를 의미하지 않는다. 구조화 출력은 후속 검토 대상이며 현재 NEXT 계약을 즉시 대체하지 않는다.
- [TypeSafe SCORE 공식 문서](https://docs.typesafe.ai/primitives/score): ordered criteria 기반 평가. 현재 저장소의 사람용 1-based threshold 변환은 Worker 계약이다.

공식 문서 확인일은 2026-09-22다. 현재 모델·단가·제공 여부는 바뀔 수 있으므로 가격 수치나 무제한 무료 실행을 설계 전제로 고정하지 않는다. 이 Master는 사용자 요구를 반영한 상위 설계이며, 기존 정책과 충돌하는 향후 계약 변경은 해당 작업에서 명시적으로 다룬다.
