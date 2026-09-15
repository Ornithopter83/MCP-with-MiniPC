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
- 현재 로드맵상 `02 개발환경 기반구축`은 완료 상태다.
- 현재 활성 작업은 `03 Server 스켈레톤`이다.
- `03-A /api/status`는 완료되었고, 다음은 `03-B ProjectHub 서비스 등록`, 이후 `03-C Server 테스트 추가`다.
- 현재 `Program.cs`는 `/api/status`만 제공하는 최소 ASP.NET Core Minimal API 상태다.
- Core와 Infrastructure는 아직 실질 구현이 거의 없고 기본 골격 수준이다.

---

## 우선 피드백

### 1. 현재 방향은 유지한다

전체 방향은 적절하다.

```text
DEV PC Agent
  -> Mini PC ProjectHub.Server
  -> ProjectService / Infrastructure
  -> Supabase
```

초기 목표는 ProjectHub 전체 기능이 아니라, 서버에서 최소 기능 하나를 끝까지 연결해 실제 동작을 확인하는 것이다.

---

### 2. 가장 먼저 검증할 E2E 목표

다음 한 줄이 실제로 동작하는 것을 최우선으로 한다.

```text
DEV PC
  -> POST /api/agent/heartbeat
  -> Mini PC ProjectHub.Server
  -> Supabase 저장
  -> GET /api/workstations
  -> 저장 결과 확인
```

이 흐름이 성공하면 서버의 핵심 통신 구조가 검증된 것으로 본다.

Agent 프로그램 전체, Git 상태 수집, MCP, NAS, GitHub Bridge는 이 이후로 미룬다.

---

## 권장 작업 순서

### 03-B: 최소 DI 경계 구성

과도하게 구현하지 말고 아래 수준만 만든다.

권장 구조:

```text
ProjectHub.Core
  - IProjectStateRepository
  - IProjectService
  - ProjectService

ProjectHub.Infrastructure
  - 저장소 구현을 연결할 수 있는 DI 경계

ProjectHub.Server
  - Core / Infrastructure 서비스 등록
```

중요:
- 이 단계에서 Supabase 실제 네트워크 호출은 아직 하지 않아도 된다.
- Server가 Supabase 구현 세부사항에 직접 결합되지 않도록 한다.

---

### 03-C: 최소 서버 테스트

테스트 인프라를 과도하게 확장하지 않는다.

최소 확인:
- `/api/status`가 200 응답
- 기본 JSON 필드 확인

---

### 04 작업은 작게 쪼갠다

기존 `04 Supabase 스키마와 저장소`를 실제 구현 시 다음처럼 작은 단위로 나누는 것을 권장한다.

```text
04-A Supabase 연결
04-B workstations 테이블
04-C heartbeat 저장
04-D workstations 조회
04-E 나머지 project 상태 테이블
```

처음부터 `projects`, `project_states`, `active_leases`, `events`를 전부 구현하지 않는다.

---

## 첫 실제 연동 범위

다음 기능만 우선 구현한다.

### POST /api/agent/heartbeat

예시 요청:

```json
{
  "workstationId": "DEV-PC-01",
  "displayName": "DEV-PC-01"
}
```

서버 동작:

```text
1. 요청 수신
2. workstation_id 기준 upsert
3. last_seen 갱신
4. Supabase 저장
5. 성공 응답
```

### GET /api/workstations

Supabase의 workstation 목록을 조회해 반환한다.

개발 PC에서는 초기 테스트를 Agent 대신 PowerShell/curl로 수행해도 된다.

예:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://MINI-PC:5070/api/agent/heartbeat `
  -ContentType "application/json" `
  -Body '{"workstationId":"DEV-PC-01","displayName":"DEV-PC-01"}'
```

이후:

```powershell
Invoke-RestMethod http://MINI-PC:5070/api/workstations
```

에서 동일 workstation이 조회되면 1차 E2E 성공이다.

---

## 이번 단계에서 하지 말 것

초기 E2E 검증 전에는 아래를 구현하지 않는다.

```text
- project_states 전체 구현
- active_leases
- FileSystemWatcher
- Git HEAD / dirty / changed files 수집
- MCP
- GitHub Bridge
- NAS Gateway
- 자동 Git commit/push/reset/merge
- 원격 shell 실행
```

---

## 구조 관련 피드백

### `/api/status`의 Supabase 표기

현재 `/api/status`가 실제 연결 여부와 무관하게 `database = "supabase"`를 반환한다면, 이후 실제 연결이 들어갈 때는 설정 대상과 연결 상태를 구분하는 편이 좋다.

예:

```json
{
  "server": "ProjectHub",
  "status": "ok",
  "storage": "supabase",
  "storageStatus": "not-configured"
}
```

또는 실제 연결 후:

```json
{
  "storage": "supabase",
  "storageStatus": "connected"
}
```

이 변경은 현재 03 작업 범위를 깨지 않는 시점에 반영한다.

---

## 문서 정합성 피드백

`CurrentWork.md`에 로컬 기준으로 `브랜치: 아직 Git 초기화 전` 같은 문구가 남아 있다면 현재 저장소 상태와 맞지 않을 수 있다.
GitHub에는 이미 ProjectHub 초기화 및 후속 커밋이 존재하므로, Codex가 다음 작업 상태 문서를 갱신할 때 실제 현재 Git 상태에 맞춰 정리한다.

단, 이 파일 작성 시점에는 기존 문서를 직접 수정하지 않는다. Codex가 정상 작업 흐름 안에서 갱신한다.

---

## Codex에게 바라는 수행 방식

1. 먼저 기존 정책 파일을 읽는다.
   - `AGENTS.md`
   - `ProjectHub_IMPLEMENTATION_PLAN.md`
   - `CurrentWork.md`
   - 현재 활성 task 파일
   - 이 `GPT-Web-Feedback.md`

2. 현재 활성 세부 작업 범위를 우선한다.

3. 이 피드백과 기존 task가 충돌하면 기존 프로젝트 정책/task를 우선하고, 필요한 경우 피드백을 참고해 더 작은 구현 단위로 진행한다.

4. 한 번에 하나의 세부 작업만 수행한다.

5. 구현 후 실제 build/test 결과만 기록한다.

6. 기존 정책대로 사용자 승인 없이 commit/push하지 않는다.

---

## 현재 권장 다음 행동

현재 상태 기준으로 가장 적절한 다음 작업은:

```text
03-B ProjectHub 서비스 등록
```

이다.

목표는 기능 확장이 아니라 **DI 경계와 Core/Infrastructure/Server 분리 기반을 최소한으로 만드는 것**이다.

그 다음 `03-C`를 짧게 완료하고, 가능한 빨리 `heartbeat -> Supabase -> 조회` E2E 검증으로 넘어가는 것을 권장한다.
