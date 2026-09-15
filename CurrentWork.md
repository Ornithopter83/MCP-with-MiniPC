# ProjectHub 현재 작업 상태

Updated: 2026-09-15

## Baseline

- 저장소: `C:\Projects\AI-AGENTS\MCP\Server`
- 브랜치: `main` (원격 `origin/main` 추적)
- 도구체인: .NET SDK 9.0.312 확인
- 솔루션: `ProjectHub.sln`
- 활성 작업: `tasks/04_supabase-schema.md`

## 확인된 현재 구현

- `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent` 프로젝트가 생성됐다.
- `tests/ProjectHub.Core.Tests`, `Server.Tests` 프로젝트가 생성됐다.
- 프로젝트 참조가 Core 중심 계층으로 연결됐다.
- ASP.NET Core Server는 `/api/status` 스켈레톤을 제공하며 Supabase 연결·정책 옵션 바인딩은 아직 구성하지 않았다.
- `GPT-Web-Feedback.md`가 원격에서 추가됐으며, pull 전 확인 규칙을 `AGENTS.md`에 반영했다.
- Core 서비스 계약과 Infrastructure의 교체 가능한 NoOp 저장소 DI 등록 경계를 추가했다.
- `/api/status` 통합 테스트가 추가되어 HTTP 200과 기본 JSON 필드를 검증한다.
- `SupabaseOptions`와 named `HttpClient` 등록 경계가 추가됐으며 실제 Supabase 호출은 아직 하지 않는다.
- 04-A Supabase 설정/클라이언트 경계 구현과 빌드·테스트 검증이 완료됐다.
- `supabase/workstations.sql`로 04-B 최소 스키마 정의를 완료했다. 실제 Supabase 적용은 사용자 확인이 필요하다.

## 목표 구조

개발 PC Agent가 heartbeat와 Git 상태를 Server에 보내고, Server의 ProjectService가 Infrastructure 저장소를 통해 Supabase에 상태·이벤트·lease를 기록한다.

## 진행

잔여 작업 5개 (04, 05, 06, 07, 08)

## 작업 정책

- 한 번에 하나의 세부 작업만 수행한다.
- 자동 Git 변경·원격 명령·파일 삭제는 금지한다.
- 비밀값은 환경 변수로만 읽고 저장소에 기록하지 않는다.

## 표준 검증

```powershell
dotnet build ProjectHub.sln --no-restore
dotnet test ProjectHub.sln --no-restore
```

## 다음 작업

04 Supabase 스키마와 저장소의 C. IWorkstationRepository와 Supabase 구현
