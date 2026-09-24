# 12 Provider abstraction

Updated: 2026-09-24

정책 원본은 `Master-Polish.md`다.

## A — Provider foundation

완료 범위:
- `AiServiceProvider`: OpenAI / Claude / Muse
- stable wire id: `openai` / `claude` / `muse`
- provider/model descriptor와 OpenAI catalog
- 기존 JSON provider 문자열 호환
- unknown provider 자동 fallback 금지

## B — Provider-ready UI + runner boundary

이번 활성 작업이며, 실제 유료 Provider 연동 전까지 가능한 내부 구조를 한 번에 완성한다.

### 설정/UI

- HQ / WORK / HIGH 각각 독립 Provider ComboBox
- Provider 변경 시 해당 catalog의 model 목록 재구성
- Model 변경 시 해당 model의 reasoning 목록 재구성
- HIGH Provider를 WORK Provider와 완전히 분리해 저장
- catalog가 없는 Claude/Muse는 Provider 선택은 가능하되 model/reasoning에 “연결 후 로드” 상태 표시
- session capability가 없는 Provider의 session selector 비활성

### Visual

- `ProviderVisualCatalog` 단일 resolver
- OpenAI: current-openai color/gray
- Claude/Muse: 공식 symbol asset이 준비될 때까지 current-console color/gray fallback
- 설정창, pipeline, 신규 role History가 resolver를 사용
- 실제 브랜드 asset 추가 시 resolver만 교체

### Runner

- `IAiRoleRunner`
- `AiRoleRunnerRegistry`
- `OpenAiCodexRoleRunner`
- `UnconfiguredAiRoleRunner` for Claude/Muse
- coordinator-first HQ/WORK/HIGH 실행은 registry를 경유
- Claude/Muse 선택 시 `CLAUDE_NOT_CONFIGURED` / `MUSE_NOT_CONFIGURED`
- 미연결 Provider를 OpenAI로 대체하지 않음

### Preflight / migration

- OpenAI provider는 Codex CLI transport/auth/model/reasoning을 검증
- HIGH permit이 있을 때만 HIGH Provider preflight 포함
- CLI_TO_CLI 과거 OpenAI coordinator transport=web 값은 runtime에서 codex_cli로 normalize
- LEGACY_WEB의 명시적 web transport는 유지

### 회귀 기준

- 기존 OpenAI HQ/WORK/HIGH 모델·추론 설정 동작 유지
- 기존 `provider: "openai"` JSON 호환
- Claude/Muse/unknown provider가 OpenAI로 자동 전환되지 않음
- HIGH provider 독립 저장
- GOTO/JUDGE/UNKNOWN/HIGH permit 의미 변경 없음
- Provider visual fallback asset이 존재
- session 미지원 Provider thread selector disabled

### 검증 상태

코드와 회귀 테스트를 함께 작성한다. 현재 GPT 실행 환경에서는 .NET SDK를 실행할 수 없으므로 commit 전에는 저장소 정적 검토를 수행하고, Windows 환경의 `dotnet test`, `dotnet build`, Explorer 확인은 잔여로 기록한다.

## D — 실제 외부 Provider 연동

Claude/Muse를 실제로 사용하기 시작할 때만 수행한다.

- 공식 CLI/실행 규격 확정
- 인증
- 실제 model discovery 또는 유지 가능한 catalog source
- reasoning capability
- session/resume
- telemetry mapping
- 실제 HQ/WORK/HIGH E2E

D 전까지 ProjectHub는 OpenAI만 실제 실행하고 Claude/Muse는 명시적 NOT_CONFIGURED 상태를 유지한다.
