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
- `/api/status` 통합 테스트가 추가되어 HTTP 200과 기본 JSON 필드를 검증한다.
- Core에 `IProjectStateRepository`, `IProjectService`, `ProjectService`가 존재한다.
- Infrastructure에는 교체 가능한 `NoOpProjectStateRepository`와 DI 등록 경계가 있다.
- build/test 기록상 현재까지 오류 없이 통과했다.
- `CurrentWork.md` 기준 다음 작업은 `04 Supabase 스키마와 저장소의 A. Supabase 연결`이다.
- `ProjectHub_IMPLEMENTATION_PLAN.md`도 현재 작업을 `04 Supabase 스키마와 저장소`로 표시한다.

현재 프로젝트는 설계 골격 단계에서 실제 외부 저장소 연동 단계로 넘어갈 준비가 됐다.

---

## 가장 중요한 피드백

이제부터는 구조를 더 넓히기보다 **실제 E2E 한 줄을 먼저 성공시키는 것**을 최우선으로 한다.

목표:

```text
DEV PC
  -> POST /api/agent/heartbeat
  -> Mini PC ProjectHub.Server
  -> Supabase workstations upsert
  -> GET /api/workstations
  -> 동일 workstation 조회
```

이 흐름이 성공하면 ProjectHub의 첫 실사용 경로가 검증된 것으로 본다.

---

## 즉시 확인해야 할 문서 불일치

현재 `CurrentWork.md`는 다음 작업을:

```text
04-A Supabase 연결
```

로 표현하고 있다.

하지만 현재 `tasks/04_supabase-schema.md`의 A는:

```text
A. SQL 스키마와 인덱스 확정
```

으로 되어 있다.

즉, **04-A의 의미가 문서 사이에서 서로 다르다.**

Codex는 다음 작업 시작 전에 이 불일치를 정리해야 한다.

권장 방향은 기존 task를 크게 뒤엎는 것이 아니라, 실제 구현 순서를 E2E 중심으로 다시 세분화해 문서에 반영하는 것이다.

권장 순서:

```text
04-A Supabase 연결 설정/클라이언트 경계
04-B workstations 최소 스키마
04-C IWorkstationRepository + Supabase 구현
04-D heartbeat upsert
04-E GET /api/workstations
04-F 실제 PowerShell/curl E2E 검증
04-G 이후 project_states / events / leases 확장
```

기존 `04`의 최종 목표는 유지하되, 처음부터 모든 테이블과 저장소를 한 번에 구현하지 않는다.

---

## 추가 문서 정합성 확인

`ProjectHub_IMPLEMENTATION_PLAN.md`의 일부 task 링크 표기가 실제 파일명과 다를 가능성이 있다.

예를 들어 계획 문서에는:

```text
tasks/03-server-skeleton.md
tasks/04-supabase-schema.md
```

형태가 보이지만 실제 확인된 파일은:

```text
tasks/03_server-skeleton.md
tasks/04_supabase-schema.md
```

이다.

Codex는 문서 작업 시 실제 존재하는 경로 기준으로 링크를 정리한다.

이 문제는 기능 구현을 막지는 않지만, 향후 자동화/탐색에서 혼선을 만들 수 있으므로 04 작업 문서 갱신 시 함께 바로잡는 것을 권장한다.

---

## 04에서 권장하는 최소 구현 범위

### 1. Supabase 연결 경계

Server가 Supabase 구현을 직접 알지 않도록 한다.

권장:

```text
ProjectHub.Core
  IWorkstationRepository

ProjectHub.Infrastructure
  SupabaseWorkstationRepository
  SupabaseOptions
  AddProjectHubInfrastructure(...)

ProjectHub.Server
  환경 변수/설정 바인딩
  API endpoint 등록
```

`IProjectStateRepository`에 workstation/heartbeat 기능을 계속 추가하지 않는 것을 권장한다.

이유:
- Project 상태와 Workstation 상태는 수명주기와 조회 패턴이 다르다.
- 향후 Agent heartbeat가 자주 갱신되면 책임 분리가 유리하다.
- MCP/REST 양쪽에서 재사용하기 쉽다.

단, 과한 계층이나 generic repository는 만들지 않는다.

---

### 2. 첫 테이블은 workstations만

초기 최소 컬럼 예:

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
- `workstation_id` unique
- 같은 heartbeat가 반복되어도 row 중복 생성 금지
- `last_seen` 갱신 가능

처음부터 `projects`, `project_states`, `active_leases`, `project_events`를 전부 구현하지 않는다.

---

### 3. Supabase 비밀값 처리

Service Role Key는 Mini PC Server에만 둔다.

권장 환경 변수:

```text
PROJECTHUB_SUPABASE_URL
PROJECTHUB_SUPABASE_SERVICE_ROLE_KEY
```

금지:
- 실제 Key를 `appsettings.json`에 기록
- 실제 Key를 MD에 기록
- 실제 Key를 로그 출력
- Agent에 Service Role Key 배포

Agent는 항상 ProjectHub.Server를 통해서만 접근한다.

---

## 첫 API 범위

### POST /api/agent/heartbeat

예시:

```json
{
  "workstationId": "DEV-PC-01",
  "displayName": "DEV-PC-01",
  "hostname": "SUHO_DEV_PC"
}
```

서버 처리:

```text
validate
  -> workstation_id upsert
  -> last_seen 갱신
  -> Supabase 저장
  -> 성공 응답
```

초기 PoC에서는 인증 체계를 복잡하게 만들지 않는다.
LAN 내부 E2E 성공이 우선이다.

### GET /api/workstations

Supabase 저장 결과를 그대로 확인할 수 있는 최소 조회 API를 만든다.

---

## E2E 완료 조건

다음 조건을 모두 만족하면 첫 연동 성공으로 본다.

```text
[ ] Mini PC Server가 Supabase에 실제 연결됨
[ ] 개발 PC에서 heartbeat POST 성공
[ ] workstations row 생성
[ ] 동일 heartbeat 재전송 시 중복 row 생성 안 됨
[ ] last_seen 갱신됨
[ ] GET /api/workstations에서 동일 PC 조회됨
[ ] Server 재시작 후에도 동일 데이터 조회됨
```

가능하면 실제 검증 명령과 결과를 task 문서에 기록한다.

---

## `/api/status`에 대한 다음 피드백

현재 `/api/status`가 `database = "supabase"`를 고정 반환한다면, 실제 Supabase 연결이 들어가는 시점부터는 설정 대상과 연결 상태를 구분하는 편이 좋다.

예:

```json
{
  "server": "ProjectHub",
  "status": "ok",
  "storage": "supabase",
  "storageStatus": "connected"
}
```

연결되지 않았을 때는:

```text
not-configured
unavailable
```

등으로 구분할 수 있다.

단, heartbeat E2E보다 이 상태 API 정교화가 우선되어서는 안 된다.

---

## 아직 하지 말 것

첫 heartbeat E2E 성공 전에는 아래를 미룬다.

```text
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

지금 목표는 기능 수가 아니라 **Mini PC가 실제 중앙 서버 역할을 하기 시작하는 것**이다.

---

## Codex 수행 권장 방식

1. 작업 시작 전에 정책/상태/task/이 파일을 읽는다.
2. 먼저 04 task와 CurrentWork의 A 단계 의미 불일치를 정리한다.
3. 실제 구현은 Supabase 연결 + workstation 최소 경로부터 시작한다.
4. 한 번에 하나의 세부 작업만 수행한다.
5. build/test 결과는 실제 실행 결과만 기록한다.
6. 외부 Supabase 변경이 필요한 경우 어떤 SQL/설정이 필요한지 명확히 기록한다.
7. 실제 E2E 검증 전에는 성공으로 표시하지 않는다.
8. 사용자 승인 없이 commit/push하지 않는다.

---

## 현재 권장 다음 행동

가장 먼저:

```text
04 작업 문서의 세부 순서를 E2E 중심으로 정합화
```

그 직후:

```text
Supabase 연결
  -> workstations 최소 스키마
  -> heartbeat upsert
  -> workstations 조회
  -> DEV PC -> Mini PC -> Supabase 실제 검증
```

으로 진행한다.

현재까지의 구조는 적절하다. 이제는 설계 확장보다 **첫 실제 데이터 왕복 성공**을 우선한다.
