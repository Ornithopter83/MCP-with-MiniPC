# ProjectHub 현재 작업 상태

Updated: 2026-09-15

## Baseline

- 저장소: `C:\Projects\AI-AGENTS\MCP\Server`
- 브랜치: 아직 Git 초기화 전
- 도구체인: .NET SDK 9.0.312 확인
- 솔루션: `ProjectHub.slnx`
- 활성 작업: `tasks/03_server-skeleton.md`

## 확인된 현재 구현

- `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent` 프로젝트가 생성됐다.
- `tests/ProjectHub.Core.Tests`, `Server.Tests` 프로젝트가 생성됐다.
- 프로젝트 참조가 Core 중심 계층으로 연결됐다.
- ASP.NET Core Server는 `/api/status` 스켈레톤을 제공하며 Supabase 연결·정책 옵션 바인딩은 아직 구성하지 않았다.

## 목표 구조

개발 PC Agent가 heartbeat와 Git 상태를 Server에 보내고, Server의 ProjectService가 Infrastructure 저장소를 통해 Supabase에 상태·이벤트·lease를 기록한다.

## 진행

잔여 작업 6개 (03, 04, 05, 06, 07, 08)

## 작업 정책

- 한 번에 하나의 세부 작업만 수행한다.
- 자동 Git 변경·원격 명령·파일 삭제는 금지한다.
- 비밀값은 환경 변수로만 읽고 저장소에 기록하지 않는다.

## 표준 검증

```powershell
dotnet build ProjectHub.slnx --no-restore
dotnet test ProjectHub.slnx --no-restore
```

## 다음 작업

03 Server 스켈레톤의 B. ProjectHub 서비스 등록
