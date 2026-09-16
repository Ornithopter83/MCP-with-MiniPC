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

이 파일은 기존 관리 파일을 대체하지 않는다. 충돌 시 기존 정책과 활성 task를 우선한다.

---

## 최종 목적 — 반드시 유지

ProjectHub의 최종 목표는 두 가지다.

1. 어떤 종류의 프로젝트도 연결할 수 있고, 소스뿐 아니라 Git에 적합하지 않은 대용량 데이터도 NAS/별도 저장소와 연계하여 자동 버전관리할 수 있어야 한다.
2. 서버 PC에 연결된 모든 프로젝트의 현재 상태를 Web ChatGPT가 조회·분석하고, 다음 작업·위험·충돌·재개 지점을 피드백할 수 있어야 한다.

MCP/Connector는 이 목표를 위한 연결 수단이며 ProjectHub 자체의 목적은 아니다.

---

## 최신 확인 상태

05-A Agent heartbeat 구현은 이미 완료됐고, 이번 사용자 실환경 테스트로 실제 원격 E2E까지 검증됐다.

기존 구현 기준 커밋:

```text
f57504b0a75b75853088b7cfa4037c4a955f1ad1
5-A
```

이번 사용자 실환경 검증에서 다음을 확인했다.

```text
[x] 외부 DEV PC에서 ProjectHub.Agent 실행
[x] ServerBaseUrl = https://projecthub.ornithopter.bid 사용
[x] 15초 주기 heartbeat 실행
[x] heartbeat 성공 로그 확인
[x] Mini PC ProjectHub.Server에서 Supabase workstation POST 처리 확인
[x] Supabase에 DEV-PC-01 row 생성 확인
[x] DEV-PC-01 hostname = DESKTOP-MS05QSG 확인
[x] workstations.last_seen 반복 갱신 확인
[x] 네트워크/서버 장애 중 timeout/502 발생 확인
[x] 장애 중에도 Agent 프로세스가 종료되지 않고 재시도 지속
[x] Server/경로 복구 후 Agent 재시작 없이 heartbeat 자동 성공 복귀
```

따라서 05-A는 **코드 구현 + build/test + 실제 원격 E2E까지 완료**된 것으로 처리한다.

---

## 실제 검증에서 관찰된 장애/복구 동작

Agent 실행 로그에서 다음 흐름이 실제 확인됐다.

```text
ProjectHub.Agent started. Heartbeat interval: 15s
Heartbeat sent successfully.
Heartbeat failed; will retry: ... HttpClient.Timeout of 10 seconds ...
Heartbeat sent successfully.
Heartbeat sent successfully.
```

이후 서버/네트워크 경로가 중단된 구간에서는:

```text
Heartbeat failed; will retry: An error occurred while sending the request.
Heartbeat failed with 502 Bad Gateway
Heartbeat failed with 502 Bad Gateway
```

가 반복됐지만 Agent 프로세스는 살아 있었다.

복구 후에는 같은 Agent 프로세스에서:

```text
Heartbeat sent successfully.
```

으로 자동 복귀했다.

이 결과는 05-A의 핵심 요구사항인 다음을 실제 검증한 것이다.

```text
정상 상태
  -> 반복 heartbeat 성공

일시 장애
  -> timeout / 502
  -> Agent 생존
  -> 다음 주기 재시도

복구
  -> Agent 재시작 없이 heartbeat 자동 재개
```

단발성 timeout 자체는 현재 05-A 완료를 막는 결함으로 취급하지 않는다. 오히려 재시도/복구 동작이 실제 환경에서 작동함을 확인한 사례다.

---

## 고정 외부접속 경로도 검증 완료 상태 유지

이미 다음 고정 외부 경로가 실제 검증됐다.

```text
외부 DEV PC
  -> https://projecthub.ornithopter.bid
  -> Cloudflare Named Tunnel `projecthub`
  -> Mini PC
  -> http://localhost:5240
  -> ProjectHub.Server
  -> Supabase
```

외부 브라우저에서 다음도 성공했다.

```text
GET https://projecthub.ornithopter.bid/api/status
```

응답 의미:

```text
server   = ProjectHub
status   = ok
database = supabase
```

Quick Tunnel은 더 이상 운영 기준으로 사용하지 않는다.

---

## 문서 갱신 요구

Codex는 이번 작업에서 실제 검증 결과를 관리 문서에 반영한다.

최소 갱신 대상:

```text
CurrentWork.md
tasks/05-agent-state.md
NewThreadHandoff.md (존재 시)
ProjectHub_IMPLEMENTATION_PLAN.md (현재 상태/다음 단계가 필요하면 최소 갱신)
```

반영할 핵심 상태:

```text
05-A Agent 설정과 heartbeat 전송 = 완료
- 구현 완료
- build/test 완료
- 외부 DEV PC -> 고정 hostname -> Mini PC Server -> Supabase E2E 완료
- repeated last_seen 갱신 완료
- 장애 중 Agent 생존/재시도 확인
- Server/경로 복구 후 자동 heartbeat 재개 확인
```

`CurrentWork.md`의 다음 작업은 **05-B Git 상태 수집기 구현**으로 변경한다.

---

## 다음 작업: 05-B Git 상태 수집기 구현

이제 heartbeat 통신 기반이 실제 환경에서 검증됐으므로 05-B로 넘어간다.

05-B의 목표는 Agent가 등록된 Git 프로젝트의 현재 상태를 읽어 ProjectState 형태로 만들 수 있게 하는 것이다.

첫 범위는 아래로 제한한다.

```text
registered project/localPath
  -> Git repository 여부 확인
  -> branch
  -> HEAD SHA
  -> dirty
  -> changed count
  -> untracked count
  -> deleted count
  -> 필요 시 diff fingerprint
  -> ProjectState 모델 생성
```

05-B에서는 아직 FileSystemWatcher 기반 자동 감시는 구현하지 않는다.

### 권장 Git 명령 경계

현재 설계 방향을 유지하면 다음과 같은 읽기 전용 Git 호출이면 충분하다.

```text
git rev-parse --abbrev-ref HEAD
git rev-parse HEAD
git status --porcelain
```

필요한 경우 `git status --porcelain` 결과를 한 번 파싱해서 dirty / changed / untracked / deleted를 계산한다.

Agent는 Git을 읽기만 하며 아래 작업은 금지한다.

```text
commit
push
pull
fetch
checkout
reset
merge
rebase
clean
파일 수정/삭제
```

---

## 05-B 설계 시 반드시 지킬 점

### 1. 범용 프로젝트 연결 가능성 유지

특정 저장소 하나를 코드에 하드코딩하지 않는다.

향후 어떤 프로젝트든 등록할 수 있도록 최소한 다음 개념으로 확장 가능해야 한다.

```text
Agent
  └─ RegisteredProjects[]
       ├─ projectId
       ├─ displayName
       ├─ localPath
       └─ repositoryUrl(optional)
```

05-B에서 전체 프로젝트 등록 UI를 만들 필요는 없지만, 한 프로젝트 경로를 소스에 고정하는 구조는 피한다.

### 2. heartbeat와 project state 역할 분리

```text
heartbeat = PC/Agent 생존 상태
project state = 특정 프로젝트의 Git/작업 상태
```

두 기능을 하나의 payload나 하나의 의미로 섞지 않는다.

### 3. 대용량 데이터 기능과 분리

향후 NAS 대용량 버전관리는 별도 adapter/manifest 계층으로 추가할 예정이므로 Git 상태 수집 코드와 결합하지 않는다.

예상 확장:

```text
GitStateCollector
LargeDataAdapter
```

각각 별도 역할로 유지한다.

---

## 05-B 완료 기준

이번 단계에서 다음이 확인되면 05-B 완료 후보로 본다.

```text
[ ] 설정/등록된 localPath에서 Git repo 여부 판정
[ ] branch 수집
[ ] HEAD full SHA 수집
[ ] dirty 판정
[ ] changed/untracked/deleted 집계
[ ] Git 명령 실패 시 Agent 전체 프로세스가 죽지 않음
[ ] Git이 아닌 폴더/없는 경로에 대해 진단 가능한 오류 처리
[ ] 읽기 전용 동작만 수행
[ ] 모의/테스트 Git repository로 결과 검증
[ ] dotnet build 성공
[ ] 관련 테스트 성공
```

가능하면 05-B에서는 실제 서버 전송까지 억지로 포함하지 말고, **Git 상태 수집기 자체의 정확성**을 먼저 검증한다.

project state 자동 전송/파일 감지는 05-C에서 연결한다.

---

## 아직 하지 말 것

05-B에서는 다음을 구현하지 않는다.

```text
- FileSystemWatcher
- debounce
- 파일 변경 시 자동 state 전송
- active lease
- 멀티 PC 충돌 판정
- NAS Gateway
- 대용량 데이터 복사/버전 생성
- MCP endpoint
- 자동 Git mutation
- 원격 shell
```

---

## 운영/보안 주의

현재 `projecthub.ornithopter.bid`는 인터넷에서 접근 가능한 경로다.

05-A 기능 검증은 완료했지만 장기 운영에서는 write API 무인증 노출을 최종 상태로 보지 않는다.

향후 인증 계층은 다음처럼 분리하는 방향을 유지한다.

```text
외부 접근 보호
  -> Cloudflare Access / Service Token 등

ProjectHub Agent 인증
  -> ProjectHub API Key / JWT / device registration 등
```

비밀값, tunnel token, service token, API key는 저장소·문서·로그에 기록하지 않는다.

인증 작업 때문에 이미 확정된 아래 주소 구조를 다시 바꾸지 않는다.

```text
https://projecthub.ornithopter.bid
```

---

## Codex 수행 지침

1. `AGENTS.md` → 구현 계획 → `CurrentWork.md` → `tasks/05-agent-state.md` → 이 파일 순으로 읽는다.
2. 05-A는 코드 구현, build/test, 실제 외부 DEV PC E2E까지 완료된 것으로 처리한다.
3. 이번 사용자 실환경 검증 결과를 관리 문서에 실제 사실대로 반영한다.
4. 05-A 완료 처리 후 다음 작업을 05-B Git 상태 수집기로 변경한다.
5. 05-B는 읽기 전용 Git 상태 수집기 구현에만 집중한다.
6. 특정 프로젝트 경로나 종류를 코드에 하드코딩하지 않는다.
7. heartbeat와 project state 역할을 분리한다.
8. Git mutation은 절대 수행하지 않는다.
9. FileSystemWatcher와 자동 전송은 05-C까지 미룬다.
10. 범용 프로젝트 연결, 향후 NAS 대용량 버전관리, Web ChatGPT 조회 목표를 막는 구조를 만들지 않는다.
11. 변경 규모에 맞는 build/test를 실행하고 실제 결과만 기록한다.
12. 사용자 승인 없이 외부 서비스 변경, 배포, commit/push 등의 작업을 수행하지 않는다.
