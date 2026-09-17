# 07 프로젝트 배포 패키지

독립 Git 저장소를 ProjectHub 관리 대상으로 비파괴 등록하고 Setup, Sync, Restore를 사용할 수 있게 한다.

구현 완료(2026-09-17): `ProjectHub_Setup.cmd` 단일 진입점, workstation heartbeat 선등록, `.projecthub/project.json`, 기존 문서 조건부 생성, `bin` 배포 디렉터리와 프로젝트별 Sync/Restore 런처, checkpoint 조회 API, NAS download endpoint. Restore는 별도 디렉터리에 복원하고 파일 크기와 SHA-256을 검증한다.

검증: 빈 `hw.git` clone에서 Setup으로 `hw` project 등록, 설정 생성, `bin`에 실행 파일 3개 배포, 조건부 관리 문서 생성을 확인했다. 초기 commit 후 generated Sync가 고정 manifest와 control-plane 상태를 생성했다. PowerShell 구문 검사, `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore`가 통과했다. Restore E2E는 NAS에 최신 `download.php`를 배포하고 checkpoint가 존재한 뒤 재개한다.

정책: 기존 관리 문서와 사용자 파일을 덮어쓰지 않으며 Git 변경과 대용량 업로드는 사용자가 명시적으로 실행할 때만 수행한다.
