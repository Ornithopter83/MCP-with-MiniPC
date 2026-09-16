# ProjectHub 새 스레드 인계

- 프로젝트 경로: `C:\Projects\AI-AGENTS\MCP\Server`
- 먼저 읽을 파일: `AGENTS.md`, `ProjectHub_IMPLEMENTATION_PLAN.md`, `CurrentWork.md`, 활성 task 파일
- 현재 구조: `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent`; `tests/ProjectHub.Core.Tests`, `Server.Tests`
- 완료 상태: 기존 루트 초기화, .NET 솔루션 및 6개 프로젝트 생성, 04 Supabase 저장/E2E, 05-A Agent heartbeat 구현, 빌드·테스트 통과
- 현재 작업: 06 Large Data/NAS 최종 통합 검증
- 표준 검증: `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore`
- 보안 경계: Supabase Service Role Key와 Agent API Key는 환경 변수·보안 저장소 외에 기록하지 않는다.
- 완료: 서버 PC에서 project state POST/GET, Supabase 저장, 동일 project/workstation update, 실제 `head_sha` 저장 확인
- 완료: 05-A 외부 E2E 및 05-B 읽기 전용 Git 상태 수집기 구현/테스트
- 완료: 05-C FileSystemWatcher/debounce와 project state 자동 전송 구현 및 테스트
- 완료: 05-C 실제 DEV PC root E2E 및 06 RS256 assertion/NAS provision E2E
- 잔여: chunk upload/status/resume/finalize, STAGED·CHECKPOINTED metadata, Agent startup reconciliation
- 고정 외부접속: Cloudflare Named Tunnel `projecthub`가 `projecthub.ornithopter.bid`에서 Server의 `http://localhost:5240` origin으로 연결되며 외부 `/api/status`를 확인했다.
- NAS Gateway PHP-visible root는 `/mnt/HDD1/ProjectHub`, HTTPS port는 `8443`이다.
- NAS Gateway URL은 `https://dfblackbox-nas.duckdns.org:8443/projecthub/`이다.
