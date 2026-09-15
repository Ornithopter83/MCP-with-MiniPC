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

## 사용자 측 실제 검증 완료 상태

사용자가 Mini PC 서버와 Supabase, 원격 네트워크 경로를 실제로 준비/검증했다.

### Supabase

다음은 사용자 확인 완료 상태다.

```text
Supabase 프로젝트 생성 완료
PROJECTHUB_SUPABASE_URL 등록 완료
PROJECTHUB_SUPABASE_SERVICE_ROLE_KEY 등록 완료
supabase/workstations.sql 실행 완료
public.workstations 테이블 생성 확인 완료
```

`workstations` 테이블은 Supabase Table Editor에서 실제 생성된 것이 확인됐다.

비밀키 값 자체는 저장소/문서/로그에 기록하지 않는다.

### Mini PC ProjectHub.Server

원격 Mini PC에는 기존 프로그램이 없는 상태에서 저장소를 새로 복제했다.

사용자가 실제 수행하고 확인한 내용:

```text
저장소 clone 완료
dotnet restore 완료
전체 solution build 성공
ProjectHub.Server 실행 성공
/api/status 호출 성공
```

처음 clone 직후 `NETSDK1004`가 발생했지만 원인은 restore 미수행 상태였고, `dotnet restore` 후 전체 빌드가 성공했다.

Mini PC 내부 `/api/status` 실제 응답은 다음 의미의 값을 반환했다.

```text
server   = ProjectHub
status   = ok
database = supabase
```

PowerShell `Invoke-RestMethod`가 JSON을 객체로 변환해 표 형태로 표시한 것이며 HTTP/API 응답 자체는 정상이다.

### 원격 개발 PC -> Mini PC 네트워크

Mini PC와 개발 PC는 서로 다른 원격지에 있다.

Tailscale 설치를 시도했으나 해당 Windows PC의 MSI/Windows Installer 2502/2503 문제 때문에 설치가 실패했다. 이 문제는 ProjectHub 코드 문제로 취급하지 않는다.

현재 E2E 네트워크 검증에는 Cloudflare Quick Tunnel을 사용했다.

Mini PC에서 ProjectHub.Server를 실행한 상태에서 `cloudflared` Quick Tunnel을 열고, 개발 PC에서 다음 경로로 실제 호출에 성공했다.

```text
DEV PC
  -> HTTPS
  -> Cloudflare Quick Tunnel
  -> 원격 Mini PC
  -> ProjectHub.Server
  -> GET /api/status
```

개발 PC에서 Quick Tunnel URL의 `/api/status`를 호출해 실제로 다음 응답을 확인했다.

```text
server   = ProjectHub
status   = ok
database = supabase
```

따라서 현재 다음 경로는 실제 검증 완료로 본다.

```text
[완료] Mini PC 로컬 서버 기동
[완료] Mini PC 내부 /api/status
[완료] 원격 DEV PC -> Mini PC ProjectHub.Server 접근
[완료] Cloudflare Quick Tunnel 경유 HTTP 왕복
```

현재 단계의 원격 E2E 테스트에는 공유기 포트포워딩이 필요하지 않다.

단, Cloudflare Quick Tunnel은 임시/공개 URL이므로 운영 구성으로 간주하지 않는다. 쓰기 API 테스트 시에는 짧게 열고 테스트 후 종료한다. 장기 인증/공개 방식은 첫 heartbeat E2E 이후 별도 설계한다.

---

## 현재 개발 우선순위 변경

`04-B workstations 최소 스키마`는 사용자 측 실제 Supabase 적용까지 완료된 것으로 취급할 수 있다.

따라서 다음 개발 작업은 **04-C**다.

권장 순서:

```text
04-C IWorkstationRepository + SupabaseWorkstationRepository
04-D POST /api/agent/heartbeat
04-E GET /api/workstations
04-F 실제 원격 E2E 검증
04-G Project 상태 저장 확장
```

Codex는 `CurrentWork.md`와 `tasks/04_supabase-schema.md`의 상태를 이 실제 사용자 검증 결과에 맞게 업데이트하되, 기존 프로젝트 정책을 따른다.

---

## 04-C Repository 구현 요구

`IProjectStateRepository`에 heartbeat 기능을 섞지 않는다.

권장 최소 경계:

```text
ProjectHub.Core
  IWorkstationRepository
  Workstation

ProjectHub.Infrastructure
  SupabaseWorkstationRepository
```

첫 E2E에 필요한 기능만 구현한다.

```text
UpsertAsync(...)
ListAsync(...)
```

generic repository, ORM, migration framework 등은 지금 추가하지 않는다.

현재 마련된 named `HttpClient("Supabase")` / PostgREST 경계를 우선 재사용한다.

---

## 04-D POST /api/agent/heartbeat

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

중요 검증 조건:

```text
첫 heartbeat -> row 1개 생성
동일 workstation_id 재전송 -> row 수 증가하지 않음
last_seen -> 새 시각으로 갱신
```

클라이언트 시각보다 서버 시각을 사용한다.

---

## 04-E GET /api/workstations

Supabase의 `workstations` 목록을 반환한다.

첫 버전에는 pagination/filtering/sorting을 추가하지 않는다.

목적은 heartbeat 저장 결과를 사람이 즉시 확인할 수 있게 하는 것이다.

---

## 04-F 실제 원격 E2E 검증 방식

코드 구현 후 실제 검증은 Mini PC 내부 테스트만으로 끝내지 않는다.

이번에 이미 원격 경로가 확인됐으므로 최종 검증은 가능하면 다음 경로를 사용한다.

```text
DEV PC
  -> Cloudflare Quick Tunnel
  -> Mini PC ProjectHub.Server
  -> Supabase workstations
```

권장 실제 검증 순서:

```text
1. Codex가 04-C~E 구현
2. GitHub 반영 후 Mini PC에서 git pull
3. Mini PC에서 dotnet restore 필요 여부 확인
4. dotnet build
5. ProjectHub.Server 실행
6. Cloudflare Quick Tunnel 실행
7. DEV PC에서 POST /api/agent/heartbeat
8. DEV PC에서 GET /api/workstations
9. Supabase Table Editor에서도 row 확인
10. 동일 workstationId heartbeat 재전송
11. 중복 row 없음 확인
12. last_seen 갱신 확인
13. ProjectHub.Server 재시작
14. GET /api/workstations에서 데이터 유지 확인
15. 테스트 종료 후 Quick Tunnel 종료
```

실제 비밀키는 어느 명령 출력에도 표시하지 않는다.

---

## 04-F 완료 기준

다음이 모두 실제 확인되기 전에는 E2E 완료 처리하지 않는다.

```text
[x] workstations SQL 실제 Supabase 적용
[x] public.workstations 테이블 생성 확인
[x] Mini PC 저장소 clone
[x] Mini PC restore/build 성공
[x] Mini PC Server 기동
[x] Mini PC /api/status 성공
[x] 원격 DEV PC -> Mini PC /api/status 성공
[ ] Supabase REST workstation upsert 성공
[ ] DEV PC -> POST /api/agent/heartbeat 성공
[ ] workstations row 생성
[ ] 동일 workstation 재전송 시 중복 없음
[ ] last_seen 갱신
[ ] DEV PC -> GET /api/workstations 성공
[ ] Server 재시작 후 데이터 유지
```

build/test 성공과 실제 외부 Supabase E2E 성공은 반드시 구분해서 문서화한다.

---

## `/api/status` 관련

현재 `/api/status`의 `database = "supabase"` 값은 실제 DB 연결 확인 결과라기보다 구성/표시 값으로 취급한다.

현재 `/api/status`가 Mini PC와 원격 DEV PC 모두에서 정상 응답한 것은 **서버/네트워크 경로 검증 성공**이다.

이 사실만으로 Supabase 실제 REST 읽기/쓰기 성공으로 판단하지 않는다.

첫 heartbeat E2E 성공 후 필요하면 storage 상태 표현 개선을 검토할 수 있으나 지금은 우선순위가 아니다.

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
- 복잡한 인증/운영용 공개 구성
- Docker/Redis/자체 PostgreSQL
```

현재 목표는 **원격 DEV PC에서 heartbeat 한 건을 Mini PC로 보내고, Mini PC가 Supabase에 저장한 뒤 다시 읽어오는 것**이다.

---

## Codex 수행 지침

1. `AGENTS.md`, 구현 계획, `CurrentWork.md`, 활성 04 task, 이 파일 순으로 확인한다.
2. 사용자 Supabase 프로젝트/환경 변수/workstations SQL 적용은 완료된 상태로 취급한다.
3. Mini PC clone/build/server 실행도 실제 성공한 상태로 기록할 수 있다.
4. 원격 DEV PC -> Mini PC `/api/status` 경로도 실제 성공했다.
5. 다음 구현 세부 작업은 `04-C`부터 진행한다.
6. 04-C~E는 첫 heartbeat E2E에 필요한 최소 코드만 구현한다.
7. `/api/status` 성공을 Supabase 읽기/쓰기 성공으로 오해하지 않는다.
8. 04-F는 실제 DEV PC -> Mini PC -> Supabase 왕복 결과가 나오기 전 완료 처리하지 않는다.
9. 비밀키/토큰/Quick Tunnel 테스트 URL을 저장소에 고정 기록하지 않는다.
10. 사용자 승인 없이 commit/push/deploy를 자동 수행하지 않는다.

---

## 현재 권장 다음 행동

즉시 개발할 작업:

```text
04-C IWorkstationRepository + SupabaseWorkstationRepository
  -> 04-D POST /api/agent/heartbeat
  -> 04-E GET /api/workstations
```

그 후 사용자 실제 검증:

```text
Mini PC git pull/build/run
  -> Quick Tunnel
  -> DEV PC heartbeat POST
  -> Supabase row 확인
  -> GET /api/workstations
  -> 중복 방지/last_seen 갱신/재시작 persistence 확인
```

이 E2E가 성공한 뒤에만 04-G 이후 Project 상태 저장 확장으로 넘어간다.
