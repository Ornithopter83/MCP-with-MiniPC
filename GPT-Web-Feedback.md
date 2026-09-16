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

## 최신 구현 상태

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
[x] 05-A Agent 코드 구현 완료
[x] 설정 기반 ServerBaseUrl / WorkstationId / DisplayName / heartbeat interval 구현
[x] 주기 heartbeat sender/runner 구현
[x] HTTP 장애 시 프로세스 유지 및 다음 주기 재시도 구현
[x] Ctrl+C CancellationToken 종료 처리
[x] build/test 성공 기록
[ ] 05-A 실제 외부 DEV PC Agent E2E 검증
```

`tasks/05-agent-state.md`에도 05-A는 구현 완료, 실환경 E2E 대기로 기록되어 있다.

---

## 사용자 측 고정 외부접속 구축 및 실제 검증 완료

기존 Quick Tunnel은 더 이상 운영 기준으로 사용하지 않는다.

사용자가 Cloudflare에서 독립 도메인 `ornithopter.bid`를 구매했고, Cloudflare DNS가 활성 상태임을 확인했다.

이후 다음 고정 경로를 실제 구성했다.

```text
Cloudflare Named Tunnel: projecthub
Published application: projecthub.ornithopter.bid
Origin service: http://localhost:5240
```

그리고 다른 네트워크의 브라우저에서 다음 고정 주소를 실제 호출했다.

```text
https://projecthub.ornithopter.bid/api/status
```

실제 응답에서 다음을 확인했다.

```text
server   = ProjectHub
status   = ok
database = supabase
```

따라서 아래 경로는 실제 검증 완료로 취급한다.

```text
외부 네트워크
  -> HTTPS
  -> projecthub.ornithopter.bid
  -> Cloudflare Named Tunnel
  -> Mini PC
  -> http://localhost:5240
  -> ProjectHub.Server
```

즉 고정 hostname 기반 외부 GET 접근 자체는 더 이상 미검증 항목이 아니다.

---

## 이번 작업에서 서버 실행 설정까지 안정화할 것

05-A Agent 실환경 E2E를 수행하기 전에, 이후 05-B/05-C 및 장기 운영에서 서버 포트/바인딩을 다시 손볼 필요가 없도록 **현재 05-A 마무리 범위에서 서버 실행 설정을 한 번 정리한다.**

단, Cloudflare 종속 코드를 Server에 넣는다는 의미가 아니다.

목표는 아래와 같다.

```text
외부 주소: https://projecthub.ornithopter.bid
Cloudflare origin: http://localhost:5240
ProjectHub.Server local endpoint: localhost/127.0.0.1:5240
```

### Codex가 확인/보완할 서버 설정

1. ProjectHub.Server가 개발용 `launchSettings.json`에만 의존해서 5240을 얻는 구조인지 확인한다.
2. 실제 Mini PC 실행에서도 `localhost:5240`을 안정적으로 유지할 수 있도록 운영 설정 경계를 명확히 한다.
3. 포트/바인딩 변경이 필요할 경우 코드 수정이 아니라 표준 ASP.NET Core 설정/환경 변수로 바꿀 수 있게 한다.
4. Cloudflare Tunnel이 같은 origin을 계속 바라볼 수 있도록 기본 운영 endpoint를 `http://127.0.0.1:5240` 또는 동등한 localhost 바인딩으로 유지한다.
5. 외부 공개를 위해 Kestrel을 `0.0.0.0`에 직접 노출하거나 공유기 포트포워딩을 추가하지 않는다.
6. 외부 TLS는 Cloudflare가 담당하므로 현재 구조에서 Mini PC origin에 별도 공인 HTTPS 인증서를 강제하지 않는다.
7. `/api/status`, `/api/agent/heartbeat`, project-state API 계약은 변경하지 않는다.
8. 서버 실행 방식이 달라져도 Cloudflare origin URL을 다시 수정할 필요가 없도록 한다.

가능하면 운영 설정은 기존 ASP.NET Core 표준인 `ASPNETCORE_URLS` 또는 동등한 설정 경계를 활용하고, 소스에 도메인/서버 PC 전용 값을 하드코딩하지 않는다.

### 불필요한 변경 금지

```text
- Cloudflare SDK/라이브러리를 Server 코드에 추가하지 않는다.
- projecthub.ornithopter.bid를 Server 코드에 하드코딩하지 않는다.
- CORS를 이유 없이 추가하지 않는다. 현재 Agent는 브라우저가 아닌 HTTP client다.
- 외부 접속 때문에 Server를 인터넷에 직접 bind하지 않는다.
- Quick Tunnel 지원 코드를 추가하지 않는다.
```

Cloudflare는 운영 계층이고 ProjectHub.Server는 로컬 HTTP origin 역할만 유지한다.

---

## 문서 상태도 이번 작업에서 실제 검증 결과로 갱신

Codex는 코드 확인/보완과 함께 관리 문서에 다음 실제 상태를 반영한다.

```text
[x] 독립 도메인 ornithopter.bid 확보
[x] Cloudflare Named Tunnel `projecthub` 구성
[x] 고정 Published Application 구성
[x] projecthub.ornithopter.bid -> http://localhost:5240 연결
[x] 외부 네트워크에서 /api/status 실제 성공
[x] 고정 hostname 사용 가능 확인
[ ] 외부 DEV PC에서 ProjectHub.Agent heartbeat E2E
[ ] 반복 last_seen 갱신 확인
[ ] Server 중단 중 Agent 생존 확인
[ ] Server 복구 후 Agent 자동 heartbeat 재개 확인
[ ] 서버 PC 재부팅 후 tunnel/Server 자동 복구 확인
```

`CurrentWork.md`, `tasks/05-agent-state.md`, 필요 시 `NewThreadHandoff.md`에는 위 상태를 실제 사실대로 갱신한다.

중요: `GET /api/status` 외부 성공과 05-A Agent E2E 성공은 구분해서 기록한다. 아직 Agent E2E를 완료로 표시하지 않는다.

---

## 다음 검증: 05-A 실제 외부 Agent E2E

서버 설정 경계를 확인/안정화한 뒤 새 기능을 더 구현하지 말고 바로 05-A 실제 E2E를 수행한다.

외부 DEV PC의 Agent 설정은 다음 고정 주소를 사용한다.

```text
ServerBaseUrl = https://projecthub.ornithopter.bid
```

검증 순서:

```text
1. DEV PC에서 ProjectHub.Agent 실행
2. heartbeat 2xx 반복 성공 확인
3. Supabase workstations.last_seen이 주기적으로 갱신되는지 확인
4. Mini PC ProjectHub.Server 일시 중단
5. Agent가 종료되지 않고 실패 로그 후 계속 살아 있는지 확인
6. ProjectHub.Server 재기동
7. Agent를 재시작하지 않고 heartbeat가 자동 재개되는지 확인
```

여기까지 성공하면 05-A를 실환경 완료 처리하고 05-B로 이동한다.

---

## 재부팅 자동복구는 별도 확인하되 네트워크 주소는 더 이상 변경하지 않는다

고정 hostname과 Named Tunnel 경로는 이미 확정되었으므로 이후 개발 과정에서 임시 URL로 되돌아가지 않는다.

남은 운영 확인은 다음이다.

```text
- cloudflared가 서버 PC 부팅 후 자동 연결되는지
- ProjectHub.Server를 어떤 방식으로 기동할지
- 서버 PC 재부팅 후 동일 hostname으로 /api/status가 복구되는지
```

이 검증에서 문제가 발생하더라도 `projecthub.ornithopter.bid`나 Tunnel route를 다시 설계하는 방향보다, 로컬 프로세스 자동시작/서비스 실행 문제로 분리해서 해결한다.

---

## 보안 경계

현재 고정 hostname은 인터넷에서 접근 가능한 경로이므로 장시간 무인증 write API 노출은 최종 운영 상태로 간주하지 않는다.

다만 이번 05-A E2E에서는 네트워크와 Agent 기능을 먼저 검증한다.

인증 보완 시 원칙:

```text
외부 접근 보호 = Cloudflare Access/Service Token 등 운영 계층
ProjectHub Agent 인증 = 향후 ProjectHub 자체 API Key/JWT 계층
```

두 계층을 구분한다.

인증 도입 때문에 ServerBaseUrl, hostname, origin port를 다시 바꾸는 설계를 피한다.

비밀값, tunnel token, service token, API key는 저장소·문서·로그에 기록하지 않는다.

---

## 아직 하지 말 것

05-A 실제 E2E가 끝나기 전에는 다음을 시작하지 않는다.

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

---

## Codex 수행 지침

1. `AGENTS.md` → 구현 계획 → `CurrentWork.md` → `tasks/05-agent-state.md` → 이 파일 순으로 읽는다.
2. 최신 `f57504b...`의 05-A 구현은 코드 구현 완료 상태로 취급한다.
3. 사용자가 `ornithopter.bid`와 Cloudflare Named Tunnel을 실제 구성했고 외부 `/api/status` 성공까지 확인한 사실을 관리 문서에 갱신한다.
4. 이번 작업에서 ProjectHub.Server의 로컬 endpoint가 `localhost:5240`으로 안정적으로 유지되는지 확인하고, 개발용 launch profile에만 의존한다면 운영 설정 경계를 보완한다.
5. 포트/바인딩은 표준 설정/환경 변수로 교체 가능해야 하며 도메인이나 Cloudflare 구현을 Server 코드에 하드코딩하지 않는다.
6. 외부 접속을 위해 직접 public bind/포트포워딩/별도 public TLS 구성을 추가하지 않는다.
7. Server API 계약은 그대로 유지한다.
8. 필요한 최소 보완 후 build/test를 수행하고 실제 결과만 기록한다.
9. 그 다음 사용자가 외부 DEV PC에서 05-A Agent E2E를 수행할 수 있도록 정확한 실행 설정 예를 문서에 남긴다. 비밀값은 넣지 않는다.
10. 05-A E2E가 성공하기 전에는 05-B를 시작하지 않는다.
11. 사용자 승인 없이 외부 서비스 설정, 배포, commit/push 등 파괴적/외부 변경을 수행하지 않는다.
