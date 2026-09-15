# GPT Web Feedback

Updated: 2026-09-15

## 목적

이 파일은 ChatGPT Web이 GitHub 저장소를 검토한 뒤 Codex에게 전달하는 전용 피드백/작업 제안 채널이다.

우선순위는 항상 다음과 같다.

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. 이 `GPT-Web-Feedback.md`

이 파일은 기존 관리 파일을 대체하지 않는다. Codex는 이 피드백을 읽고 필요한 변경을 기존 작업 흐름 안에서 스스로 반영한다.

---

## 현재 확인한 상태

- `03 Server 스켈레톤`은 완료됐다.
- `/api/status` 통합 테스트가 존재하고 HTTP 200 및 기본 JSON 필드를 검증한다.
- `04 Supabase 스키마와 저장소`가 현재 활성 작업이다.
- `04-A Supabase 연결 설정/클라이언트 경계`는 완료됐다.
- `SupabaseOptions`가 환경 변수에서 URL과 서버용 비밀키를 읽는다.
- Infrastructure에 named `HttpClient("Supabase")`가 등록되어 `/rest/v1/` BaseAddress와 인증 헤더를 구성한다.
- 환경 변수가 없어도 서버 기동은 가능하도록 되어 있다.
- 실제 Supabase 네트워크 호출과 `workstations` 테이블은 아직 구현/검증 전이다.
- 현재 `CurrentWork.md` 기준 다음 작업은 `04-B workstations 최소 스키마`다.

현재까지의 방향은 적절하다.

---

## 사용자 측 서버 준비 상태

사용자가 Mini PC 서버 측 Supabase 준비를 완료했다.

확인된 사항:

```text
Supabase 프로젝트 생성 완료
PROJECTHUB_SUPABASE_URL 등록 완료
PROJECTHUB_SUPABASE_SERVICE_ROLE_KEY 등록 완료
```

Project URL도 확보되어 있으며 실제 비밀키 값은 저장소/문서에 기록하지 않는다.

중요:
- 위 환경 변수는 **Mini PC 서버 실행 환경에 설정된 값**으로 간주한다.
- Codex가 실행되는 개발 PC에 동일한 환경 변수가 있다고 가정하지 않는다.
- 로컬 테스트에서 환경 변수가 없다는 이유로 서버 PC 설정이 실패했다고 판단하지 않는다.
- 실제 비밀값을 출력하거나 문서화하지 않는다.

---

## 지금부터 가장 중요한 목표

이제 설계 확대보다 첫 실제 E2E를 최대한 빨리 완성한다.

목표 경로:

```text
DEV PC
  -> POST /api/agent/heartbeat
  -> Mini PC ProjectHub.Server
  -> Supabase workstations upsert
  -> GET /api/workstations
  -> 동일 workstation 조회
```

이 흐름이 성공하면 ProjectHub의 첫 실사용 데이터 경로가 검증된 것으로 본다.

---

## 다음 작업: 04-B workstations 최소 스키마

다음 세부 작업은 `04-B`다.

최소 컬럼은 다음 수준을 권장한다.

```text
id
workstation_id
display_name
hostname
last_seen
created_at
updated_at
```

필수 조건:

```text
workstation_id UNIQUE NOT NULL
last_seen timestamptz
created_at timestamptz default now()
updated_at timestamptz default now()
```

과도한 컬럼, trigger, 정책, migration framework를 먼저 추가하지 않는다.

### SQL 전달 방식

사용자가 Supabase SQL Editor에서 직접 실행할 수 있도록 **정확한 SQL을 저장소에 별도 파일로 남기는 것을 권장**한다.

예시 위치:

```text
sql/001_create_workstations.sql
```

또는 현재 프로젝트 구조에 더 자연스러운 별도 경로가 있다면 그 경로를 사용한다.

이 SQL 파일은 다음 목적을 가진다.

- 사용자가 그대로 복사해 Supabase SQL Editor에서 실행 가능
- 이후 환경 재구축 시 재사용 가능
- 어떤 스키마가 실제 서버에 적용됐는지 추적 가능

단, SQL 파일을 작성했다고 해서 실제 Supabase에 적용됐다고 표시하지 않는다.

Codex는 외부 Supabase 상태를 임의로 성공 처리하지 말고 다음처럼 구분한다.

```text
SQL 작성 완료
!=
사용자 Supabase 적용 완료
```

사용자가 적용 완료를 알려준 뒤 실제 E2E 검증으로 넘어간다.

---

## 04-C 이후 권장 구현 순서

04-B 후에는 다음 순서가 적절하다.

```text
04-C IWorkstationRepository + SupabaseWorkstationRepository
04-D POST /api/agent/heartbeat
04-E GET /api/workstations
04-F Mini PC에서 실제 E2E 검증
04-G Project 상태 저장 확장
```

### Repository 경계

`IProjectStateRepository`에 heartbeat 기능을 넣지 않는다.

권장:

```text
ProjectHub.Core
  IWorkstationRepository
  Workstation

ProjectHub.Infrastructure
  SupabaseWorkstationRepository
```

필요한 기능은 최소 두 개면 충분하다.

```text
UpsertAsync(...)
ListAsync(...)
```

필요 이상으로 generic repository나 추상 계층을 추가하지 않는다.

---

## Supabase REST 구현 주의점

현재 프로젝트는 Supabase .NET SDK보다 PostgREST HTTP 경계를 이미 마련했으므로, 첫 E2E에서는 그 방향을 유지하는 편이 단순하다.

heartbeat upsert에서는 반드시 `workstation_id` 충돌 시 update가 되도록 구현한다.

검증해야 할 동작:

```text
첫 heartbeat -> row 1개 생성
같은 workstation_id heartbeat 재전송 -> row 수 유지
last_seen -> 새로운 시각으로 갱신
```

API/헤더 세부 구현은 현재 named HttpClient 경계를 재사용한다.

---

## POST /api/agent/heartbeat

최소 요청 예:

```json
{
  "workstationId": "DEV-PC-01",
  "displayName": "DEV-PC-01",
  "hostname": "SUHO_DEV_PC"
}
```

최소 처리:

```text
1. 필수 값 검증
2. 서버에서 last_seen 현재 시각 생성
3. workstation_id 기준 Supabase upsert
4. 성공/실패를 명확한 HTTP 상태로 반환
```

클라이언트가 보낸 시각을 신뢰하기보다 첫 버전에서는 서버 시각을 사용하는 것을 권장한다.

---

## GET /api/workstations

Supabase의 `workstations` 목록을 반환한다.

첫 버전에서는 복잡한 paging/filtering/sorting이 필요 없다.

목적은 저장 결과를 사람이 즉시 확인하는 것이다.

---

## Mini PC에서 실제로 검증해야 할 순서

코드 준비 및 SQL 적용 후 사용자가 Mini PC에서 수행할 수 있도록 정확한 명령을 문서에 남긴다.

권장 검증 순서:

```text
1. 새 PowerShell에서 환경 변수 존재 확인
2. ProjectHub.Server 실행
3. /api/status 확인
4. 개발 PC 또는 Mini PC에서 heartbeat POST
5. GET /api/workstations 확인
6. heartbeat 재전송
7. row 중복 없음 + last_seen 변경 확인
8. 서버 재시작
9. GET /api/workstations에서 데이터 유지 확인
```

비밀키는 검증 명령 출력에 노출하지 않는다.

---

## E2E 성공 기준

다음이 모두 실제 확인되기 전에는 04-F를 완료 처리하지 않는다.

```text
[ ] workstations SQL이 실제 Supabase에 적용됨
[ ] Mini PC Server가 환경 변수를 읽고 실행됨
[ ] Supabase REST 호출 성공
[ ] heartbeat POST 성공
[ ] workstations row 생성
[ ] 동일 workstation 재전송 시 중복 없음
[ ] last_seen 갱신
[ ] GET /api/workstations 조회 성공
[ ] Server 재시작 후 데이터 유지
```

---

## `/api/status` 관련

현재 `database = "supabase"` 고정 표시는 당장 막는 요소가 아니다.

첫 E2E 성공이 우선이다.

그 이후 필요하면 다음처럼 바꿀 수 있다.

```json
{
  "server": "ProjectHub",
  "status": "ok",
  "storage": "supabase",
  "storageStatus": "connected"
}
```

그러나 `/api/status`에서 매 요청마다 Supabase에 불필요한 네트워크 체크를 넣지는 않는다.

---

## 아직 하지 말 것

첫 heartbeat E2E 성공 전에는 아래를 미룬다.

```text
- projects 전체 구현
- project_states 전체 구현
- active_leases
- project_events 확장
- FileSystemWatcher
- Git HEAD / dirty / changed files 수집
- Agent 전체 자동화
- 멀티 PC 충돌 판정
- MCP
- GitHub Bridge
- NAS Gateway
- 자동 Git commit/push/reset/merge
- 원격 shell 실행
- 복잡한 인증
- Docker/Redis/자체 PostgreSQL
```

지금 목표는 Mini PC가 실제로 **한 건의 workstation 상태를 Supabase에 기록하고 다시 읽어오는 것**이다.

---

## Codex 수행 지침

1. 기존 정책/현재 상태/04 task/이 파일을 먼저 읽는다.
2. 현재 사용자 측 Supabase 프로젝트와 Mini PC 환경 변수 준비는 완료된 상태로 취급한다.
3. 다음 세부 작업은 `04-B workstations 최소 스키마`다.
4. SQL을 재사용 가능한 파일로 저장하고, 사용자가 Supabase SQL Editor에서 실행해야 할 내용을 명확히 남긴다.
5. 외부 DB에 실제 적용됐다고 임의로 기록하지 않는다.
6. 이후 04-C~E는 첫 heartbeat E2E에 필요한 최소 코드만 구현한다.
7. build/test 결과와 실제 E2E 결과를 구분해서 문서화한다.
8. 비밀키/토큰은 코드, 문서, 로그, 테스트 출력에 남기지 않는다.
9. 사용자 승인 없이 commit/push하지 않는다.

---

## 현재 권장 다음 행동

즉시 진행할 작업:

```text
04-B workstations 최소 스키마 작성
  -> SQL 파일 제공
```

그 후 사용자가 SQL을 실제 Supabase에 적용하면:

```text
04-C Workstation Repository
  -> 04-D heartbeat POST
  -> 04-E workstations GET
  -> 04-F 실제 Mini PC E2E
```

로 진행한다.

현재 단계에서는 추가 설계보다 **첫 실제 Supabase 데이터 왕복 성공**을 가장 우선한다.
