# 12 제공자 추상화

갱신일: 2026-09-24

정책 원본은 `Master-Polish.md`다.

## A — 제공자 기반

완료 범위:
- `AiServiceProvider`: OpenAI / Claude / Muse
- stable 전송 ID: `openai` / `claude` / `muse`
- 제공자/모델 설명자와 OpenAI 목록
- 기존 JSON 제공자 문자열 호환
- UNKNOWN 제공자 자동 대체 금지

## B — 제공자 확장 준비 UI + 실행기 경계

이번 활성 작업이며, 실제 유료 제공자 연동 전까지 가능한 내부 구조를 한 번에 완성한다.

### 설정/UI

- HQ / WORK / HIGH 각각 독립 제공자 선택 상자
- 제공자 변경 시 해당 목록의 model 목록 재구성
- 모델 변경 시 해당 모델의 추론 목록 재구성
- HIGH 제공자를 WORK 제공자와 완전히 분리해 저장
- 목록가 없는 Claude/Muse는 제공자 선택은 가능하되 model/reasoning에 “연결 후 로드” 상태 표시
- 세션 기능가 없는 제공자의 세션 선택기 비활성

### 시각 요소

- `ProviderVisualCatalog` 단일 해석기
- OpenAI: current-openai 컬러/회색
- Claude/Muse: 공식 심볼 자산이 준비될 때까지 current-console color/gray 대체
- 설정창, pipeline, 신규 role 이력가 해석기를 사용
- 실제 브랜드 asset 추가 시 해석기만 교체

### 실행기

- `IAiRoleRunner`
- `AiRoleRunnerRegistry`
- `OpenAiCodexRoleRunner`
- Claude/Muse용 `UnconfiguredAiRoleRunner`
- 관제 우선 HQ/WORK/HIGH 실행은 등록소를 경유
- Claude/Muse 선택 시 `CLAUDE_NOT_CONFIGURED` / `MUSE_NOT_CONFIGURED`
- 미연결 제공자를 OpenAI로 대체하지 않음

### 사전 점검 / 마이그레이션

- OpenAI 제공자는 Codex CLI 전송/인증/모델/추론을 검증
- HIGH 허가가 있을 때만 HIGH 제공자 사전 점검 포함
- CLI_TO_CLI 과거 OpenAI coordinator transport=web 값은 실행 시점에서 codex_cli로 정규화
- LEGACY_WEB의 명시적 web transport는 유지

### 회귀 기준

- 기존 OpenAI HQ/WORK/HIGH 모델·추론 설정 동작 유지
- 기존 `provider: "openai"` JSON 호환
- Claude/Muse/UNKNOWN 제공자가 OpenAI로 자동 전환되지 않음
- HIGH 제공자 독립 저장
- GOTO/JUDGE/UNKNOWN/HIGH 허가 의미 변경 없음
- 제공자 visual 대체 asset이 존재
- 세션 미지원 제공자 thread 선택기 비활성

### 검증 상태

코드와 회귀 테스트를 함께 작성한다. 현재 GPT 실행 환경에서는 .NET SDK를 실행할 수 없으므로 commit 전에는 저장소 정적 검토를 수행하고, Windows 환경의 `dotnet test`, `dotnet build`, Explorer 확인은 잔여로 기록한다.

## D — 실제 외부 제공자 연동

Claude/Muse를 실제로 사용하기 시작할 때만 수행한다.

- 공식 CLI/실행 규격 확정
- 인증
- 실제 model 탐색 또는 유지 가능한 목록 출처
- reasoning 기능
- 세션/resume
- 계측 연결
- 실제 HQ/WORK/HIGH E2E

D 전까지 ProjectHub는 OpenAI만 실제 실행하고 Claude/Muse는 명시적 NOT_CONFIGURED 상태를 유지한다.
