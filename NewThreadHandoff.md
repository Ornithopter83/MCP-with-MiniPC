# ProjectHub 새 스레드 인계

- 프로젝트 경로: `C:\Projects\AI-AGENTS\MCP\Server`
- 먼저 읽을 파일: `AGENTS.md`, `ProjectHub_IMPLEMENTATION_PLAN.md`, `CurrentWork.md`, 활성 task 파일
- 현재 구조: `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent`; `tests/ProjectHub.Core.Tests`, `Server.Tests`
- 완료 상태: 기존 루트 초기화, .NET 솔루션 및 6개 프로젝트 생성, 04 Supabase 저장/E2E, 05-A Agent heartbeat 구현, 빌드·테스트 통과
- 현재 작업: 05 Agent heartbeat와 상태수집 A, 실환경 E2E 대기
- 표준 검증: `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore`
- 보안 경계: Supabase Service Role Key와 Agent API Key는 환경 변수·보안 저장소 외에 기록하지 않는다.
- 완료: 서버 PC에서 project state POST/GET, Supabase 저장, 동일 project/workstation update, 실제 `head_sha` 저장 확인
- 보류: 05-A 실환경 E2E, Git 상태수집, LAN Agent 등록, Mini PC 운영 배포, 외부 계정 승인
