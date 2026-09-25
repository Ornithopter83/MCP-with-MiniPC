# 10 검증 기능·사용량 관찰·JEV 기준실험

갱신일: 2026-09-23

## 목표

JEV 의미 검증과 엔진 실행, 브라우저 UI, 사용자 체감 확인의 범위를 분리하고, 호출별 실측 사용량과 통제된 JEV 음성 대조 픽스처를 제공한다.

## 세부 작업

### A. 검증 기능 및 계층 분리 — 완료 (2026-09-23)

- ENGINE_HEADLESS, UI_BROWSER, HUMAN_UX, JEV의 기능/상태/실행기/scope를 별도로 기록한다.
- 실행 가능 도구가 발견됐다는 사실을 validator 실행 PASS로 표시하지 않는다.
- 브라우저 adapter가 없으면 UI 검증을 BLOCKED_BY_TOOL로 남기며, 실행·결과 evidence는 독립 검증으로 보존한다.
- 결과는 09-C 봉투 구조와 함께 round state에 기록한다. 엔진 실행 명령·validator를 추론해서 실행하지 않는다.

### B. 호출별 사용량과 데이터 묶음 계측 — 완료 (2026-09-23)

- Codex/JEV의 작업·round·role·model·reasoning·목적, 토큰 사용량, prompt/footer/evidence/데이터 묶음 bytes, 지연 시간, 재시도 및 사용량 확인 여부을 호출 단위로 보존한다.
- Codex의 누적 사용량 스냅샷을 중복 합산하지 않는다. JEV/Web 제공자 usage가 오지 않으면 UNKNOWN으로 기록한다.
- token cost 또는 절감률을 실제 제공자 usage 없이 추정하지 않는다.
- Codex/JEV/Web 각각 role별 호출 JSONL을 `%Worker%/state/usage/<job-id>/calls.jsonl`에 남긴다. 본문은 기록하지 않고 prompt/footer/데이터 묶음 SHA-256만 저장한다.
- Codex usage 파서는 `total_token_usage`의 최신 스냅샷을 사용하고, `last_token_usage`만 있으면 증분 합산하며, 구형 일반 usage 객체는 마지막 값만 선택한다. 제공자가 제공하지 않은 제공자 total은 `null`이다.
- JEV 응답 usage가 없으면 usage_known=false, token 필드는 null로 남긴다. GPT Web 사용량은 항상 UNKNOWN으로 기록한다.

### C. JEV 음성 대조 픽스처 — 구현 완료 (2026-09-23; 제공자 측정 미실행)

- MiniStore 유사 격리 픽스처에 알려진 결함과 증거 모순을 구조화한다.
- 결정적 validator 결과와 JEV 판정 결과를 다른 필드로 저장한다.
- 픽스처 정의와 로컬 회귀만으로 JEV 탐지율을 주장하지 않는다. 실 제공자 실행은 사용량·외부 요청 승인 조건에서 별도로 수행한다.
- `JevNegativeControls.json`에 SALE/재고/원장 불일치, 중복 복원, CLOSED 상태 입고, 소스/실행 시점 모순, 미실행 validator의 허위 PASS 등 6개 격리 사례를 정의하고 테스트 assembly resource로 회귀 검증한다.

## 완료 및 잔여

- A/B/C 각 결과, 완료일, 실제 검증 명령, 실 제공자 호출 여부를 기록한다.
- 09-C evidence 봉투 구조와 공개 JEV v1 wire 계약을 바꾸지 않는다.
- 07 Force Restore 잔여 및 사용자의 09-B 해결 처리와 혼합하지 않는다.

## 결과와 검증

2026-09-23 10-A/B/C: 봉투 구조에 ENGINE_HEADLESS/UI_BROWSER/HUMAN_UX/JEV 기능·상태를 추가했다. 호출별 Codex/JEV/Web usage, 데이터 묶음 크기·digest·지연을 로컬 JSONL에 보존하고 usage 미제공은 UNKNOWN으로 구분한다. Codex usage 중복 집계를 cumulative/증분 규칙으로 수정했다. JEV 음성 대조 6건 픽스처를 추가했다. 검증: `dotnet test ProjectHub.sln --configuration Debug --no-restore` 통과 (Core 1, Agent 3, Server 1, Worker 16), `git diff --check`, Release publish 성공. `C:\AI-AGENT\Worker`/`C:\GameProject` 복사본 SHA-256 모두 `CCD2C0B67FC85652B5387D7E54D3FD69A337D7DFD9BAE21768958866C3868E55`이며 Bridge 준비 완료, Extension 동기화됨=true다. 실 TypeSafe 벤치마크는 호출/비용 측정하지 않았으므로 판별 성공률은 미측정이다.
