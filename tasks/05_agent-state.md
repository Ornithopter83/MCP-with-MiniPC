# Agent heartbeat와 상태수집

## 목표

Agent가 heartbeat와 branch, HEAD, dirty, 파일 목록을 Server로 전송한다.

## 세부 작업

### A. Agent 설정과 heartbeat 전송
### B. Git 상태 수집기 구현
### C. FileSystemWatcher debounce와 상태 전송

## 진행

잔여 작업 1개 (C-E2E)

## 변경 금지

- Agent는 Supabase에 직접 접근하지 않으며 Git을 수정하지 않는다.

## 완료 기준

- 등록 프로젝트 상태가 주기적으로 Server에 전달된다.

## 검증 방법

- 모의 Git 저장소와 테스트 Server로 payload 검증

## 결과

### A. Agent 설정과 heartbeat 전송 (완료: 2026-09-16)

- 설정 기반 heartbeat와 실제 외부 E2E를 완료했다.

### B. Git 상태 수집기 구현 (완료: 2026-09-16)

- `GitStateCollector`가 등록된 localPath에서 읽기 전용 Git 명령으로 branch, HEAD full SHA, dirty, changed/untracked/deleted 수를 수집한다.
- Git이 아닌 폴더나 명령 실패 시 진단 가능한 `GitStateException`을 반환한다.
- FileSystemWatcher와 자동 project state 전송은 아직 구현하지 않는다.

### C. FileSystemWatcher debounce와 상태 전송 (구현 완료: 2026-09-16)

- `RegisteredProjects` 설정으로 여러 프로젝트의 `ProjectId`, `DisplayName`, `LocalPath`, 선택적 `RepositoryUrl`을 등록할 수 있다.
- `ProjectActivityMonitor`가 생성/수정/삭제/이름변경 이벤트를 1초 debounce하고 `GitStateCollector`를 호출한다.
- 수집한 상태는 `POST /api/projects/{projectId}/state`로 전송하며, `.git`, `bin`, `obj`, `Library`, `Temp`, `Logs`, `node_modules` 이벤트는 무시한다.
- heartbeat loop와 project state 전송은 독립적으로 동작하고 오류 발생 시 Agent 전체를 종료하지 않는다.

- A 구현 완료: 설정 기반 Server heartbeat sender/runner를 추가했다. `ServerBaseUrl`, `WorkstationId`, `DisplayName`, `HeartbeatIntervalSeconds`를 JSON·환경 변수·명령줄로 설정할 수 있고 hostname은 `Environment.MachineName`으로 전송한다.
- A 구현 완료: HTTP 실패 시 프로세스를 종료하지 않고 다음 주기에 재시도하며 Ctrl+C 취소를 처리한다. Agent는 Supabase에 직접 접근하지 않는다.
- A 실환경 E2E 대기: DEV PC에서 Agent 실행 후 Mini PC Server와 Supabase의 반복 `last_seen` 갱신, Server 중단/복구 재전송을 확인해야 한다.
- A 서버 실행 경계 보완: `src/ProjectHub.Server/appsettings.json`의 표준 `Urls`를 `http://127.0.0.1:5240`으로 지정했다. `ASPNETCORE_URLS` 또는 실행 인자로 재정의할 수 있으며 Cloudflare 종속 코드는 추가하지 않았다.
- 고정 외부접속 기반: 사용자가 Cloudflare Named Tunnel `projecthub`와 `projecthub.ornithopter.bid`를 구성하고 외부 `/api/status` 성공을 확인했다. 이는 Agent heartbeat E2E와 별도 검증이다.
- 검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(2개 통과).
- B 검증: 모의 Git 상태 파싱 및 비저장소 오류 테스트 3개 통과. 전체 테스트는 5개 통과.
- C 검증: 전체 솔루션 빌드 성공(경고 0, 오류 0), 전체 테스트 5개 통과. 실제 DEV PC 파일 변경부터 Supabase 자동 갱신까지의 E2E는 사용자 확인 대기다.
- C 격리 E2E: 임시 Git 저장소에서 파일 생성 및 tracked 파일 수정을 수행해 Agent 로그의 `Project state sent`와 Server 조회 결과(`dirty=true`, `changed_count=1`, `untracked_count` 갱신)를 확인했다. 실제 사용자 프로젝트 E2E는 아직 대기다.
- C 보완: 파일 이벤트 후 `last_file_activity`가 자동 저장되도록 수정했다. `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(5개 통과).
