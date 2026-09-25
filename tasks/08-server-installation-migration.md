# 08 Server 설치·이전 가이드

Windows 11 이상, Git, 요구 .NET SDK, PowerShell, 기존 Supabase 및 NAS 게이트웨이를 사용한다. 비밀값은 환경 변수 또는 Windows 보안 저장소에만 둔다.

설치 순서: `git clone https://github.com/Ornithopter83/MCP-with-MiniPC.git C:\ProjectHub`, `dotnet --info`, `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore`.

Server-only 환경 변수: `PROJECTHUB_SUPABASE_URL`, `PROJECTHUB_SUPABASE_SERVICE_ROLE_KEY`, `PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM`. 실제 값은 문서와 저장소에 기록하지 않는다.

수동 실행: `dotnet run --project .\src\ProjectHub.Server\ProjectHub.Server.csproj --urls http://127.0.0.1:5240`.

운영 자동 시작은 Windows Task Scheduler에 위 명령을 등록하고, 환경 변수는 해당 작업의 실행 계정 환경에 설정한다.

이전 검증 순서: 로컬/외부 `/api/status=200`, 기존 Supabase 프로젝트 조회, 검증 토큰 발급, NAS 게이트웨이 인증, 기존 SHA-256 객체 재사용, 프로젝트 동기화, 체크포인트 복원.

Server 주소가 변경되면 각 프로젝트의 `.projecthub/project.json`의 `serverBaseUrl`을 수동 갱신한다. 기존 Supabase/NAS object는 재업로드하지 않는다.
