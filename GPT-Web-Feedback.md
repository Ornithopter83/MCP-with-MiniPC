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

최신 구현 커밋:

```text
348786ce620049b8e7bf6c1c4bc98726d9f94a2a
05-B 완료
```

현재 확인된 진행 상태:

```text
[x] 04 Supabase 저장/조회 실제 E2E 완료
[x] 고정 외부접속 projecthub.ornithopter.bid 검증 완료
[x] 05-A Agent heartbeat 구현 완료
[x] 05-A 외부 DEV PC -> Mini PC -> Supabase 실제 E2E 완료
[x] 장애 중 Agent 생존/재시도 확인
[x] 서버/경로 복구 후 Agent 자동 heartbeat 재개 확인
[x] 05-B GitStateCollector 구현 완료
[x] branch 수집
[x] HEAD full SHA 수집
[x] dirty 판정
[x] changed / untracked / deleted 집계
[x] non-repository/명령 실패 진단 오류 처리
[x] 읽기 전용 Git 경계 유지
[x] ProjectHub.Agent.Tests 추가
[x] 관련 build/test 통과 기록
[ ] 실제 DEV PC의 real Git repository 대상으로 CollectAsync 결과 1회 확인
[ ] 05-C FileSystemWatcher/debounce + 자동 state 전송
```

문서상 현재 다음 작업은 **05-C FileSystemWatcher debounce와 상태 전송**으로 넘어간 상태다.

---

## 05-B 검토 결과

05-B는 구현 완료로 판단한다.

`GitStateCollector`는 등록된 `localPath`를 기준으로 다음 읽기 전용 Git 명령을 사용한다.

```text
git rev-parse --abbrev-ref HEAD
git rev-parse HEAD
git status --porcelain=v1 --untracked-files=all
```

수집 결과는 다음 ProjectState 요소로 연결된다.

```text
projectId
workstationId
branch
headSha
dirty
changedCount
untrackedCount
deletedCount
lastSeen
```

현재 단계에서 `diffFingerprint`, `lastFileActivity`가 비어 있어도 05-B 완료를 막는 문제로 보지 않는다. 이 값들은 05-C의 파일 활동/debounce 또는 이후 상태 확장에서 채울 수 있다.

Git mutation은 포함되지 않았고, FileSystemWatcher/자동 project state 전송도 아직 넣지 않아 작업 경계가 잘 지켜졌다.

---

## 05-B 테스트 검토

현재 Agent 테스트에는 최소한 다음이 포함되어 있다.

```text
- tracked changed + deleted + untracked 집계
- clean status 파싱
- Git repository가 아닌 경로의 진단 오류
```

따라서 파서와 기본 실패 경계는 검증됐다.

다만 현재 확인한 테스트는 실제 임시 Git repository를 만들고 `CollectAsync()` 전체 경로를 성공 케이스로 검증하는 통합 테스트까지는 포함하지 않는다.

즉 현재 상태는 다음과 같이 구분한다.

```text
05-B 코드 구현          완료
05-B 단위/기본 테스트   완료
05-B 실제 repo 검증     1회 권장
```

이 real repo 검증은 05-B를 다시 미완료로 되돌릴 정도의 차단 항목은 아니다. 05-C 시작 직전 또는 05-C 초반에 매우 짧게 수행하면 된다.

---

## 05-C 시작 전 권장 real repo 1회 검증

05-C에서 문제가 생겼을 때 Git 수집 문제와 FileSystemWatcher 문제를 분리하기 위해, 실제 DEV PC의 Git repository 하나를 대상으로 `GitStateCollector.CollectAsync()` 결과를 한 번 확인한다.

검증 대상 예:

```text
현재 ProjectHub repo 또는 사용자가 등록할 실제 개발 repo
```

확인할 값:

```text
branch          = 실제 현재 branch와 일치
headSha         = git rev-parse HEAD와 일치
dirty           = git status 결과와 일치
changedCount    = tracked 변경 수와 일치
untrackedCount  = untracked 수와 일치
deletedCount    = deleted 수와 일치
```

가능하면 다음 세 상태 중 2개 이상만 확인하면 충분하다.

```text
1. clean repo
2. tracked file 1개 수정
3. untracked file 1개 추가
4. tracked file 1개 삭제
```

검증 때문에 실제 사용자 파일을 임의 수정하지 않는다. 안전한 테스트 repo를 만들거나 사용자가 승인한 상태에서만 수행한다.

---

## 다음 작업: 05-C FileSystemWatcher debounce와 상태 전송

05-C의 목표는 파일 변경 활동을 감지하고, 이벤트 폭주를 debounce한 뒤, 이미 구현된 `GitStateCollector`를 호출하여 ProjectHub.Server에 project state를 전송하는 것이다.

권장 흐름:

```text
registered project
  -> FileSystemWatcher
  -> 파일 활동 감지
  -> debounce
  -> GitStateCollector.CollectAsync()
  -> ProjectState payload
  -> ProjectHub.Server project-state API
  -> Supabase project_states upsert
```

핵심 원칙:

```text
heartbeat = workstation/Agent 생존 상태
project state = 특정 프로젝트의 Git 작업 상태
file activity = project state 재계산을 유발하는 trigger
```

세 역할을 섞지 않는다.

---

## 05-C 권장 구현 범위

이번 단계에서 필요한 최소 범위는 다음이다.

```text
- 등록 프로젝트 localPath 감시
- 파일 생성/수정/삭제/이름변경 이벤트 수신
- 짧은 debounce 적용
- debounce 종료 후 GitStateCollector 호출
- 수집된 ProjectState를 기존 Server API로 전송
- 전송 실패 시 Agent 전체 종료 금지
- 다음 파일 활동 또는 재시도 기회에서 복구 가능
```

권장 debounce 기준은 처음에는 약 1~2초 수준의 단순 구조면 충분하다. 복잡한 queue/message broker는 필요 없다.

FileSystemWatcher 이벤트 하나당 즉시 `git status`를 실행하지 않는다. IDE 저장, 빌드, Git 작업에서는 짧은 시간에 많은 이벤트가 발생할 수 있으므로 반드시 합쳐서 처리한다.

---

## 05-C에서 반드시 고려할 제외/노이즈

모든 파일 이벤트를 의미 있는 사용자 작업으로 해석하면 안 된다.

최소한 아래 경로는 watcher 이벤트가 많이 발생할 수 있으므로 설계 시 주의한다.

```text
.git
bin
obj
Library (Unity)
Temp
Logs
node_modules
기타 빌드/캐시 디렉터리
```

다만 프로젝트 종류별 제외 목록을 05-C에서 거대한 하드코딩 목록으로 만들지는 않는다.

우선 원칙은:

```text
FileSystemWatcher = 재계산 trigger
GitStateCollector = 실제 source 상태 판단
```

즉 watcher 자체가 changed/untracked/deleted를 직접 계산하지 않는다.

Git 기준 프로젝트에서는 최종 상태 판단을 `git status`에 맡긴다.

---

## 범용 프로젝트 목표와 05-C 구조

현재는 Git 프로젝트를 먼저 다루지만 장기적으로 ProjectHub는 Git 프로젝트만 지원하는 제품이 아니다.

따라서 watcher 계층은 향후 다음 구조로 확장 가능해야 한다.

```text
ProjectRegistration
  ├─ projectId
  ├─ localPath
  ├─ source adapter (Git 등)
  └─ large-data policy (future)

FileActivityMonitor
  -> source adapter 재계산 trigger
  -> future LargeDataAdapter trigger
```

05-C에서 NAS 대용량 버전관리를 구현하지는 않지만 FileSystemWatcher를 Git 전용 클래스 내부에 강하게 결합해서 향후 확장을 막지는 않는다.

---

## 05-C 완료 기준

다음이 실제 확인되면 05-C 완료 후보로 본다.

```text
[ ] project localPath 파일 변경 감지
[ ] 짧은 이벤트 폭주가 debounce되어 Git 상태 재계산 1회 수준으로 수렴
[ ] GitStateCollector 결과가 실제 Git 상태와 일치
[ ] ProjectState가 Server API로 전송됨
[ ] Supabase project_states가 자동 갱신됨
[ ] untracked/modified/deleted 변화가 자동 반영됨
[ ] Agent heartbeat는 독립적으로 계속 동작
[ ] Server/네트워크 장애 시 Agent 전체가 죽지 않음
[ ] 복구 후 이후 상태 전송이 다시 성공
[ ] Git mutation 없음
[ ] build/test 성공
```

가능하면 05-C 완료 시 사용자 실제 DEV PC에서 다음 E2E를 한 번 수행한다.

```text
파일 수정
  -> Agent 감지
  -> debounce
  -> Git 상태 수집
  -> HTTPS
  -> projecthub.ornithopter.bid
  -> ProjectHub.Server
  -> Supabase project_states 갱신
```

이 흐름이 성공하면 이후 06 Project 상태 API 단계에서 Web ChatGPT가 사용할 수 있는 실제 실시간 상태 기반이 갖춰진다.

---

## 아직 하지 말 것

05-C에서 다음은 구현하지 않는다.

```text
- active lease 최종 판정
- 멀티 PC 충돌 판정
- NAS 대용량 파일 복사/버전 생성
- MCP endpoint
- Web ChatGPT용 최종 Resource 설계
- 자동 Git commit/push/pull/fetch/reset/merge
- 원격 shell
- 복잡한 message queue
- Docker/Redis 추가
```

---

## 운영/보안 상태 유지

고정 외부 endpoint는 계속 다음을 사용한다.

```text
https://projecthub.ornithopter.bid
```

Agent/Server 코드에 Cloudflare provider 종속 로직이나 도메인을 하드코딩하지 않는다. `ServerBaseUrl` 설정 경계를 유지한다.

현재 write API의 장기 무인증 공개 상태는 최종 운영형으로 보지 않는다. 다만 05-C 기능 E2E를 먼저 완성한 후 인증 계층을 별도 명확한 작업으로 추가할 수 있다.

비밀값, tunnel token, service token, Agent API key는 저장소·문서·로그에 기록하지 않는다.

---

## 문서 갱신 요구

Codex는 작업 전 최신 상태를 관리 문서에 맞춘다.

최소 반영:

```text
05-A = 완료
05-B = 완료
현재 작업 = 05-C
05-B GitStateCollector 구현/테스트 완료
05-B actual real-repo check는 05-C 시작 전 1회 권장 검증으로 기록
```

`CurrentWork.md`, `tasks/05-agent-state.md`, `NewThreadHandoff.md`가 서로 다른 다음 작업을 가리키지 않도록 정리한다.

---

## Codex 수행 지침

1. `AGENTS.md` → 구현 계획 → `CurrentWork.md` → `tasks/05-agent-state.md` → 이 파일 순으로 읽는다.
2. 최신 커밋 `348786ce...`의 05-B 구현은 완료 상태로 취급한다.
3. 필요하면 05-C 착수 직전 actual Git repo에서 `GitStateCollector` 성공 경로를 1회 검증한다.
4. 이 검증은 안전한 repo/상태에서 읽기 전용으로만 수행한다.
5. 다음 구현은 오직 05-C FileSystemWatcher debounce와 project state 자동 전송이다.
6. watcher 이벤트마다 즉시 git 명령을 실행하지 말고 debounce한다.
7. watcher는 trigger 역할만 하며 Git 상태 판단은 `GitStateCollector`가 담당한다.
8. heartbeat와 project state 전송을 독립적으로 유지한다.
9. Agent/Server 장애 때문에 프로세스 전체가 종료되지 않게 한다.
10. 특정 프로젝트 타입 하나에 종속된 구조를 피하고 향후 범용 project adapter / LargeDataAdapter 확장을 막지 않는다.
11. FileSystemWatcher 단계에서 NAS/MCP/멀티PC 판정까지 범위를 확장하지 않는다.
12. 변경 규모에 맞는 build/test를 수행하고 실제 결과만 기록한다.
13. 사용자 승인 없이 외부 서비스 변경, 배포, Git mutation을 수행하지 않는다.
