# Supabase 스키마와 저장소

## 목표

Supabase `workstations` 최소 경로를 먼저 실제 E2E로 연결하고, 이후 Project 상태 저장으로 확장할 수 있는 기반을 마련한다.

## 현재 기준

Server와 Infrastructure DI 경계가 존재한다. 04-A에서 환경 변수 기반 Supabase 설정과 HTTP 클라이언트 경계를 추가했으며, 실제 Supabase 계정·키·스키마는 아직 연결하지 않았다.

## 세부 작업

### A. Supabase 연결 설정/클라이언트 경계 (완료: 2026-09-15)

- `SupabaseOptions`가 설정에 지정된 환경 변수에서 URL과 Service Role Key를 읽는다.
- Infrastructure가 named `HttpClient`를 등록하며 REST base address와 인증 헤더를 구성한다.
- 환경 변수가 없을 때도 서버가 기동되며 실제 비밀값은 저장소에 기록하지 않는다.

### B. workstations 최소 스키마 (완료: 2026-09-15)

- `supabase/workstations.sql`에 `workstation_id` unique, display name, hostname, last_seen, created/updated 시각, updated_at trigger, RLS 활성화를 정의했다. 사용자가 Supabase SQL Editor에서 실행해야 한다.

### C. IWorkstationRepository와 Supabase 구현 (완료: 2026-09-15)

- `IWorkstationRepository`와 `SupabaseWorkstationRepository`를 추가했다. 기존 named `HttpClient("Supabase")`를 통해 PostgREST upsert/list를 수행한다.

### D. heartbeat upsert (완료: 2026-09-15)

- `POST /api/agent/heartbeat` 요청 검증, 서버 시각 기반 `last_seen` 생성, `workstation_id` 충돌 병합 upsert와 503 오류 응답을 구현했다.

### E. workstation 조회 (완료: 2026-09-15)

- `GET /api/workstations`로 Supabase `workstations` 목록을 반환한다.

### F. 실제 E2E 검증 (완료: 2026-09-15)

- 사용자가 Mini PC와 Supabase에서 heartbeat POST, row 생성, 동일 workstation upsert, `last_seen` 갱신, 목록 GET, 서버 재시작 후 persistence를 확인했다.

### G. Project 상태 확장 (진행)

- 첫 heartbeat E2E 성공 후 projects와 project_states 최소 저장 경로부터 단계적으로 추가한다.

#### G-A. projects/project_states 최소 스키마 설계 (완료: 2026-09-15)

- `supabase/project-state.sql`에 프로젝트 식별자와 workstation별 최신 상태의 unique 제약을 정의했다.
- 사용자가 Supabase SQL Editor에서 실행하고 테이블 생성을 확인했다.

#### G-B. project state 저장소와 수동 API (완료: 2026-09-15)

- `SupabaseProjectStateRepository`가 `projects`와 `project_states`를 순서대로 upsert한다.
- `POST /api/projects/{projectId}/state`로 수동 상태를 저장한다.
- `GET /api/projects/{projectId}/states`와 `/states/{workstationId}`로 상태를 조회한다.
- 자동 Git 수집과 FileSystemWatcher는 구현하지 않는다.

## 진행

잔여 작업 1개 (G-C)

## 변경 금지

- Service Role Key를 코드·문서·로그에 기록하지 않는다.
- Agent가 Supabase에 직접 접근하지 않는다.
- 첫 heartbeat E2E 전에는 FileSystemWatcher, Git 상태 수집, MCP, NAS, GitHub Bridge, 복잡한 인증을 구현하지 않는다.

## 완료 기준

- heartbeat가 `workstations`에 중복 없이 저장되고 조회 API에서 확인된다.

## 검증 방법

- `dotnet build ProjectHub.sln --no-restore`
- `dotnet test ProjectHub.sln --no-restore`
- Supabase 환경 구성 후 PowerShell/curl E2E 명령

## 결과

- A 완료: 환경 변수 기반 `SupabaseOptions`와 Supabase REST named `HttpClient` 경계를 추가했다. 환경 변수가 없어도 서버가 기동되며 비밀값은 저장소에 기록하지 않는다. `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(2개 통과).
- B 완료: `supabase/workstations.sql`을 추가했다. 실제 Supabase 적용은 아직 하지 않았다.
- C 완료: workstation 저장소 계약과 PostgREST 구현을 추가했다.
- D 완료: heartbeat upsert API를 추가했다.
- D 보완: upsert 요청에서 null 메타데이터 필드를 제외하고 Supabase 오류 본문을 읽어 502 응답에 포함하도록 수정했다.
- E 완료: workstation 목록 조회 API를 추가했다.

F 완료: 사용자가 실제 Mini PC→Supabase E2E를 검증했다.
G-A 완료: `supabase/project-state.sql`을 작성했고 사용자가 Supabase 적용 및 테이블 생성을 확인했다.
G-B 완료: project state 저장소와 수동 POST/GET API를 구현했다. `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(2개 통과).

## 사용자 수행 필요

- Supabase 프로젝트를 생성하고 URL과 Service Role Key를 Mini PC Server 실행 환경에 `PROJECTHUB_SUPABASE_URL`, `PROJECTHUB_SUPABASE_SERVICE_ROLE_KEY`로 등록한다.
- 실제 키는 이 저장소나 문서에 기록하지 않는다.
- `supabase/workstations.sql`을 Supabase SQL Editor에서 실행하고 테이블 생성 여부를 확인한다.
- Mini PC에서 Server를 재시작한 뒤 `/api/status`, heartbeat POST, `/api/workstations`를 순서대로 호출한다.
- Mini PC에서 최신 코드를 반영하고 Server를 재시작한다.
- 수동 project state POST 후 `projects`, `project_states` row 생성을 확인한다.
- GET 두 API와 동일 project/workstation 재전송 시 update를 확인한다.
