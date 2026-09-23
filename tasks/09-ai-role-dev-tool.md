# 09 AI Role Dev Tool 설계와 검증 기반

Updated: 2026-09-22

## 목표

토큰 절약을 최우선으로 무료 ChatGPT Web 관제 → Codex Luna Medium 구현 → JEV 판단 흐름을 정리하고, 작은 작업을 지속적으로 완성하는 기준을 세운다.

## 현재 기준

- 2026-09-22 최신 feedback 기준으로 **09-B JEV v1 계약 정합성과 라우팅 보수**를 활성화했다.
- 동기화 기준 최신 HEAD `2d75139`, 최신 feedback 섹션 `09-B JEV v1 계약 정합성·라우팅 보수`.
- 기존 활성 `07-project-deployment-package.md`의 잔여 검증은 보존하며 동시에 수행하지 않는다.
- 상세 설계와 복사 양식은 [Master-Polish.md](../Master-Polish.md)를 참조한다.

## 세부 작업

### A. 현재 구현 분석과 Master 설계 문서 — 완료 (2026-09-22)

- 현재 구현과 사용자 요구의 차이, 토큰 낭비 원인, 목표 역할·상태·복구 구조를 정리한다.
- 사용자/관제/구현/JEV 양식을 제공하고 현행 파서 호환 양식과 미래 API evidence 양식을 구분한다.
- 공개 계약과 제품 소스는 변경하지 않는다.
- 사용자가 승인한 Git 동기화·문서 commit/push 범위로 완료한다.

### B. JEV v1 계약 정합성과 라우팅 보수 — 구현 완료, Explorer 검증 대기 (2026-09-22)

- 최신 feedback에 요구된 ERROR/FAIL 구분과 입력·응답 검증, REPORT 경계, PASS 재진입 차단을 먼저 구현한다.
- Worker 전용 parser/evaluator/라우팅 fixture 테스트를 마련한다.
- 실제 Explorer 실행본에서 Judge OFF/ON, PASS/FAIL, ERROR fallback을 검증한다.
- `JevContract`가 첫 NEXT/REPORT 경계, NOUL·SCORE·CHOICE 구조·범위·허용값을 검증한다.
- `JevJudgeRunner`가 TypeSafe 응답의 누락·알 수 없는 ID·타입·범위 오류를 ERROR로, 조건 불충족만 FAIL로 분류한다.
- 같은 Codex 세션에서 JEV FAIL 재시도와 PASS report-only 단계를 수행하며 report-only 단계의 JEV 재진입을 차단한다.
- mock HTTP handler 기반 Worker fixture 테스트를 추가했다.

### C. 증거 전달과 수용 조건 고정 — 대기

- 작업 AC와 검증한 diff/파일·도구 결과를 연결한다.
- JEV에 실제 증거를 전달하고 증거 부족과 오래된 PASS의 재사용을 막는다.
- v1 wire 형식을 보존하는 내부 evidence envelope부터 도입한다.

## 진행

- 완료: `09-A`, `09-B`, 2026-09-22.
- 잔여: `09-C` 및 실제 Explorer 실행본 검증.
- Master의 `10-A`~`11-C`는 향후 후보이며 아직 활성 작업이 아니다.
- 07의 Force Restore GUI 계속/취소 등 기존 잔여 검증을 완료로 바꾸지 않는다.

## 변경 금지

- 09-B에서는 Worker 제품 코드와 fixture 테스트를 변경하되, JEV 원 계약·브라우저 확장·배포본은 변경하지 않는다.
- 기존 Agent → Server → Supabase와 NAS 경계를 보존한다.
- 현재 문서 push 승인을 향후 자동 Git/배포의 포괄 승인으로 사용하지 않는다.

## 완료 기준

- Master에 세 가지 우선순위, 코드 근거, 실행 단계와 검증 방법이 있다.
- JEV 복사 form이 실제 현행 parser를 통과한다.
- 구현 완료·과거 검증 이력·미구현 제안을 구분한다.
- 구현·fixture 검증·문서 상태를 함께 기록한다. commit/push는 별도 사용자 승인 범위에서만 수행한다.

## 결과와 검증

2026-09-22 09-A: `Master-Polish.md` 작성, 구현계획·현재상태의 최신 요약 갱신. 비용 절약, 관제 우선 시작, 실제 증거 기반 JEV, 복구 가능한 Job, 단계적 provider 교체를 제안했다.

실행 명령:

```powershell
git -c safe.directory=C:/AI-AGENT/ProjectHub fetch
git -c safe.directory=C:/AI-AGENT/ProjectHub pull --rebase
dotnet run --project "$env:TEMP\ProjectHub-MasterPolish-09A\FormCheck.csproj" -- C:\AI-AGENT\ProjectHub\Master-Polish.md
git -c safe.directory=C:/AI-AGENT/ProjectHub diff --check
```

- fetch/pull 성공, Already up to date.
- 임시 오프라인 .NET harness에서 현행 `JevContract.cs`를 직접 링크해 NEXT JEV, NOUL 0.90, SCORE 4단계/정규화 1.0, CHOICE 허용값을 확인했다.
- 기존 inline PASS 단일 항목의 파싱 실패도 재현했다. 제품 코드는 수정하지 않았다.
- JSON 예제 4개 파싱, Master의 로컬 링크, 코드블록 경계 검사를 통과했다.
- 임시 harness는 저장소 밖 테스트 자료이며 영구 테스트 프로젝트가 아니다. 실제 JEV API/Explorer E2E를 대신한 결과가 아니다.
- `git diff --check` 통과. 변경은 Master·현재상태·구현계획·09 task 문서 4개로 한정한다.
- 제품 전체 build/test, 실제 API 호출, Explorer 검증과 배포는 이번 문서 범위에서 실행하지 않았다.

## 2026-09-23 후속 보강

- MESSAGE 누적 표시를 가상화 목록으로 변경해 전체 로그 재조합·대형 TextBlock 재렌더링을 제거했다.
- JEV footer가 누락되지 않도록 모든 Codex 실행 경로를 공통 실행 메서드로 통합했다.
- GPT Web에도 JEV 사용 우선 지침을 전달하도록 Web prompt를 보강했다.
- 검증: Debug build 성공, 전체 테스트 10개 통과, extension `node --check`, `git diff --check` 성공.
- 잔여: 실제 Explorer 화면에서 장시간 누적 스크롤과 JEV ON 왕복 검증.

## 2026-09-23 Extension 단계 보고 및 전송 복구

- Bridge `/bridge/progress`와 Worker `WEB EXTENSION` MESSAGE 로그를 추가했다.
- Extension은 전송 단계별 상태를 보고하고 composer·Send·실제 user message 확인을 여유 있게 재시도한다.
- Extension build `2026-09-23.1`로 동기화 기준을 갱신했다.
- 검증: Node 구문 검사, Debug build, 전체 테스트 10개, diff check 통과.
- 실제 Chrome 화면 검증은 Extension 새로고침 후 잔여.

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
