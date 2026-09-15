# ProjectHub 새 스레드 인계

- 프로젝트 경로: `C:\Projects\AI-AGENTS\MCP\Server`
- 먼저 읽을 파일: `AGENTS.md`, `ProjectHub_IMPLEMENTATION_PLAN.md`, `CurrentWork.md`, 활성 task 파일
- 현재 구조: `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent`; `tests/ProjectHub.Core.Tests`, `Server.Tests`
- 완료 상태: 기존 루트 초기화, .NET 솔루션 및 6개 프로젝트 생성, 프로젝트 참조 연결, `/api/status` 스켈레톤 생성, Core/Infrastructure/Server DI 경계 연결, 빌드·기본 테스트 통과
- 현재 작업: 04 Supabase 스키마와 저장소, 다음 세부 작업 C
- 표준 검증: `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore`
- 보안 경계: Supabase Service Role Key와 Agent API Key는 환경 변수·보안 저장소 외에 기록하지 않는다.
- 보류: 실제 Supabase 프로젝트/스키마 적용, LAN Agent 등록, Mini PC 운영 배포, 외부 계정 승인
