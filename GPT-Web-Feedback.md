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

이 파일은 기존 관리 파일을 대체하지 않는다. Codex는 이 피드백을 읽고 기존 작업 흐름 안에서 필요한 변경만 반영한다.

---

## 최신 GitHub 구현 상태 확인

커밋 `6fb8dca4e1613e396d013f93b58c11a625c9365a` (`04-E`)를 확인했다.

이 커밋에서 다음이 실제 구현 완료됐다.

```text
04-C IWorkstationRepository + SupabaseWorkstationRepository
04-D POST /api/agent/heartbeat
04-E GET /api/workstations
```

구체적으로 확인된 변경:

```text
- ProjectHub.Core/IWorkstationRepository.cs 추가
- Workstation 모델 추가
- SupabaseWorkstationRepository 추가
- workstation_id 기준 PostgREST upsert 구현
- POST /api/agent/heartbeat 구현
- 서버 시각 기반 last_seen 생성
- GET /api/workstations 구현
- DI에 IWorkstationRepository 등록
- CurrentWork.md의 다음 작업을 04-F로 변경
- tasks/04_supabase-schema.md에서 C/D/E 완료 처리
```

따라서 이전 피드백에 있었던 "다음 작업은 04-C"라는 지시는 폐기한다.

현재 정확한 다음 단계는 **04-F 실제 E2E 검증**이다.

---

## 사용자 측 실제 준비/검증 완료 상태

### Supabase

사용자가 다음을 실제 완료했다.

```text
Supabase 프로젝트 생성 완료
PROJECTHUB_SUPABASE_URL 등록 완료
PROJECTHUB_SUPABASE_SERVICE_ROLE_KEY 등록 완료
supabase/workstations.sql 실행 완료
public.workstations 테이블 생성 확인 완료
```

실제 비밀키 값은 저장소/문서/로그에 기록하지 않는다.

### Mini PC ProjectHub.Server

원격 Mini PC에 저장소를 새로 복제했고 다음을 실제 확인했다.

```text
git clone 완료
dotnet restore 완료
전체 solution build 성공
ProjectHub.Server 실행 성공
Mini PC 내부 GET /api/status 성공
```

실제 응답 의미:

```text
server   = ProjectHub
status   = ok
database = supabase
```

`Invoke-RestMethod`가 JSON을 PowerShell 객체로 변환해 표 형태로 출력한 것은 정상 동작이다.

### 원격 DEV PC -> Mini PC 네트워크

Mini PC와 개발 PC는 서로 다른 원격지에 있다.

Tailscale은 해당 Windows PC의 MSI 2502/2503 문제로 설치하지 못했으며, 이는 ProjectHub 코드 문제로 취급하지 않는다.

대신 Cloudflare Quick Tunnel을 사용했고 다음 실제 호출에 성공했다.

```text
DEV PC
  -> HTTPS
  -> Cloudflare Quick Tunnel
  -> 원격 Mini PC
  -> ProjectHub.Server
  -> GET /api/status
```

개발 PC에서 Quick Tunnel URL을 통해 `/api/status`가 실제 성공했다.

따라서 현재 다음은 검증 완료다.

```text
[x] Mini PC 서버 기동
[x] Mini PC 내부 /api/status
[x] 원격 DEV PC -> Mini PC 접근
[x] Cloudflare Quick Tunnel 경유 HTTP 왕복
```

Cloudflare Quick Tunnel은 임시 테스트 경로다. 운영 구성으로 간주하지 않는다.

---

## 현재 정확한 목표: 04-F 실제 E2E 검증

이제 새 코드를 추가하기보다, 이미 구현된 04-C~E가 실제 Mini PC와 Supabase에서 정상 동작하는지 검증한다.

목표 경로:

```text
DEV PC
  -> POST /api/agent/heartbeat
  -> Cloudflare Quick Tunnel
  -> Mini PC ProjectHub.Server
  -> Supabase workstations upsert
  -> GET /api/workstations
  -> 동일 workstation 조회
```

---

## 사용자 실제 검증 순서

Mini PC에서 먼저 GitHub 최신 내용을 반영한다.

```text
1. git pull
2. dotnet restore (필요 시)
3. dotnet build
4. ProjectHub.Server 실행
5. Cloudflare Quick Tunnel 실행
```

그 후 DEV PC에서 실제 API를 호출한다.

### 1. heartbeat 최초 전송

예시 요청 본문:

```json
{
  "workstationId": "DEV-PC-01",
  "displayName": "DEV-PC-01",
  "hostname": "SUHO_DEV_PC"
}
```

검증 목표:

```text
HTTP 성공
Supabase workstations에 row 1개 생성
last_seen 기록
```

### 2. workstation 목록 조회

`GET /api/workstations` 호출 후 방금 보낸 `DEV-PC-01`이 반환되는지 확인한다.

### 3. 동일 heartbeat 재전송

같은 `workstationId`로 다시 POST한다.

검증 목표:

```text
row 수 증가하지 않음
기존 row가 update 됨
last_seen이 더 새로운 시각으로 갱신됨
```

### 4. 서버 재시작 후 persistence 확인

ProjectHub.Server를 재시작한 뒤 다시 `GET /api/workstations`를 호출한다.

검증 목표:

```text
Supabase에 저장된 workstation 데이터 유지
```

---

## 04-F 완료 기준

다음이 모두 실제 확인되기 전에는 04-F를 완료 처리하지 않는다.

```text
[x] workstations SQL 실제 Supabase 적용
[x] public.workstations 테이블 생성 확인
[x] Mini PC 저장소 clone
[x] Mini PC restore/build 성공
[x] Mini PC Server 기동
[x] Mini PC /api/status 성공
[x] 원격 DEV PC -> Mini PC /api/status 성공
[x] 04-C repository 구현 코드 존재
[x] 04-D heartbeat API 구현 코드 존재
[x] 04-E workstation GET 구현 코드 존재
[ ] Mini PC 최신 04-E 코드 pull/build/run
[ ] Supabase REST workstation upsert 성공
[ ] DEV PC -> POST /api/agent/heartbeat 성공
[ ] workstations row 생성
[ ] 동일 workstation 재전송 시 중복 없음
[ ] last_seen 갱신
[ ] DEV PC -> GET /api/workstations 성공
[ ] Server 재시작 후 데이터 유지
```

build/test 성공과 실제 외부 Supabase E2E 성공은 반드시 구분해서 기록한다.

---

## `/api/status` 해석 주의

현재 `/api/status`의 `database = "supabase"` 표시는 실제 DB 쿼리 성공 증명이 아니다.

지금까지 `/api/status` 성공으로 확인된 것은 다음뿐이다.

```text
ProjectHub.Server 정상 기동
Mini PC 내부 HTTP 접근 가능
원격 DEV PC -> Mini PC 네트워크 접근 가능
```

Supabase 실제 읽기/쓰기 성공은 반드시 heartbeat와 `/api/workstations`로 별도 검증한다.

---

## 아직 하지 말 것

04-F 성공 전에는 아래 확장을 미룬다.

```text
- 04-G Project 상태 저장 확장
- projects 전체 구현 확대
- project_states 전체 구현 확대
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

현재 목표는 오직 **원격 DEV PC -> Mini PC -> Supabase -> Mini PC -> DEV PC 데이터 왕복 성공**이다.

---

## Codex 수행 지침

1. `AGENTS.md`, 구현 계획, `CurrentWork.md`, 활성 04 task, 이 파일 순으로 확인한다.
2. 커밋 `04-E` 기준 04-C/D/E는 이미 구현 완료 상태로 취급한다.
3. 이전 피드백의 "04-C부터 구현" 지시는 무시한다.
4. 현재 다음 작업은 04-F 실제 E2E 검증이다.
5. 사용자 Supabase 환경 변수와 workstations SQL 적용은 완료된 상태다.
6. Mini PC clone/build/server 실행과 원격 `/api/status` 접근도 이미 실제 성공했다.
7. 실제 heartbeat/GET 결과가 확인되기 전에는 04-F 완료 처리하지 않는다.
8. 비밀키/토큰/Quick Tunnel URL은 저장소에 고정 기록하지 않는다.
9. E2E 중 문제가 나오면 먼저 현재 구현의 오류를 최소 수정하고, 구조 확대는 하지 않는다.
10. 사용자 승인 없이 자동 deploy 또는 외부 시스템 파괴적 변경을 수행하지 않는다.

---

## 현재 권장 다음 행동

새 구현이 아니라 사용자 측 실제 실행 검증으로 진행한다.

```text
Mini PC: git pull -> build -> ProjectHub.Server 실행
  -> Cloudflare Quick Tunnel 실행
DEV PC: POST /api/agent/heartbeat
  -> Supabase row 확인
  -> GET /api/workstations
  -> 동일 heartbeat 재전송
  -> 중복 없음 + last_seen 갱신 확인
  -> Server 재시작 후 persistence 확인
```

이 검증이 성공한 뒤에만 04-G로 넘어간다.
