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

### C. IWorkstationRepository와 Supabase 구현

- workstation 전용 저장소 계약과 PostgREST upsert/list 구현을 추가한다.

### D. heartbeat upsert

- `POST /api/agent/heartbeat` 요청 검증, upsert, last_seen 갱신을 구현한다.

### E. workstation 조회

- `GET /api/workstations`로 저장된 workstation 목록을 반환한다.

### F. 실제 E2E 검증

- PowerShell/curl로 heartbeat POST 후 목록 GET, 중복 방지, last_seen 갱신, 서버 재시작 후 조회를 확인한다.

### G. Project 상태 확장

- 첫 heartbeat E2E 성공 후 projects, project_states, project_events, active_leases를 단계적으로 추가한다.

## 진행

잔여 작업 5개 (C, D, E, F, G)

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

## 사용자 수행 필요

- Supabase 프로젝트를 생성하고 URL과 Service Role Key를 Mini PC Server 실행 환경에 `PROJECTHUB_SUPABASE_URL`, `PROJECTHUB_SUPABASE_SERVICE_ROLE_KEY`로 등록한다.
- 실제 키는 이 저장소나 문서에 기록하지 않는다.
- `supabase/workstations.sql`을 Supabase SQL Editor에서 실행하고 테이블 생성 여부를 확인한다.
