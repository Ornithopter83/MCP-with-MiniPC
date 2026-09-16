# GPT Web Feedback

Updated: 2026-09-16

## 목적

이 파일은 ChatGPT Web이 GitHub 저장소를 검토한 뒤 Codex에게 전달하는 전용 피드백/작업 제안 채널이다.

우선순위는 항상 다음과 같다.

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. 이 `GPT-Web-Feedback.md`

이 파일은 기존 관리 파일을 대체하지 않는다. Codex는 이 피드백을 읽고 기존 작업 흐름 안에서 필요한 변경만 반영한다.

---

## 최종 목적 — 반드시 유지할 기준

ProjectHub의 장기 최종 목적은 아래 두 가지다.

### 1. 어떤 프로젝트든 연결 + 대용량 데이터 자동 버전관리

ProjectHub는 특정 언어, 엔진, 저장소 구조에 종속되지 않는 범용 프로젝트 허브를 목표로 한다.

최종적으로 다음을 지원해야 한다.

```text
임의의 개발 프로젝트 등록/연결
  -> 소스/설정/문서는 Git/GitHub 상태 추적
  -> Git에 적합하지 않은 대용량 데이터는 NAS/별도 저장소 관리
  -> 파일 변경 감지
  -> hash/manifest/version 기반 버전 식별
  -> 프로젝트가 사용하는 활성 대용량 데이터 버전 추적
```

Unity/Unreal, Firmware, .NET, Python, 일반 데이터 프로젝트 등 종류와 관계없이 연결할 수 있는 구조를 유지한다.

대용량 데이터 자체를 Supabase에 저장하는 방향이 아니라, Supabase에는 프로젝트/버전/위치/hash 등의 메타데이터를 저장하고 실제 payload는 NAS 또는 적합한 대용량 저장소가 담당하는 방향을 우선한다.

### 2. 서버 PC에 연결된 모든 프로젝트를 Web ChatGPT가 조회·분석·피드백

최종적으로 Web ChatGPT가 ProjectHub를 통해 다음을 직접 확인할 수 있어야 한다.

```text
- 연결된 전체 프로젝트 목록
- 프로젝트별 workstation
- branch / HEAD
- dirty 상태
- changed / untracked / deleted
- 최근 작업 활동
- PC별 상태 차이
- 동시작업/충돌 가능성
- 대용량 데이터의 현재 버전
- CurrentWork / task 등 관리 문서
```

그리고 단순 조회를 넘어 현재 실제 상태를 기반으로 다음 작업, 위험 요소, 충돌 가능성, 재개 지점을 피드백할 수 있어야 한다.

MCP/Connector는 이 목적을 위한 최종 연결 수단이지 ProjectHub 자체의 목적이 아니다.

---

## 최신 GitHub 상태 확인

최신 확인 커밋:

```text
ddcf6b13d6ea28d28e1267eec0733176f3cf54f0
Record project state E2E validation
```

04 Supabase 스키마/저장 계층은 실제 E2E까지 완료된 상태로 확인한다.

확인 완료:

```text
[x] workstation heartbeat 저장/조회
[x] 동일 workstation upsert
[x] last_seen 갱신
[x] projects + project_states 최소 스키마
[x] project state POST/GET
[x] 동일 project/workstation update
[x] 실제 head_sha 저장
```

현재 구현 로드맵의 다음 작업은:

```text
05 Agent heartbeat와 상태수집
└─ A. Agent 설정과 heartbeat 전송
```

이다.

---

## 문서 불일치 정리 필요

`CurrentWork.md`의 하단 `다음 작업`은 05-A로 올바르게 변경됐지만, 상단 Baseline에는 아직 다음과 같이 남아 있다.

```text
활성 작업: tasks/04_supabase-schema.md
```

05-A 작업을 시작할 때 `tasks/05-agent-state.md`로 수정한다.

또한 `CurrentWork.md`의 일부 초기 설명에는 Supabase 연결/실제 호출이 아직 없다는 과거 문장이 남아 있다. 완료된 실제 E2E 상태와 모순되는 오래된 설명은 이번 문서 갱신 시 최소한으로 정리한다.

정책/공개 계약을 바꾸는 작업이 아니라 현재 상태 문서의 stale 정보 정리로 취급한다.

---

## 다음 작업: 05-A Agent 설정과 heartbeat 전송

현재 `src/ProjectHub.Agent/Program.cs`는 기본 `Hello, World!` 상태다.

05-A에서는 Agent 전체 기능을 한꺼번에 만들지 않는다.

이번 단계의 유일한 목표는:

```text
ProjectHub.Agent
  -> 설정 로드
  -> 주기적으로 heartbeat 생성
  -> ProjectHub.Server POST /api/agent/heartbeat
  -> 장애 시 프로세스 유지
  -> 서버 복구 후 자동 재전송
```

이다.

### 권장 최소 설정

```text
ServerBaseUrl
WorkstationId
DisplayName
HeartbeatIntervalSeconds
```

`hostname`은 가능하면 `Environment.MachineName`으로 자동 수집한다.

Server URL, workstation ID 등을 코드에 하드코딩하지 않는다.

첫 구현은 일반 Console/Worker 실행 형태를 우선한다. Windows Service 등록은 실제 heartbeat E2E가 안정화된 이후 별도 작업으로 미룬다.

---

## heartbeat 실행 정책

초기 검증 간격은 15초 정도를 권장하되 설정값으로 변경 가능하게 한다.

heartbeat payload는 이미 검증된 Server 계약을 그대로 사용한다.

```json
{
  "workstationId": "...",
  "displayName": "...",
  "hostname": "..."
}
```

성공 시:

```text
- 2xx 확인
- 과도한 응답 본문 출력 없이 간단한 상태 로그
```

실패 시:

```text
- Agent 종료 금지
- HTTP status / 핵심 오류만 기록
- 다음 heartbeat 주기에 재시도
```

05-A에서는 Polly 등 복잡한 retry/backoff 의존성을 추가할 필요가 없다. 주기 loop 자체가 재시도 역할을 하면 충분하다.

CancellationToken을 존중하여 Ctrl+C 또는 정상 종료 시 깨끗하게 종료될 수 있게 한다.

---

## 중요한 설계 경계

Agent는 ProjectHub.Server만 바라본다.

```text
Agent -> ProjectHub.Server -> Supabase
```

Agent에 Supabase URL/Secret/Service Role Key를 배포하지 않는다.

또한 Agent는 Cloudflare, Tailscale 등의 특정 네트워크 구현에 종속되지 않아야 한다.

```text
Agent -> configured ServerBaseUrl
```

까지만 책임지고 실제 네트워크 경로는 운영 계층으로 분리한다.

이 경계는 향후 어떤 프로젝트든 연결하는 범용 Agent로 확장하기 위해 중요하다.

---

## 05-A 완료 기준

다음이 모두 실제 확인되면 05-A 완료로 처리한다.

```text
[ ] ProjectHub.Agent가 Hello World 상태에서 실제 실행 구조로 변경
[ ] 설정 기반 ServerBaseUrl 로드
[ ] WorkstationId / DisplayName 설정 가능
[ ] hostname 자동 수집
[ ] 설정 가능한 주기의 heartbeat POST
[ ] Server가 heartbeat 2xx 반환
[ ] Supabase workstations.last_seen이 주기적으로 갱신
[ ] Server 일시 중단 시 Agent가 종료되지 않음
[ ] Server 재기동 후 Agent가 자동으로 heartbeat 재개
[ ] dotnet build 성공
[ ] 관련 테스트 성공
```

build/test 성공과 실제 Mini PC/Supabase E2E 성공을 반드시 구분해서 기록한다.

---

## 05-A에서 하지 말 것

이번 세부 작업에는 아래를 포함하지 않는다.

```text
- Git branch/HEAD 수집
- git status 실행
- dirty 계산
- changed/untracked/deleted 집계
- diff fingerprint
- FileSystemWatcher
- debounce
- project state 자동 전송
- active lease
- 멀티 PC 충돌 판단
- Windows Service 설치
- 자동 updater
- NAS Gateway
- 대용량 데이터 동기화
- MCP endpoint
- 자동 commit/push/reset/merge
- 원격 shell
```

특히 장기 목표에 NAS와 MCP가 포함되어 있더라도 05-A에 조기 구현하지 않는다. 현재 v0.1 경계를 유지하되 향후 확장을 막는 하드코딩만 피한다.

---

## 05-B / 05-C로의 확장 방향

05-A 실제 E2E 성공 후에만 다음으로 진행한다.

### 05-B Git 상태 수집

```text
registered project
  -> branch
  -> HEAD SHA
  -> dirty
  -> changed/untracked/deleted
  -> ProjectState 생성
```

Git 상태 수집 계층은 특정 프로젝트 종류와 분리된 범용 Git adapter로 유지하는 것이 좋다.

### 05-C FileSystemWatcher + debounce

```text
file activity
  -> debounce
  -> Git 상태 재계산
  -> ProjectHub.Server project state 전송
```

heartbeat와 project-state 전송은 역할을 분리한다.

```text
heartbeat = PC/Agent 생존 상태
project state = 프로젝트/Git 작업 상태
```

이 구분은 이후 멀티 PC 동시작업 판정과 Web ChatGPT 상태 분석에 직접 사용된다.

---

## 장기 목표를 위한 구조적 주의사항

현재 05 단계에서 NAS/MCP를 구현하지는 않지만 아래 확장 가능성은 유지한다.

### 범용 프로젝트 등록

향후 Agent가 특정 repo 하나에 고정되지 않고 여러 project registration을 읽을 수 있는 형태로 확장 가능해야 한다.

예상 개념:

```text
Agent
  ├─ Workstation identity
  └─ Registered projects[]
       ├─ projectId
       ├─ localPath
       ├─ adapter/type (optional)
       └─ large-data policy (future)
```

05-A에서 이 전체 모델을 구현할 필요는 없지만 구조를 한 프로젝트 하드코딩으로 닫지 않는다.

### 대용량 데이터 버전관리

향후 Git 상태와 별도로 다음 계층을 추가할 수 있어야 한다.

```text
LargeDataAdapter
  -> file inventory
  -> hash
  -> manifest
  -> NAS object/version
```

현재 Agent heartbeat 코드가 이 기능과 결합되지 않도록 한다.

### Web ChatGPT 연결

06/07에서 조회 가능한 ProjectHub 상태 모델이 만들어진 뒤 08 이후 MCP/지원 Connector를 통해 Web ChatGPT에 노출하는 것이 자연스럽다.

Web ChatGPT가 읽어야 할 것은 개별 DB row가 아니라 ProjectHub가 정리한 프로젝트 상태 API/Resource여야 한다.

---

## Codex 수행 지침

1. `AGENTS.md` → 구현 계획 → `CurrentWork.md` → `tasks/05-agent-state.md` → 이 파일 순으로 확인한다.
2. 04는 실제 E2E까지 완료된 상태로 취급한다.
3. `CurrentWork.md`의 stale 활성 task/과거 설명을 05-A 시작에 맞게 최소 정리한다.
4. 이번 작업은 오직 05-A 하나만 수행한다.
5. `ProjectHub.Agent`를 설정 기반 주기 heartbeat client로 만든다.
6. Agent가 Supabase에 직접 접근하지 않게 한다.
7. 네트워크 구현 방식에 Agent가 의존하지 않게 한다.
8. Server 장애로 Agent 프로세스가 종료되지 않게 한다.
9. Git 상태 수집/FileSystemWatcher/project state 자동전송은 아직 구현하지 않는다.
10. 향후 범용 프로젝트 등록, NAS 대용량 버전관리, Web ChatGPT 연결을 막는 하드코딩은 피한다.
11. 변경 규모에 맞는 build/test를 실행하고 실제 결과만 기록한다.
12. 실제 heartbeat E2E는 사용자가 Mini PC/Supabase에서 확인하기 전까지 완료로 단정하지 않는다.
13. 비밀키/토큰/민감 URL을 저장소에 기록하지 않는다.
14. 사용자 승인 없이 commit/push/deploy/외부 시스템 변경을 수행하지 않는다.

---

## 현재 권장 다음 행동

Codex 작업:

```text
05-A 시작
  -> CurrentWork stale 항목 최소 정리
  -> Agent 설정 모델
  -> HTTP heartbeat client
  -> 주기 실행 loop
  -> 장애 시 계속 실행
  -> build/test
```

그 후 사용자 실제 검증:

```text
DEV PC에서 ProjectHub.Agent 실행
  -> Mini PC ProjectHub.Server에 heartbeat 도착
  -> Supabase last_seen 반복 갱신 확인
  -> Server 중단
  -> Agent 생존 확인
  -> Server 재실행
  -> heartbeat 자동 복구 확인
```

이 검증이 성공한 뒤에만 05-B Git 상태 수집으로 넘어간다.
