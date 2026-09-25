# 07 프로젝트 배포 패키지

독립 Git 저장소를 ProjectHub 관리 대상으로 비파괴 등록하고 Setup, Sync, Restore를 사용할 수 있게 한다.

구현 진행(2026-09-17): `ProjectHub_Setup.cmd` 단일 진입점, 작업 PC heartbeat 선등록, `.projecthub/project.json`, 기존 문서 조건부 생성, `bin` 배포 디렉터리, Sync/Restore 런처, 체크포인트 조회 API, NAS download endpoint를 구현했다. Sync는 이전/current manifest를 비교해 `ADDED`, `CHANGED`, `UNCHANGED`, `REMOVED`, `FAILED`를 분류하고, Windows GUI 승인 후에만 tombstone API를 호출한다. Restore는 현재 프로젝트 폴더를 기준으로 관리 파일을 갱신하며 `LOCAL_ONLY` 파일은 보존하고, 관리된 REMOVED 경로만 GUI 승인 후 삭제한다.

검증: 빈 `hw.git` clone에서 Setup으로 `hw` project 등록, 설정 생성, `bin`에 실행 파일 3개 배포, 조건부 관리 문서 생성을 확인했다. 초기 commit 후 generated Sync가 고정 manifest와 control-plane 상태를 생성했다. PowerShell 파서, `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore`가 통과했다. 새 삭제 diff/GUI/tombstone/Restore 동작은 Server 새 바이너리 배포·재기동 후 `hw`에서 탐색기 더블클릭 E2E를 수행한다.

2026-09-18 청크 전송 회귀 수정: `Invoke-WebRequest -InFile` 대신 대용량 바이너리 chunk PUT만 `curl.exe --data-binary`로 전환하고, 제어 엔드포인트와 검증 토큰 캐시/401 1회 갱신는 유지했다. 실패 시 file/세션/chunk index/chunk bytes/URI/HTTP 상태/예외/inner 예외/갱신 여부를 출력한다. 재개 세션의 `chunkSizeBytes`를 우선 사용해 chunk 크기 기준도 통일했다.

2026-09-18 `hw` 실제 검증: Explorer Sync와 동일한 `ProjectHub_Sync.ps1` 경로로 `forUpload.z01` 524,288,000 bytes를 업로드했다. curl 전송은 실패 0건, 원본 및 NAS object SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, `STAGED`, `CHECKPOINTED=true`로 완료됐다. Server 조회에서 `project_large_files` lifecycle `STAGED`, 세션 `COMPLETED`, 체크포인트 commit `be3cff250b18ce651f1e50167a9ff8407d395197`를 확인했다. 수정본은 중앙 uploader와 `hw\bin\ProjectHub_LargeData_Uploader.ps1`에 반영했다.

2026-09-18 작업 로그: Server에 `ProjectHub.Server` 범주를 추가해 시작, project state, assertion, upload 세션 완료, STAGED, 체크포인트, removal/tombstone 주요 단계만 Information으로 기록한다. `Microsoft`, ASP.NET Core, Supabase HttpClient 반복 로그는 Warning으로 제한하고 Console을 한 줄/시각 형식으로 설정했다. 로컬 `http://127.0.0.1:5280/api/status`가 200으로 응답하고 `SERVER_STARTED` 한 줄 로그를 출력하는 것을 확인했다.

2026-09-18 삭제 반복 표시 수정: Server enum의 `Removed` 값이 숫자 `7`로 JSON 반환되는 점을 반영하고, 체크포인트 API의 여러 결과에서 최신 단일 객체·문자열 commit SHA를 선택하도록 `Sync`를 보완했다. 삭제 API가 `forUpload.z01` tombstone 처리를 성공적으로 반환했으며, `hw`에서 후속 Sync 결과가 `0 large files`, `Removed=0`, `Failed=0`으로 확인되어 삭제 확인 창이 재표시되지 않았다.

2026-09-18 최종 작업 로그 형식: 공통 `WriteOperationLog`와 `ProjectHubConsoleFormatter`를 추가해 `yyyy-MM-dd HH:mm:ss [LEVEL] [WORKSTATION] [PROJECT] MESSAGE [STATUS]` 형식을 강제했다. 시작·assertion·세션·STAGED·체크포인트·removal·tombstone·project state 로그에 동일한 필드 순서와 상태 코드를 적용했다. 로컬 Server 기동에서 `SERVER_STARTED` 형식과 `/api/status=200`을 확인했다.

2026-09-18 NAS 삭제 및 Full-log 구현: Sync 승인 후 Server가 현재 활성 SHA-256 참조 수를 계산하고, NAS Gateway `delete-object.php`에 삭제 검증 토큰을 발급한다. named alias는 삭제하고 활성 참조가 0일 때만 canonical object를 삭제하며, 다른 참조가 있으면 보존한다. Gateway 실패는 `PARTIAL`/ERROR로 반환하고 tombstone을 되돌리지 않는다. `ContentRoot\log\yyyyMMdd.log` 일자별 writer, 날짜 rollover, 정상 종료 `SERVER_STOPPING` 구분선, heartbeat file-only 기록을 추가했다. NAS delete endpoint 운영 배포와 실제 NAS 삭제 E2E는 배포 후 검증 대상으로 남아 있다.

2026-09-18 운영 배포 재검증: Server `/api/status=200`, Gateway health 정상, `delete-object.php` 메서드 경계 `405`를 확인했다. `hw` 삭제 표식 객체에 삭제 검증 토큰을 발급해 `files/hw/forUpload.z01` alias와 `objects/sha256/e9/e93ac6ff...e8267` canonical object를 실제 삭제했고, 동일 요청 재호출은 `already_deleted=true`로 확인했다.

2026-09-18 enum 입력 호환성 보완: 운영 로그에서 문자열 `operation=delete` 요청이 ASP.NET JSON enum 역직렬화 400을 남긴 것을 확인했다. `LargeDataOperation`에 `JsonStringEnumConverter`를 적용해 문자열/기존 숫자 입력을 모두 허용하도록 수정했으며 빌드/테스트를 재통과했다. 운영 Server 재배포 후 문자열 입력 400이 재발하지 않는지 확인한다.

정책: 기존 관리 문서와 사용자 파일을 덮어쓰지 않으며 Git 변경과 대용량 업로드는 사용자가 명시적으로 실행할 때만 수행한다.

2026-09-18 CMD 진입점 보완: Setup/Sync/Restore/GC/Update/Agent 테스트 모든 사용자용 CMD가 PowerShell 종료 코드를 출력하고 항상 `pause`한 뒤 원래 종료 코드를 반환한다. Update는 `mode con: cols=220 lines=50`을 적용했다. Agent 테스트의 스크립트 누락 오류 경로도 같은 종료 처리를 사용한다. `git diff --check` 통과; 서버/게이트웨이 배포 후 Explorer 더블클릭 E2E만 남았다.

2026-09-18 v0.2 Git 진입점 구현: `ProjectHub_Commit_Push.cmd`는 `git add -A` → commit → fetch → pull --rebase → push → 기존 Sync/체크포인트 순서로 실행한다. `ProjectHub_Fetch_Pull.cmd`는 local dirty를 먼저 검사한 뒤 fetch → pull --rebase → Restore를 실행한다. 분리된 HEAD, merge/rebase 진행, 충돌, 푸시 거부는 자동 해결하지 않고 중단한다. 두 PS1 엔진은 Setup에서 `bin`으로 배포되며 모든 CMD는 pause/exit-code를 유지한다.

2026-09-18 강제 복구 구현: `ProjectHub_Force_Restore.cmd`는 `FORCE` 확인 전에는 아무것도 변경하지 않는다. 승인 후 remote branch 확인, `fetch origin`, `reset --hard`, `clean -fd`를 수행하고, `.projecthub/project.json`과 ProjectHub launcher/engine을 보존한 뒤 기존 Restore로 최신 체크포인트/NAS 상태를 복구한다. 실행 결과 HEAD와 최종 working tree를 출력하며, 사용자가 명시적으로 승인한 파괴적 작업으로만 동작한다.

2026-09-18 hw 순차 검증 결과: 최신 파일 배포, 일반 Fetch_Pull dirty 보호, Force Restore의 Git reset/clean, 500MiB 업로드·STAGED·CHECKPOINTED, ProjectHub 외 로컬 파일 삭제를 확인했다. Force Restore 재실행 시 Git 상태와 로컬 삭제는 복구됐지만 NAS 다운로드가 `RESTORE_SIZE_MISMATCH: forUpload.z01`로 실패했다. 따라서 NAS `download.php`가 반환하는 실제 크기/hash와 canonical object를 추가 확인해야 07 최종 E2E를 완료할 수 있다.

2026-09-18 최신 피드백 구현: Gateway `download.php`의 canonical path/readability/size/readfile 진단을 보강했고, Restore/Force Restore를 PREPARE→APPLY→최종 0 mismatch/0 missing 검증 구조로 변경했다. Setup은 `ProjectHub\bin`, `ProjectHub\config`, `ProjectHub\state`, `ProjectHub\log`를 생성하고 루트 최종 진입점 3개를 연결한다. PowerShell 파서, 빌드, 테스트는 통과했다. NAS에 수정 PHP를 배포한 뒤 `download.php` 단독 호출과 Fetch_Pull/Force Restore E2E를 재검증해야 한다.

2026-09-18 다운로드/복원 E2E 완료: NAS 웹 루트의 구버전 `download.php`를 수정본으로 교체했다. canonical object 실제 크기 524,288,000 bytes 확인, assertion 단독 download HTTP 200/Content-Length 일치, `hw` Restore 실행에서 matched=1/mismatched=0/missing=0, 로컬 SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267` 일치를 확인했다. 이후 `expected` 집계 표시도 배열 안전성을 보완했다.
2026-09-18 Restore 다운로드 UX 보완: `ProjectHub_Restore.ps1`의 NAS 다운로드를 HttpClient 스트림 수신으로 변경해 `DOWNLOAD`/퍼센트/바이트 진행과 완료 로그를 표시한다. 기존 PREPARE/APPLY 검증 및 완료 후 `pause`는 유지한다.
2026-09-18 Force Restore UX 보완: 파괴적 실행 전 콘솔에서 `FORCE`를 직접 입력하던 방식을 Windows 확인 대화상자로 변경했다. 경고 아이콘·대상 경로·삭제 범위를 표시하고 `계속`/`취소` 선택으로 승인하며, 새 `ProjectHub\bin` 엔진을 우선 사용하고 구형 `bin`은 호환한다.

2026-09-18 문서 정정 및 최신 `hw` 구조 반영: 이전 항목의 `.projecthub`, 루트 `bin`, `ProjectHub_Restore.cmd`, 콘솔 `FORCE` 입력, Restore 미완료 상태는 변경 전 검증 이력이다. 현재 `hw`는 구형 `.projecthub\`와 루트 `bin\`, 구형 Setup/Sync/Restore 진입점을 제거하고 `ProjectHub\bin`, `ProjectHub\config`, `ProjectHub\state`, `ProjectHub\log`를 사용한다. 루트 사용자 진입점은 Commit_Push/Fetch_Pull/Force_Restore 3개이며, 최신 구조 변경은 커밋 `8dd5c80081929da8957d32938ea5aa58c064f252`로 `origin/main`에 push했다. Commit_Push와 후속 Sync/uploader는 성공했으며 `checkpointed=true`를 확인했다. 잔여 검증은 Force Restore GUI의 계속/취소 실제 클릭 확인 1건이다.

운영 UX 주의: Commit_Push 루트 CMD는 종료 전 `pause`로 대기하지만, Sync가 별도 프로세스로 실행하는 대용량 uploader의 표준 입력 연결과 Enter 대기는 별도 항목으로 관리한다.
