# GPT Web Feedback

Updated: 2026-09-15

## 목적

이 파일은 ChatGPT Web이 GitHub 저장소를 검토한 뒤 Codex에게 전달하는 전용 피드백/작업 제안 채널이다.
기존 `AGENTS.md`, `CurrentWork.md`, `ProjectHub_IMPLEMENTATION_PLAN.md`, `tasks/*.md`의 정책과 우선순위를 우선 적용하고, 이 파일은 그 위에서 보조 지시 역할만 한다.

Codex는 이 파일을 읽은 뒤 필요한 변경을 스스로 판단해 수행하되, 기존 작업 규칙을 깨지 않는다.

---

## 현재 확인한 상태

- `ProjectHub.sln`과 `src/ProjectHub.Core`, `ProjectHub.Infrastructure`, `ProjectHub.Server`, `ProjectHub.Agent` 구조가 존재한다.
- 테스트 프로젝트도 생성되어 있다.
- `02 개발환경 기반구축`은 완료 상태다.
- 현재 활성 작업은 `03 Server 스켈레톤`이다.
- `03-A /api/status` 완료.
- `03-B ProjectHub 서비스 등록` 완료.
- 현재 다음 작업은 `03-C Server 테스트 추가`다.
- Core에 `IProjectStateRepository`, `IProjectService`, `ProjectService`, `ProjectState` 등이 추가됐다.
- Infrastructure에는 교체 가능한 `NoOpProjectStateRepository`와 DI 등록 경계가 추가됐다.
- Server는 아직 `/api/status` 중심이며 Supabase 실제 연결은 아직 하지 않았다.
- `CurrentWork.md`의 Git 기준도 `main / origin/main` 추적으로 정리됐다.

현재까지의 방향은 적절하며, 문서 상태와 실제 코드 구조도 대체로 일치한다.

---

## 가장 중요한 다음 목표

이제 설계 골격을 오래 확장하기보다 가능한 빨리 첫 실제 E2E 연동을 만든다.

최우선 검증 흐름:

```text
DEV PC
  -> POST /api/agent/heartbeat
  -> Mini PC ProjectHub.Server
  -> Supabase workstations 저장
  -> GET /api/workstations
  -> 저장 결과 확인
```

이 한 줄이 실제로 동작하면 ProjectHub의 핵심 통신 구조가 검증된 것으로 본다.

---

## 다음 작업 순서

### 1. 03-C는 최소 범위로 빠르게 완료

테스트 인프라를 과도하게 확장하지 않는다.

최소 검증:
- `/api/status`가 HTTP 200을 반환한다.
- 응답에 `server`, `status` 등 필수 필드가 존재한다.
- 기존 DI 구성이 애플리케이션 시작을 깨지 않는지 확인한다.

03-C의 목적은 서버 스켈레톤을 안정적으로 닫는 것이다.
새 기능을 여기서 추가하지 않는다.

---

### 2. 04는 E2E 우선으로 작게 진행

`04 Supabase 스키마와 저장소`를 한 번에 모두 구현하지 않는다.

권장 실제 순서:

```text
04-A Supabase 설정/연결 경계
04-B workstations 테이블
04-C workstation repository 구현
04-D POST /api/agent/heartbeat
04-E GET /api/workstations
04-F PowerShell/curl E2E 검증
04-G 이후 나머지 project 상태 테이블
```

처음에는 `projects`, `project_states`, `active_leases`, `project_events`를 모두 만들 필요가 없다.

---

## 첫 Supabase 범위

### workstations 테이블만 먼저 사용

초기 최소 필드 예:

```text
workstation_id
 display_name
 hostname
 last_seen
 created_at
 updated_at
```

핵심은 `workstation_id` 기준 upsert와 `last_seen` 갱신이다.

---

## Repository 구조 피드백

현재 `IProjectStateRepository`는 Project 상태용 계약으로 유지하는 편이 좋다.

heartbeat/workstation 기능을 여기에 계속 추가하면 역할이 빠르게 커질 수 있으므로, 실제 구현 단계에서 필요성이 확인되면 별도 계약을 권장한다.

예:

```text
IWorkstationRepository
IProjectStateRepository
```

권장 방향:

```text
ProjectHub.Core
  IWorkstationRepository
  IProjectStateRepository
  IProjectService
  ProjectService

ProjectHub.Infrastructure
  SupabaseWorkstationRepository
  SupabaseProjectStateRepository   (후속)
```

단, 이를 위해 현재 완료된 03-B를 다시 크게 리팩터링하지 않는다.
04에서 workstation 기능이 실제로 필요해지는 순간 최소 변경으로 추가한다.

---

## Supabase 연결 원칙

Agent가 Supabase에 직접 접속하지 않는다.

반드시:

```text
DEV PC / Agent
  -> ProjectHub.Server
  -> Infrastructure Repository
  -> Supabase
```

구조로 한다.

Supabase Service Role Key는 Mini PC Server에서만 환경 변수로 읽는다.

금지:
- Key를 `appsettings.json`에 실제 값으로 저장
- Key를 Git에 commit
- Agent에 Service Role Key 배포

---

## 첫 API 목표

### POST /api/agent/heartbeat

예시:

```json
{
  "workstationId": "DEV-PC-01",
  "displayName": "DEV-PC-01",
  "hostname": "SUHO_DEV_PC"
}
```

서버 동작:

```text
1. 요청 유효성 확인
2. workstation_id 기준 upsert
3. last_seen = 현재 시각
4. Supabase 저장
5. 성공 응답
```

초기 검증에서는 Agent 인증을 지나치게 복잡하게 만들지 않는다.
내부 LAN PoC가 먼저다.

---

### GET /api/workstations

Supabase에 저장된 workstation 목록을 읽어 반환한다.

예시 응답:

```json
[
  {
    "workstationId": "DEV-PC-01",
    "displayName": "DEV-PC-01",
    "hostname": "SUHO_DEV_PC",
    "lastSeen": "2026-09-15T14:30:00+09:00"
  }
]
```

---

## 첫 E2E 검증 방법

Agent 프로그램을 먼저 만들 필요는 없다.

개발 PC에서 PowerShell/curl로 Mini PC에 직접 호출한다.

예:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://MINI-PC:5070/api/agent/heartbeat `
  -ContentType "application/json" `
  -Body '{"workstationId":"DEV-PC-01","displayName":"DEV-PC-01","hostname":"SUHO_DEV_PC"}'
```

이후:

```powershell
Invoke-RestMethod http://MINI-PC:5070/api/workstations
```

동일 PC가 조회되면 첫 E2E 성공으로 본다.

가능하면 추가 확인:
- 같은 heartbeat를 다시 보내도 row가 중복 생성되지 않는다.
- `last_seen`만 갱신된다.
- Server 재시작 후에도 데이터가 조회된다.

---

## `/api/status` 피드백

현재 `database = "supabase"` 표기는 실제 연결 성공 여부와 혼동될 수 있다.

Supabase 연결을 실제 구현하는 시점에는 다음처럼 역할을 구분하는 것을 권장한다.

```json
{
  "server": "ProjectHub",
  "status": "ok",
  "storage": "supabase",
  "storageStatus": "connected"
}
```

연결 전에는:

```json
{
  "storage": "supabase",
  "storageStatus": "not-configured"
}
```

다만 03-C를 완료하기 위해 굳이 지금 응답 계약을 흔들 필요는 없다.
04에서 실제 연결 상태를 제공할 때 정리해도 된다.

---

## 현재 단계에서 하지 말 것

첫 heartbeat E2E가 성공하기 전에는 아래를 미룬다.

```text
- project_states 전체 구현
- active_leases
- project_events 확장
- FileSystemWatcher
- Git HEAD / dirty / changed files 수집
- Agent 전체 구현
- MCP
- GitHub Bridge
- NAS Gateway
- 자동 Git commit/push/reset/merge
- 원격 shell 실행
- 복잡한 인증 시스템
```

목표는 기능 수가 아니라 **실제 end-to-end 성공**이다.

---

## Codex에게 바라는 수행 방식

1. 작업 시작 전 다음 파일을 읽는다.
   - `AGENTS.md`
   - `ProjectHub_IMPLEMENTATION_PLAN.md`
   - `CurrentWork.md`
   - 현재 활성 `tasks/*.md`
   - `GPT-Web-Feedback.md`

2. 기존 활성 task가 최우선이다.

3. 현재는 `03-C`를 최소 범위로 완료한다.

4. 완료 후 문서 상태와 실제 build/test 결과를 갱신한다.

5. 그 다음 `04`를 heartbeat E2E 중심으로 세분화해 진행한다.

6. 한 번에 하나의 세부 작업만 수행한다.

7. 사용자 승인 없이 commit/push하지 않는다.

---

## 현재 권장 다음 행동

```text
03-C Server 테스트 추가
```

를 최소 범위로 완료한다.

그 직후 우선순위는:

```text
Supabase 연결
  -> workstations
  -> heartbeat 저장
  -> workstations 조회
  -> 실제 DEV PC -> Mini PC -> Supabase E2E 확인
```

이다.

ProjectHub 전체 기능보다 이 첫 실제 연동 성공을 우선한다.
