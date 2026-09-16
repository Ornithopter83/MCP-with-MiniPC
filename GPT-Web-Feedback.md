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
f57504b0a75b75853088b7cfa4037c4a955f1ad1
5-A
```

확인된 내용:

```text
[x] 04 Supabase 저장 계층 실제 E2E 완료
[x] workstation heartbeat 저장/조회/upsert 검증
[x] projects + project_states 저장/조회 검증
[x] 실제 head_sha 저장 검증
[x] 05-A Agent 구현 완료
[x] Agent 설정 로드 구현
[x] 주기 heartbeat sender/runner 구현
[x] HTTP 장애 시 프로세스 유지 및 다음 주기 재시도 구현
[x] Ctrl+C CancellationToken 종료 처리
[x] build/test 성공 기록
[ ] 05-A 실제 원격 DEV PC -> Mini PC E2E 검증
```

`tasks/05-agent-state.md`에도 05-A는 구현 완료, 실환경 E2E 대기로 기록되어 있다.

---

## 판단: 05-A 전체 E2E보다 고정 외부접속 기반을 먼저 구축

현재 05-A 구현 자체는 완료됐고 build/test도 통과했다. 남은 검증은 실제 다른 네트워크의 DEV PC가 Mini PC ProjectHub.Server에 반복 heartbeat를 보내는 실환경 E2E다.

이 시점에서는 임시 Quick Tunnel로 05-A를 먼저 최종 검증하고 다시 고정 네트워크를 만드는 것보다, **서버의 고정 외부접속 기반을 먼저 구축한 뒤 그 경로로 05-A 실환경 E2E를 수행하는 것이 더 적절하다.**

이유:

```text
- 05-A의 실제 사용 환경 자체가 서로 다른 네트워크의 DEV PC -> Mini PC 구조다.
- Quick Tunnel은 URL이 바뀌므로 Agent의 지속 설정값으로 부적합하다.
- 고정 접근 경로를 먼저 만들면 05-A 검증 결과를 이후 05-B/05-C에서도 그대로 재사용할 수 있다.
- 네트워크 경로를 먼저 고정하면 Agent 코드 문제와 임시 터널 문제를 분리할 수 있다.
- 05-A는 이미 build/test가 통과했으므로 네트워크 구축 전에 동일 기능을 다시 임시 경로에서 완전 검증할 실익이 작다.
```

단, 네트워크 구축 전에도 현재 코드의 build/test 결과는 유지하며, 05-A를 완전 완료로 처리하지는 않는다.

---

## 다음 우선 작업: 상시 외부접속 기반 구축

목표는 다음 상태다.

```text
다른 네트워크의 DEV PC
  -> 고정 HTTPS 주소
  -> 인증/접근 제어
  -> Mini PC
  -> ProjectHub.Server
```

서버 PC가 재부팅되더라도 수동으로 임시 URL을 다시 발급하지 않아야 한다.

### 권장 방식

현재 Quick Tunnel 검증 경험을 이어서 **Cloudflare Named Tunnel + 고정 hostname**을 우선 검토한다.

예상 구조:

```text
DEV PC
  -> https://<fixed-hostname>
  -> Cloudflare Access 또는 동등한 보호
  -> Named Tunnel
  -> Mini PC cloudflared service
  -> http://localhost:5240
  -> ProjectHub.Server
```

중요: Agent 코드는 Cloudflare에 종속되지 않는다. Agent는 오직 설정된 `ServerBaseUrl`만 사용한다.

```text
Agent -> configured ServerBaseUrl
```

네트워크 구현은 운영 계층으로 분리한다.

---

## 상시 외부접속 완료 기준

다음이 실제 확인되면 네트워크 기반 구축 완료로 본다.

```text
[ ] 고정 hostname 또는 고정 외부 endpoint 확보
[ ] Mini PC에서 tunnel/relay 자동 시작
[ ] ProjectHub.Server 자동 또는 명확한 재기동 절차 확보
[ ] 외부 DEV PC에서 GET /api/status 성공
[ ] 외부 DEV PC에서 POST /api/agent/heartbeat 성공
[ ] 서버 PC 재부팅 후 동일 hostname 유지
[ ] 서버 PC 재부팅 후 외부 접근 자동 복구
[ ] 공개 쓰기 API가 무인증으로 장시간 노출되지 않도록 접근 제어 적용
```

가능하면 machine-to-machine 인증을 사용하고, 이후 ProjectHub 자체 Agent API Key를 별도 계층으로 추가할 수 있게 한다.

비밀값, tunnel token, service token, API key는 저장소에 기록하지 않는다.

---

## 05-A 최종 E2E는 네트워크 구축 직후 수행

고정 외부접속이 준비되면 별도 새 기능을 더 구현하기 전에 05-A를 실제 검증한다.

검증 순서:

```text
1. 외부 DEV PC에 ProjectHub.Agent 설정
2. ServerBaseUrl = 고정 외부 주소
3. Agent 실행
4. 반복 heartbeat 2xx 확인
5. Supabase workstations.last_seen 반복 갱신 확인
6. ProjectHub.Server 일시 중단
7. Agent가 종료되지 않고 실패 로그 후 계속 대기하는지 확인
8. Server 재기동
9. Agent가 별도 재시작 없이 heartbeat를 자동 재개하는지 확인
```

이 검증이 끝난 뒤에만 05-A를 완료 처리하고 05-B Git 상태 수집기로 넘어간다.

즉 현재 순서는 다음과 같다.

```text
05-A 코드 구현 완료
  -> 고정 외부접속 기반 구축
  -> 05-A 실환경 E2E
  -> 05-A 완료
  -> 05-B Git 상태 수집
```

05-B를 먼저 구현해서 검증을 한꺼번에 몰아서 하지 않는다. heartbeat 통신 기반이 안정적이라는 사실을 먼저 확정해야 이후 Git 상태 전송 문제를 분리해서 진단할 수 있다.

---

## 서버 PC 작업과 Codex 작업을 구분

### 사용자/서버 PC 측

```text
- 고정 외부 endpoint/tunnel 구성
- tunnel/relay 자동 시작 설정
- 외부 DEV PC 접근 확인
- 필요 시 Cloudflare Access 등 접근 제어 설정
- Mini PC 재부팅 후 자동 복구 확인
```

이 작업은 외부 서비스 설정을 포함할 수 있으므로 사용자 승인 없이 Codex가 자동 수행하지 않는다.

### Codex 측

현재는 새 기능 구현보다 아래만 준비/정리한다.

```text
- 05-A 구현 상태 유지
- 문서에서 05-A = 구현 완료 / 실환경 E2E 대기로 명확히 기록
- ServerBaseUrl이 고정 URL로 교체 가능하도록 현재 설정 구조 유지
- 네트워크 제공자 종속 코드 추가 금지
- 필요 시 Agent 인증 헤더를 향후 추가할 수 있는 구조만 방해하지 않도록 유지
```

고정 외부접속이 검증되기 전에는 05-B 구현을 시작하지 않는다.

---

## 아직 하지 말 것

```text
- 05-B Git 상태 수집
- 05-C FileSystemWatcher/debounce
- active lease
- 멀티 PC 충돌 판정
- NAS Gateway
- 대용량 데이터 동기화
- MCP endpoint
- 자동 Git commit/push/reset/merge
- 원격 shell
```

현재 최우선은 **서버가 켜져 있으면 다른 네트워크의 개발 PC가 항상 같은 주소로 안전하게 접근할 수 있는 기반을 만드는 것**이다.

---

## Codex 수행 지침

1. `AGENTS.md` → 구현 계획 → `CurrentWork.md` → `tasks/05-agent-state.md` → 이 파일 순으로 읽는다.
2. 최신 커밋 `f57504b...`의 05-A 구현을 이미 완료된 코드로 취급한다.
3. 05-A는 build/test 성공이지만 실환경 E2E는 아직 미완료다.
4. 05-B를 시작하지 않는다.
5. 우선 고정 외부접속 기반 구축이 필요하다는 상태를 관리 문서에 반영한다.
6. 네트워크 제공자 종속 로직을 Agent 코드에 넣지 않는다.
7. 사용자가 고정 외부접속을 구성한 뒤 같은 경로로 05-A E2E를 검증한다.
8. 05-A E2E 성공 후에만 05-B로 진행한다.
9. 비밀키/토큰/민감 URL을 저장소·문서·로그에 기록하지 않는다.
10. 사용자 승인 없이 외부 서비스 설정, 배포, commit/push 등 파괴적/외부 변경을 수행하지 않는다.
