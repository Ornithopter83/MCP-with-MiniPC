# 12 Provider abstraction

Updated: 2026-09-24

정책 원본은 `Master-Polish.md`다.

## A — Provider foundation

목표는 ProjectHub가 특정 회사/모델 문자열에 직접 결합되지 않도록 Provider 식별과 모델 capability의 첫 경계를 만든다.

### 범위

- `AiServiceProvider` enum: OpenAI / Claude / Muse
- 영속 wire id: `openai` / `claude` / `muse`
- `AiProviderDescriptor`
- `AiModelDescriptor`
- `AiProviderCatalog`
- 기존 `CodexServedModels`를 OpenAI descriptor의 모델 source로 사용
- `WorkerAiRoleSettings.ProviderKind` typed view
- 기존 JSON provider 문자열과 역호환
- unknown provider 자동 OpenAI fallback 금지
- Claude/Muse는 descriptor만 존재하고 `ExecutionConfigured=false`
- JEV는 Provider enum에 넣지 않음

### 이번 단계에서 하지 않음

- Claude/Muse 실제 CLI/API 호출
- Claude/Muse 인증
- Claude/Muse 모델명 하드코딩
- 설정 UI에 Claude/Muse 노출
- provider별 icon asset
- runner interface 전환
- 기존 Codex 실행 경로 변경
- GOTO/JUDGE/HIGH/History 변경

### 다음 단계

B:
- HQ/WORK/HIGH provider ComboBox 통일
- provider 선택 → model catalog
- model 선택 → reasoning options
- HIGH 독립 provider selector
- provider visual resolver/fallback symbol

C:
- `IAiRoleRunner` 경계
- 기존 OpenAI Codex runner adapter
- OpenAI-only 회귀
- Claude/Muse runner skeleton
- 미설정 preflight

D:
- 실제 Claude/Muse CLI 규격이 확정된 뒤 인증/session/resume/E2E

### 회귀 기준

- 기존 `provider: "openai"` 설정이 그대로 읽힘
- enum typed view는 OpenAI로 해석
- `provider: "claude"` / `"muse"`도 typed view로 구분
- unknown provider는 null/unsupported이고 OpenAI로 대체되지 않음
- OpenAI generic catalog의 모델/reasoning이 기존 CodexServedModels와 동일
- Claude/Muse는 현재 실행 가능으로 표시하지 않음

### 검증 상태

코드/테스트 구조를 작성한다. 현재 GPT 실행 환경에서는 .NET SDK를 직접 실행할 수 없으므로 Windows 환경의 `dotnet test` / `dotnet build` 실검증은 후속으로 남긴다.
