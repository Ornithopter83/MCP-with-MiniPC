# ProjectHub 현재 작업 상태

Updated: 2026-09-16

## Baseline

- 저장소: `C:\Projects\AI-AGENTS\MCP\Server`
- 브랜치: `main` (원격 `origin/main` 추적)
- 도구체인: .NET SDK 9.0.312 확인
- 솔루션: `ProjectHub.sln`
- 활성 작업: `tasks/05_agent-state.md`

## 확인된 현재 구현

- `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent` 프로젝트가 생성됐다.
- `tests/ProjectHub.Core.Tests`, `Server.Tests` 프로젝트가 생성됐다.
- 프로젝트 참조가 Core 중심 계층으로 연결됐다.
- ASP.NET Core Server는 `/api/status`, workstation heartbeat/list, project state 수동 POST/GET API를 제공하며 Supabase REST 연결을 사용한다.
- `GPT-Web-Feedback.md`가 원격에서 추가됐으며, pull 전 확인 규칙을 `AGENTS.md`에 반영했다.
- Core 서비스 계약과 Infrastructure의 교체 가능한 NoOp 저장소 DI 등록 경계를 추가했다.
- `/api/status` 통합 테스트가 추가되어 HTTP 200과 기본 JSON 필드를 검증한다.
- `SupabaseOptions`와 named `HttpClient` 등록 경계가 추가됐으며 Server의 실제 Supabase E2E가 완료됐다.
- 04-A Supabase 설정/클라이언트 경계 구현과 빌드·테스트 검증이 완료됐다.
- `supabase/workstations.sql`과 `supabase/project-state.sql`을 Supabase에 적용하고 실제 row 저장을 확인했다.
- `IWorkstationRepository`, `SupabaseWorkstationRepository`, heartbeat upsert와 workstation 조회 API의 실제 Mini PC→Supabase E2E가 완료됐다.
- heartbeat upsert payload에서 null 메타데이터 필드를 제외하고, Supabase 오류 응답 본문을 읽어 502로 반환하도록 보완했다.
- 사용자가 Mini PC와 Supabase에서 04-F 실제 E2E를 완료했다. heartbeat upsert, 중복 방지, `last_seen` 갱신, workstation 조회, 서버 재시작 후 persistence를 확인했다.
- `supabase/project-state.sql`에 04-G 첫 단계의 `projects`와 `project_states` 최소 스키마를 정의하고 사용자가 Supabase에서 실행했다.
- `SupabaseProjectStateRepository`와 수동 project state POST/GET API를 구현했다.
- 사용자가 서버 PC에서 project state POST/GET, Supabase row 저장, 동일 project/workstation 재전송 update를 검증했다.
- `head_sha`에 실제 커밋 SHA `cebda36a4937056e9abd11254131ee42ad7afc83`가 저장된 것을 확인했다.
- `ProjectHub.Agent`가 설정 기반 heartbeat sender/runner로 구현됐으며 실제 외부 DEV PC E2E까지 완료됐다.
- `GitStateCollector`가 등록된 localPath에서 branch, HEAD full SHA, dirty, changed/untracked/deleted 수를 읽기 전용으로 수집한다. 05-B 관련 테스트가 통과했다.
- `ProjectActivityMonitor`가 등록 프로젝트를 감시하고 1초 debounce 후 Git 상태를 기존 project-state API로 전송한다. 실제 DEV PC 루트 프로젝트 외부 E2E까지 검증했다.
- 실제 DEV PC 저장소 루트에서 임시 파일 생성 후 공식 HTTPS 터널 경유 Agent → Server → Supabase 상태 갱신을 확인했다. `dirty=true`, `untracked_count=1`, `last_file_activity` 갱신을 확인하고 임시 파일·설정을 복구했다.
- 임시 E2E에서 발견한 `last_file_activity` 누락을 수정해 파일 이벤트 처리 시각을 자동 기록하도록 보완했다. 빌드·테스트 재검증도 통과했다.
- Server 기본 origin을 표준 `Urls` 설정으로 `http://127.0.0.1:5240`에 고정하고, `ASPNETCORE_URLS` 또는 실행 인자로 재정의할 수 있게 했다.
- 사용자가 Cloudflare Named Tunnel `projecthub`와 `projecthub.ornithopter.bid`를 구성하고 외부 `/api/status` 성공을 확인했다. Agent 외부 E2E는 아직 검증하지 않았다.

## 목표 구조

개발 PC Agent가 heartbeat와 Git 상태를 Server에 보내고, Server의 ProjectService가 Infrastructure 저장소를 통해 Supabase에 상태·이벤트·lease를 기록한다.

## 진행

잔여 작업 4개 (06, 07, 08, 09)

## 작업 정책

- 한 번에 하나의 세부 작업만 수행한다.
- 자동 Git 변경·원격 명령·파일 삭제는 금지한다.
- 비밀값은 환경 변수로만 읽고 저장소에 기록하지 않는다.

## 표준 검증

```powershell
dotnet build ProjectHub.sln --no-restore
dotnet test ProjectHub.sln --no-restore
```

## 다음 작업

06 Large Data/NAS: ProjectHub-native assertion issuer와 NAS Gateway 계약
