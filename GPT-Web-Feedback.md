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

## 최신 상태 요약

04-C~E 구현 후 실제 Mini PC + Supabase 환경에서 04-F E2E 검증까지 완료됐다.

현재 상태:

```text
04-A Supabase 설정/클라이언트 경계        완료
04-B workstations 최소 스키마            완료
04-C IWorkstationRepository              완료
04-D POST /api/agent/heartbeat           완료
04-E GET /api/workstations               완료
04-F 실제 Supabase E2E 검증              완료
```

따라서 다음 작업은 **04-G Project 상태 확장**이다.

---

## 04-F 실제 E2E 검증 완료 결과

사용자가 원격 Mini PC에서 최신 코드를 반영하고 ProjectHub.Server를 실행한 뒤 실제 Supabase 연결을 검증했다.

확인된 결과:

```text
[x] Mini PC ProjectHub.Server 실행
[x] localhost:5240 heartbeat POST 성공
[x] Supabase REST POST 응답 201 확인
[x] workstations row 생성 확인
[x] 동일 workstation_id로 heartbeat 2회 전송
[x] row가 1개로 유지됨
[x] last_seen 갱신 확인
[x] created_at은 최초 생성 시각 유지
[x] GET /api/workstations 조회 성공
[x] Supabase Table Editor에서 실제 데이터 확인
[x] 사용자 최종 E2E 검증 완료 확인
```

실제 검증에 사용된 workstation 예:

```text
workstation_id = SERVER-PC-01
display_name   = SERVER-PC-01
hostname       = DESKTOP-GGV0EFL
```

같은 `workstation_id`를 두 번 전송한 뒤 Supabase에는 row가 1개만 존재했고 `last_seen`이 더 최신 시각으로 변경됐다.

따라서 `workstation_id` 기준 PostgREST upsert가 정상 동작하는 것으로 확인한다.

---

## 04-F 중 발생했던 오류와 해결 상태

초기 heartbeat E2E에서 Supabase POST가 HTTP 400으로 실패했다.

원인은 신규 Workstation 객체의 `created_at`, `updated_at` null 값이 JSON payload에 포함되어 DB의 NOT NULL/default 정책과 충돌하는 구조였다.

보완 후:

```text
- upsert 요청 payload에서 null 메타데이터 필드 제외
- created_at / updated_at은 DB default/trigger에 맡김
- Supabase 오류 응답 본문을 읽어 진단 가능하도록 개선
```

수정된 코드로 실제 POST 201과 데이터 저장 성공을 확인했으므로 이 문제는 현재 해결된 상태로 본다.

---

## 네트워크/운영 확인 상태

Mini PC와 개발 PC는 서로 다른 원격지에 있다.

Tailscale은 해당 Windows PC의 MSI 2502/2503 문제 때문에 사용하지 못했으나, ProjectHub 기능 검증에는 영향이 없다.

원격 접근은 Cloudflare Quick Tunnel로 다음 경로를 이미 검증했다.

```text
DEV PC
  -> HTTPS
  -> Cloudflare Quick Tunnel
  -> Mini PC ProjectHub.Server
```

현재 Quick Tunnel은 테스트용 임시 경로로만 취급한다.

Mini PC 로컬 테스트에서는 현재 ProjectHub.Server가 `http://localhost:5240`에서 정상 실행되는 것도 확인됐다.

---

## 다음 작업: 04-G Project 상태 확장

이제 heartbeat로 workstation 생존 상태를 저장하는 단계에서, 실제 프로젝트의 Git/작업 상태를 중앙 서버가 보관하는 단계로 넘어간다.

다만 04-G 전체를 한 번에 구현하지 않는다.

### 04-G 첫 단계 목표

먼저 **projects + project_states 최소 저장 경로**만 만든다.

목표 흐름:

```text
수동 API 요청
  -> Mini PC ProjectHub.Server
  -> Supabase projects / project_states
  -> 조회 API
  -> 저장된 project state 확인
```

첫 단계에서는 Agent 자동 수집이 아니라 **수동 POST/GET으로 저장 경로 자체를 검증**한다.

---

## 권장 최소 상태 모델

첫 `project_states` 경로에서 다음 정보를 우선 고려한다.

```text
projectId
workstationId
branch
headSha
dirty
changedCount
untrackedCount
deletedCount
diffFingerprint
lastFileActivity
lastSeen
```

기존 설계 방향을 유지하되, 첫 구현에서는 E2E에 필요한 필드만 사용하고 불필요한 구조 확대를 피한다.

### projects 최소 정보

초기 `projects`에는 다음 정도면 충분하다.

```text
project_id
display_name
repo_url
created_at
updated_at
```

### project_states 핵심 제약

한 프로젝트/한 workstation의 최신 상태는 중복 row를 계속 쌓기보다 우선 upsert 가능한 구조를 권장한다.

예:

```text
UNIQUE(project_id, workstation_id)
```

Git 이력/감사 로그는 이후 `project_events`에서 다룰 수 있으므로 첫 단계에서는 최신 상태 저장에 집중한다.

---

## 04-G 첫 E2E 목표

첫 검증은 다음 정도로 제한한다.

```text
1. projects / project_states 최소 SQL 정의
2. Supabase SQL 적용은 사용자 확인 후 진행 상태 반영
3. ProjectState 저장 Repository 경계 구현
4. 수동 POST API 구현
5. GET 조회 API 구현
6. Mini PC에서 실제 Supabase E2E 검증
7. 동일 projectId + workstationId 재전송 시 update 확인
```

예상 테스트 데이터 예:

```text
projectId      = MCP-with-MiniPC
workstationId  = SERVER-PC-01
branch         = main
headSha        = <현재 commit SHA>
dirty          = false
```

실제 필드/엔드포인트 명은 기존 코드 구조와 정책에 맞춰 Codex가 정리한다.

---

## 아직 하지 말 것

04-G 첫 project_state 수동 E2E 성공 전에는 아래를 미룬다.

```text
- FileSystemWatcher
- 자동 Git status 수집
- 자동 git diff 계산
- Agent 주기 전송 자동화
- active_leases 판정
- project_events 확장
- 멀티 PC 충돌 판정
- 최신 workstation 자동 선택 로직
- MCP
- GitHub Bridge
- NAS Gateway
- 자동 Git commit/push/reset/merge
- 원격 shell 실행
- 복잡한 인증/운영용 공개 구성
- Docker/Redis/자체 PostgreSQL
```

지금 우선순위는 **project state 한 건을 Supabase에 저장하고 다시 읽는 것**이다.

---

## Codex 수행 지침

1. `AGENTS.md`, 구현 계획, `CurrentWork.md`, 활성 task, 이 파일 순으로 확인한다.
2. 04-F E2E는 사용자 실제 환경에서 완료된 것으로 취급한다.
3. `CurrentWork.md`와 `tasks/04_supabase-schema.md`의 04-F 상태를 완료로 갱신한다.
4. 다음 작업을 04-G Project 상태 확장으로 전환한다.
5. 04-G 전체를 한 번에 구현하지 말고 첫 세부 단계만 수행한다.
6. 첫 세부 단계는 `projects + project_states` 최소 스키마/저장 경로다.
7. 먼저 수동 POST/GET으로 Supabase E2E가 가능한 최소 구조를 만든다.
8. FileSystemWatcher나 자동 Git 상태 수집은 아직 구현하지 않는다.
9. Supabase Secret/Service Role Key를 코드, 문서, 로그에 기록하지 않는다.
10. build/test 성공과 실제 Supabase E2E 성공을 계속 구분해서 기록한다.
11. 사용자 승인 없이 자동 deploy, Git mutation, 외부 시스템 파괴적 변경을 수행하지 않는다.

---

## 현재 권장 다음 행동

Codex가 수행할 다음 작업:

```text
04-F 완료 기록
  -> 04-G 시작
  -> projects + project_states 최소 스키마/계약 설계
  -> 첫 수동 저장/조회 API 구현
```

사용자 측 다음 실제 검증은 해당 구현이 GitHub에 반영된 뒤 진행한다.

```text
Mini PC git pull
  -> restore/build
  -> ProjectHub.Server 실행
  -> 수동 project_state POST
  -> Supabase row 확인
  -> GET 조회
  -> 동일 project/workstation 재전송 시 update 확인
```

이 첫 project_state E2E가 성공한 뒤 `project_events`, `active_leases`, 자동 Agent 수집으로 확장한다.
