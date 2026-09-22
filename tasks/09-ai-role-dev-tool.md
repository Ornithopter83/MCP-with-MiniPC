# 09 AI Role Dev Tool 설계와 검증 기반

Updated: 2026-09-22

## 목표

토큰 절약을 최우선으로 무료 ChatGPT Web 관제 → Codex Luna Medium 구현 → JEV 판단 흐름을 정리하고, 작은 작업을 지속적으로 완성하는 기준을 세운다.

## 현재 기준

- 2026-09-22 사용자 요청으로 이번에는 **09-A 문서 설계만** 수행한다.
- 동기화 기준 `74fcc6a8bef9796ebc3355ebcb34a4bd37f0a804`, 최신 feedback 변경 `ac47ba5`.
- 기존 활성 `07-project-deployment-package.md`의 잔여 검증은 보존하며 동시에 수행하지 않는다.
- 상세 설계와 복사 양식은 [Master-Polish.md](../Master-Polish.md)를 참조한다.

## 세부 작업

### A. 현재 구현 분석과 Master 설계 문서 — 완료 (2026-09-22)

- 현재 구현과 사용자 요구의 차이, 토큰 낭비 원인, 목표 역할·상태·복구 구조를 정리한다.
- 사용자/관제/구현/JEV 양식을 제공하고 현행 파서 호환 양식과 미래 API evidence 양식을 구분한다.
- 공개 계약과 제품 소스는 변경하지 않는다.
- 사용자가 승인한 Git 동기화·문서 commit/push 범위로 완료한다.

### B. JEV v1 계약 정합성과 라우팅 보수 — 대기

- 최신 feedback에 요구된 ERROR/FAIL 구분과 입력·응답 검증, REPORT 경계, PASS 재진입 차단을 먼저 구현한다.
- Worker 전용 parser/evaluator/라우팅 fixture 테스트를 마련한다.
- 실제 Explorer 실행본에서 Judge OFF/ON, PASS/FAIL, ERROR fallback을 검증한다.
- 범위 확정과 실행 지시 후 B 하나만 활성화한다.

### C. 증거 전달과 수용 조건 고정 — 대기

- 작업 AC와 검증한 diff/파일·도구 결과를 연결한다.
- JEV에 실제 증거를 전달하고 증거 부족과 오래된 PASS의 재사용을 막는다.
- v1 wire 형식을 보존하는 내부 evidence envelope부터 도입한다.

## 진행

- 완료: `09-A`, 2026-09-22.
- 잔여: `09-B`, `09-C`.
- Master의 `10-A`~`11-C`는 향후 후보이며 아직 활성 작업이 아니다.
- 07의 Force Restore GUI 계속/취소 등 기존 잔여 검증을 완료로 바꾸지 않는다.

## 변경 금지

- 이번 A에서 제품 코드·JEV 원 계약·브라우저 동작·배포본을 바꾸지 않는다.
- 기존 Agent → Server → Supabase와 NAS 경계를 보존한다.
- 현재 문서 push 승인을 향후 자동 Git/배포의 포괄 승인으로 사용하지 않는다.

## 완료 기준

- Master에 세 가지 우선순위, 코드 근거, 실행 단계와 검증 방법이 있다.
- JEV 복사 form이 실제 현행 parser를 통과한다.
- 구현 완료·과거 검증 이력·미구현 제안을 구분한다.
- 문서 변경만 커밋하며 원격 도달 여부를 확인한다.

## 결과와 검증

2026-09-22: `Master-Polish.md` 작성, 구현계획·현재상태의 최신 요약 갱신. 비용 절약, 관제 우선 시작, 실제 증거 기반 JEV, 복구 가능한 Job, 단계적 provider 교체를 제안했다.

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
