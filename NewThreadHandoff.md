# ProjectHub 새 스레드 인계

- 프로젝트 경로: `C:\Projects\AI-AGENTS\MCP\Server`
- 먼저 읽을 파일: `AGENTS.md`, `ProjectHub_IMPLEMENTATION_PLAN.md`, `CurrentWork.md`, 활성 작업 파일
- 현재 구조: `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent`; `tests/ProjectHub.Core.Tests`, `Server.Tests`
- 완료 상태: 기존 루트 초기화, .NET 솔루션 및 6개 프로젝트 생성, 04 Supabase 저장/E2E, 05-A Agent 생존 신호 구현, 빌드·테스트 통과
- 현재 작업: 07 프로젝트 배포 패키지 — 최신 `hw` 구조 Commit/Push 완료, Force Restore GUI 승인 실제 클릭 검증 대기, 이후 08 Server 설치·이전
- 07 검증: Setup이 `ProjectHub\\bin`, `ProjectHub\\config`, `ProjectHub\\state`, `ProjectHub\\log`를 구성하며 루트에는 Commit_Push/Fetch_Pull/Force_Restore 3개 진입점만 둔다. Restore는 별도 임시 디렉터리와 크기/SHA-256 검증을 사용한다.
- 표준 검증: `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore`
- 보안 경계: Supabase Service 역할 Key와 Agent API Key는 환경 변수·보안 저장소 외에 기록하지 않는다.
- 완료: 서버 PC에서 프로젝트 상태 POST/GET, Supabase 저장, 동일 프로젝트/작업 PC 갱신, 실제 `head_sha` 저장 확인
- 완료: 05-A 외부 E2E 및 05-B 읽기 전용 Git 상태 수집기 구현/테스트
- 완료: 05-C 파일 시스템 감시기/디바운스와 project 상태 자동 전송 구현 및 테스트
- 완료: 05-C 실제 개발 PC 루트 E2E 및 06 RS256 검증 토큰/NAS 프로비저닝 E2E
- 완료: 평상시 에이전트의 대용량 자동 해시/업로드/조정 경로 제거. `ProjectHub_Sync.ps1` 고정 manifest와 제어-plane 선반영, 별도 `ProjectHub_LargeData_Uploader.ps1`의 Supabase 기준 기존 세션 재사용, 청크 재개/완료 처리, STAGED·CHECKPOINTED 메타데이터 경로 구현
- 정책: Agent 재시작·watcher는 자동 upload/staging을 수행하지 않으며, 출처 변경 항목은 `CHANGED_DURING_UPLOAD`으로 제외한다.
- 고정 외부접속: Cloudflare 명명 터널 `projecthub`가 `projecthub.ornithopter.bid`에서 서버의 `http://localhost:5240` 원본으로 연결되며 외부 `/api/status`를 확인했다.
- NAS 게이트웨이 PHP 가시 루트는 `/mnt/HDD1/ProjectHub`, HTTPS 포트는 `8443`이다.
- NAS 게이트웨이 URL은 `https://dfblackbox-nas.duckdns.org:8443/projecthub/`이다.
- 최신 `hw` 반영 커밋: `8dd5c80081929da8957d32938ea5aa58c064f252` (`origin/main`).
