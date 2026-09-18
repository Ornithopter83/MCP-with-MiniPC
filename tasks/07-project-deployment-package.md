# 07 프로젝트 배포 패키지

독립 Git 저장소를 ProjectHub 관리 대상으로 비파괴 등록하고 Setup, Sync, Restore를 사용할 수 있게 한다.

구현 진행(2026-09-17): `ProjectHub_Setup.cmd` 단일 진입점, workstation heartbeat 선등록, `.projecthub/project.json`, 기존 문서 조건부 생성, `bin` 배포 디렉터리, Sync/Restore 런처, checkpoint 조회 API, NAS download endpoint를 구현했다. Sync는 이전/current manifest를 비교해 `ADDED`, `CHANGED`, `UNCHANGED`, `REMOVED`, `FAILED`를 분류하고, Windows GUI 승인 후에만 tombstone API를 호출한다. Restore는 현재 프로젝트 폴더를 기준으로 관리 파일을 갱신하며 `LOCAL_ONLY` 파일은 보존하고, 관리된 REMOVED 경로만 GUI 승인 후 삭제한다.

검증: 빈 `hw.git` clone에서 Setup으로 `hw` project 등록, 설정 생성, `bin`에 실행 파일 3개 배포, 조건부 관리 문서 생성을 확인했다. 초기 commit 후 generated Sync가 고정 manifest와 control-plane 상태를 생성했다. PowerShell parser, `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore`가 통과했다. 새 삭제 diff/GUI/tombstone/Restore 동작은 Server 새 바이너리 배포·재기동 후 `hw`에서 탐색기 더블클릭 E2E를 수행한다.

2026-09-18 chunk transport 회귀 수정: `Invoke-WebRequest -InFile` 대신 대용량 binary chunk PUT만 `curl.exe --data-binary`로 전환하고, control endpoint와 assertion cache/401 1회 refresh는 유지했다. 실패 시 file/session/chunk index/chunk bytes/URI/HTTP status/exception/inner exception/refresh 여부를 출력한다. resume session의 `chunkSizeBytes`를 우선 사용해 chunk 크기 기준도 통일했다.

2026-09-18 `hw` 실제 검증: Explorer Sync와 동일한 `ProjectHub_Sync.ps1` 경로로 `forUpload.z01` 524,288,000 bytes를 업로드했다. curl 전송은 실패 0건, 원본 및 NAS object SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, `STAGED`, `CHECKPOINTED=true`로 완료됐다. Server 조회에서 `project_large_files` lifecycle `STAGED`, session `COMPLETED`, checkpoint commit `be3cff250b18ce651f1e50167a9ff8407d395197`를 확인했다. 수정본은 중앙 uploader와 `hw\bin\ProjectHub_LargeData_Uploader.ps1`에 반영했다.

2026-09-18 operation logging: Server에 `ProjectHub.Server` category를 추가해 startup, project state, assertion, upload session 완료, STAGED, CHECKPOINT, removal/tombstone 주요 단계만 Information으로 기록한다. `Microsoft`, ASP.NET Core, Supabase HttpClient 반복 로그는 Warning으로 제한하고 Console을 single-line/timestamp 형식으로 설정했다. 로컬 `http://127.0.0.1:5280/api/status`가 200으로 응답하고 `SERVER_STARTED` 한 줄 로그를 출력하는 것을 확인했다.

2026-09-18 삭제 반복 표시 수정: Server enum의 `Removed` 값이 숫자 `7`로 JSON 반환되는 점을 반영하고, checkpoint API의 여러 결과에서 최신 단일 객체·문자열 commit SHA를 선택하도록 `Sync`를 보완했다. 삭제 API가 `forUpload.z01` tombstone 처리를 성공적으로 반환했으며, `hw`에서 후속 Sync 결과가 `0 large files`, `Removed=0`, `Failed=0`으로 확인되어 삭제 확인 창이 재표시되지 않았다.

2026-09-18 최종 operation log 형식: 공통 `WriteOperationLog`와 `ProjectHubConsoleFormatter`를 추가해 `yyyy-MM-dd HH:mm:ss [LEVEL] [WORKSTATION] [PROJECT] MESSAGE [STATUS]` 형식을 강제했다. startup·assertion·session·STAGED·checkpoint·removal·tombstone·project state 로그에 동일한 필드 순서와 상태 코드를 적용했다. 로컬 Server 기동에서 `SERVER_STARTED` 형식과 `/api/status=200`을 확인했다.

2026-09-18 NAS 삭제 및 Full-log 구현: Sync 승인 후 Server가 현재 활성 SHA-256 참조 수를 계산하고, NAS Gateway `delete-object.php`에 delete assertion을 발급한다. named alias는 삭제하고 활성 참조가 0일 때만 canonical object를 삭제하며, 다른 참조가 있으면 보존한다. Gateway 실패는 `PARTIAL`/ERROR로 반환하고 tombstone을 되돌리지 않는다. `ContentRoot\log\yyyyMMdd.log` 일자별 writer, 날짜 rollover, 정상 종료 `SERVER_STOPPING` 구분선, heartbeat file-only 기록을 추가했다. NAS delete endpoint 운영 배포와 실제 NAS 삭제 E2E는 배포 후 검증 대상으로 남아 있다.

정책: 기존 관리 문서와 사용자 파일을 덮어쓰지 않으며 Git 변경과 대용량 업로드는 사용자가 명시적으로 실행할 때만 수행한다.
