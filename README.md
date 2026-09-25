# ProjectHub

Mini PC를 중앙 프로젝트 상태 서버로 사용하는 ProjectHub v0.2 초기 골격이다.

## 구성

- `src/ProjectHub.Core`: 도메인 모델과 핵심 로직
- `src/ProjectHub.Infrastructure`: Supabase·Git·파일시스템 연동
- `src/ProjectHub.Server`: ASP.NET Core Minimal API
- `src/ProjectHub.Agent`: 개발 PC 상태 수집 Agent
- `tests/`: Core와 Server 테스트

## 실행

```powershell
dotnet run --project src/ProjectHub.Server
```

상태 확인: `GET /api/status`

구현 로드맵과 작업 순서는 [ProjectHub_IMPLEMENTATION_PLAN.md](ProjectHub_IMPLEMENTATION_PLAN.md)를 참고한다.

## GPTWeb-Hub Worker 최신 상태

Worker는 하나의 작업 안에서 Codex CLI 결과를 GPT Web으로 전달하고, Web 응답의 ACTION에 따라 다음 Codex 라운드를 진행하거나 PAUSE/END로 종료한다. 후속 Web 라운드에는 최초 COMMAND를 중복해서 보내지 않고, 현재 라운드 결과를 전달한다.

작업 메시지는 실행 중에도 실시간 이벤트로 기록된다. Coordinator-first 작업은 작업 폴더 아래의 `.projecthub`에 상태와 로그를 남긴다.

<작업 폴더>\.projecthub\session-state.json
<작업 폴더>\.projecthub\last-handoff.md
<작업 폴더>\.projecthub\events\<jobId>.jsonl
<작업 폴더>\.projecthub\transcripts\<jobId>.txt

`events/<jobId>.jsonl`은 이벤트 발생 시마다 한 줄 JSON으로 append되며 Full Message를 보존한다. 재개 가능한 작업은 Worker 재시작 후 `session-state.json`에서 복구할 수 있다.

### 알려진 제약

- 빌드·테스트와 함께 빌드된 Worker 실행파일 및 연결된 GPT Web의 스무고개 다중 왕복 E2E를 확인했다. ACTION=CONTINUE 반복과 ACTION=END 종료가 정상 동작했다.
- 확장이 응답 완료/Stop 상태를 보고하지 않으면 Worker가 GPT Web 응답 완료를 확정할 수 없다.
- CLI 사용량가 제공되지 않는 경우 누적 토큰은 정확한 계정 한도 조회값이 아니라 CLI 응답에서 추출 가능한 값의 합계다.