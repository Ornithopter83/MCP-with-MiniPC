# ProjectHub 현재 작업 상태

Updated: 2026-09-18

## Baseline

- 저장소: `C:\Projects\AI-AGENTS\MCP\Server`
- 브랜치: `main` (원격 `origin/main` 추적)
- 도구체인: .NET SDK 9.0.312 확인
- 솔루션: `ProjectHub.sln`
- 활성 작업: `tasks/07-project-deployment-package.md`

## 확인된 현재 구현

- `src/ProjectHub.Core`, `Infrastructure`, `Server`, `Agent` 프로젝트가 생성됐다.
- `tests/ProjectHub.Core.Tests`, `Server.Tests` 프로젝트가 생성됐다.
- 프로젝트 참조가 Core 중심 계층으로 연결됐다.
- ASP.NET Core Server는 `/api/status`, workstation heartbeat/list, project state 수동 POST/GET API를 제공하며 Supabase REST 연결을 사용한다.
- `GPT-Web-Feedback.md`가 원격에서 추가됐으며, pull 전 확인 규칙을 `AGENTS.md`에 반영했다.
- Core 서비스 계약과 Infrastructure의 교체 가능한 NoOp 저장소 DI 등록 경계를 추가했다.
- `/api/status` 통합 테스트가 추가되어 HTTP 200과 기본 JSON 필드를 검증한다.
- `SupabaseOptions`와 named `HttpClient` 등록 경계가 추가됐으며 Server의 실제 Supabase E2E가 완료됐다.
- 04-A Supabase 설정/클라이언트 경계 구현과 빌드·테스트 검증이 완료됐다.
- `supabase/workstations.sql`과 `supabase/project-state.sql`을 Supabase에 적용하고 실제 row 저장을 확인했다.
- `IWorkstationRepository`, `SupabaseWorkstationRepository`, heartbeat upsert와 workstation 조회 API의 실제 Mini PC→Supabase E2E가 완료됐다.
- heartbeat upsert payload에서 null 메타데이터 필드를 제외하고, Supabase 오류 응답 본문을 읽어 502로 반환하도록 보완했다.
- 사용자가 Mini PC와 Supabase에서 04-F 실제 E2E를 완료했다. heartbeat upsert, 중복 방지, `last_seen` 갱신, workstation 조회, 서버 재시작 후 persistence를 확인했다.
- `supabase/project-state.sql`에 04-G 첫 단계의 `projects`와 `project_states` 최소 스키마를 정의하고 사용자가 Supabase에서 실행했다.
- `SupabaseProjectStateRepository`와 수동 project state POST/GET API를 구현했다.
- 사용자가 서버 PC에서 project state POST/GET, Supabase row 저장, 동일 project/workstation 재전송 update를 검증했다.
- `head_sha`에 실제 커밋 SHA `cebda36a4937056e9abd11254131ee42ad7afc83`가 저장된 것을 확인했다.
- `ProjectHub.Agent`가 설정 기반 heartbeat sender/runner로 구현됐으며 실제 외부 DEV PC E2E까지 완료됐다.
- 평상시 Agent에서는 대용량 hash/upload/staging/reconciliation을 수행하지 않도록 분리했다. 명시적 `ProjectHub_Sync.ps1`이 시작 시점의 size/mtime 고정 manifest를 만들고 control-plane Git/file 요약을 먼저 Server에 반영한 뒤, 별도 `ProjectHub_LargeData_Uploader.ps1`가 실제 upload/resume/finalize와 metadata checkpoint를 수행한다. uploader는 project/workstation/object hash/size 기준으로 Supabase `UPLOADING` session을 조회해 기존 session ID를 재사용하고, 없을 때만 GUID session을 생성한다.
- `GitStateCollector`가 등록된 localPath에서 branch, HEAD full SHA, dirty, changed/untracked/deleted 수를 읽기 전용으로 수집한다. 05-B 관련 테스트가 통과했다.
- `ProjectActivityMonitor`가 등록 프로젝트를 감시하고 1초 debounce 후 Git 상태를 기존 project-state API로 전송한다. 실제 DEV PC 루트 프로젝트 외부 E2E까지 검증했다.
- 실제 DEV PC 저장소 루트에서 임시 파일 생성 후 공식 HTTPS 터널 경유 Agent → Server → Supabase 상태 갱신을 확인했다. `dirty=true`, `untracked_count=1`, `last_file_activity` 갱신을 확인하고 임시 파일·설정을 복구했다.
- 임시 E2E에서 발견한 `last_file_activity` 누락을 수정해 파일 이벤트 처리 시각을 자동 기록하도록 보완했다. 빌드·테스트 재검증도 통과했다.
- Server 기본 origin을 표준 `Urls` 설정으로 `http://127.0.0.1:5240`에 고정하고, `ASPNETCORE_URLS` 또는 실행 인자로 재정의할 수 있게 했다.
- 사용자가 Cloudflare Named Tunnel `projecthub`와 `projecthub.ornithopter.bid`를 구성하고 외부 `/api/status` 성공을 확인했다. Agent 외부 E2E는 아직 검증하지 않았다.

## 목표 구조

개발 PC Agent가 heartbeat와 Git 상태를 Server에 보내고, Server의 ProjectService가 Infrastructure 저장소를 통해 Supabase에 상태·이벤트·lease를 기록한다.

## 진행

잔여 작업: Worker-B 최소 smoke test, 현재 실행 중인 작업 중지 동작 검증, 이후 Worker-D GPT Web bridge

## 작업 정책

- 한 번에 하나의 세부 작업만 수행한다.
- v0.2에서 Explorer 사용자가 명시적으로 실행한 Commit_Push/Fetch_Pull CMD에 한해 Git 변경을 수행한다. Agent 자동 실행·원격 shell·reset/checkout은 금지한다.
- 비밀값은 환경 변수로만 읽고 저장소에 기록하지 않는다.

## 표준 검증

```powershell
dotnet build ProjectHub.sln --no-restore
dotnet test ProjectHub.sln --no-restore
```

## 현재 작업

06 Large Data/NAS 기능 검증 완료 / 07 프로젝트 배포 패키지 검증 중

07 구현: Setup/Sync/Restore 배포 패키지에 이전·현재 manifest diff, REMOVED 승인 GUI, Server tombstone API, current-folder Restore의 LOCAL_ONLY 보호와 REMOVED 삭제 승인을 추가했다. `hw`는 `ProjectHub\\bin`, `ProjectHub\\config`, `ProjectHub\\state`, `ProjectHub\\log` 구조로 최신화했고 루트에는 Commit_Push/Fetch_Pull/Force_Restore 3개 진입점만 유지했다. 남은 검증은 Force Restore GUI 승인 동작 1건이다.

2026-09-17 재검증: Server `/api/status=200`, NAS Gateway `200`, GC dry-run `safe=0, keep=0, review=0`을 확인했다. 500MiB `forUpload.z01`은 .NET SHA-256 fallback으로 해시 계산 후 기존 session 재사용, NAS `already_present`, 원본과 동일한 size/hash, `STAGED`, `CHECKPOINTED`까지 성공했다. Server session은 `COMPLETED`로 확인됐다.

2026-09-18 최신 피드백 반영: `Invoke-WebRequest -InFile` 대용량 chunk 전송 회귀를 수정해 binary PUT에 `curl.exe --data-binary`를 사용하도록 변경했다. assertion cache와 401 1회 refresh는 유지하고, chunk 실패 진단에 파일·session·index·size·URI·HTTP status·예외 정보를 추가했다. `hw`에서 Explorer Sync와 동일한 경로로 500MiB 업로드를 재검증했으며 실패 0건, SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, Server `STAGED`/`COMPLETED` session/`CHECKPOINTED`를 확인했다. checkpoint commit은 `be3cff250b18ce651f1e50167a9ff8407d395197`이다.

2026-09-18 operation logging 구현 및 검증: `ProjectHub.Server` category로 주요 operation만 Information 로그를 남기고, Microsoft/ASP.NET Core/HttpClient 반복 로그는 Warning으로 제한했다. Console single-line/timestamp를 적용했다. `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore` 통과 후 로컬 Server `http://127.0.0.1:5280`에서 `SERVER_STARTED` 로그와 `/api/status=200`을 확인했다.

2026-09-18 삭제 반복 표시 원인 수정: `Removed` enum의 실제 JSON 값 `7`을 인식하도록 `Sync`를 수정하고, checkpoint 응답에서 단일 commit SHA를 명시적으로 선택하도록 보완했다. `hw` 삭제 API가 `forUpload.z01`을 tombstone 처리했고, 최신 `Sync`를 `hw\bin`에 복사해 실행한 결과 `0 large files`, `Removed=0`, `Failed=0`으로 확인했다.

2026-09-18 최신 feedback 구현 완료: `ProjectHubConsoleFormatter`와 공통 operation log helper로 최종 콘솔 형식 `yyyy-MM-dd HH:mm:ss [LEVEL] [WORKSTATION] [PROJECT] MESSAGE [STATUS]`를 적용했다. `dotnet build ProjectHub.sln --no-restore` 및 `dotnet test ProjectHub.sln --no-restore`가 모두 통과했고, 로컬 Server startup의 `SERVER_STARTED`와 `/api/status=200`을 재확인했다.

2026-09-18 NAS 삭제/Full-log 구현: Sync 승인 후 Server가 활성 object 참조 수를 확인해 NAS named alias를 삭제하고, 참조가 0일 때 canonical object까지 삭제하도록 `delete-object.php`와 delete assertion 흐름을 추가했다. 다른 활성 참조가 있으면 object를 보존한다. NAS 실패는 `PARTIAL`과 ERROR 로그로 남긴다. `ContentRoot\log\yyyyMMdd.log` 일자별 file logger와 heartbeat 포함 file-only 로그, rollover 및 정상 종료 구분선을 추가했다. C# build/test는 통과했고, local startup `/api/status=200` 및 일자별 log 파일 생성을 확인했다. 실제 NAS delete PHP 배포/운영 E2E가 다음 검증 항목이다.

2026-09-18 운영 배포 E2E 완료: 배포된 Server와 Gateway health를 확인하고, `hw` delete assertion으로 named alias 및 canonical object 삭제 성공을 확인했다. 동일 object 재호출은 `already_deleted=true`로 idempotent 동작했다.

2026-09-18 운영 로그 후속 수정: 문자열 `operation=delete` 요청이 400을 남긴 호환성 문제를 확인해 `LargeDataOperation`에 `JsonStringEnumConverter`를 적용했다. 숫자 요청 호환성을 유지하며 문자열/숫자 입력을 모두 허용한다. build/test 통과; 운영 재배포 후 최종 확인 대기.

추가 GC 검증: PowerShell 배열 응답 호환성 문제를 수정한 뒤 완료 session 2개를 개별 인식했고, dry-run은 SAFE 2개(각 524,288,000 bytes), KEEP 0, REVIEW 0으로 정상 집계됐다. `-Apply`는 실행하지 않았다.

GC 적용 검증: 사용자가 승인한 범위에서 두 SAFE session에 `-Apply`를 실행해 Gateway cleanup을 완료했고, 같은 명령을 다시 실행했을 때 두 session 모두 `ALREADY_CLEAN`으로 반환됐다. 테스트 `UPLOADING` session은 GC dry-run에서 `KEEP`으로 보호됐고, fixture 삭제 후 session count 0 및 SAFE/KEEP/REVIEW 0을 확인했다.

2026-09-17 재검증: NAS Gateway health 및 `provision.php=405`, `upload-start.php=405`는 응답했고, 일시적인 운영 Server `502` 복구 후 `/api/status=200`, GC, assertion 기반 업로드를 완료했다.

후속 개선 구현: uploader assertion을 파일/session 범위에서 캐시하고 JWT 만료 임박 또는 Gateway 401에서만 1회 refresh한다. 파일별 실패 격리, mixed batch checkpoint 보수 정책, `STAGING_CLEANED; OBJECT_RETAINED` 출력, manifest `.result.json` 결과 기록을 추가했다.

실제 NAS1DUAL 기준:

- PHP-visible storage root: `/mnt/HDD1/ProjectHub`
- 운영자가 `objects/sha256`와 `staging`을 미리 생성하고 Gateway는 root 내부만 사용
- Gateway HTTPS port: `8443`
- Gateway URL: `https://dfblackbox-nas.duckdns.org:8443/projecthub/`
- Agent는 Supabase·SMB·NAS filesystem에 직접 접근하지 않고 NAS Gateway HTTPS만 사용

완료: 05 실제 DEV PC root E2E, 06 RS256 assertion 및 NAS provision/authentication E2E

완료: 명시적 Batch Sync manifest와 별도 uploader 분리, main metadata 선반영, 기존 resumable session 재사용, chunk/status/resume/finalize → STAGED → Git checkpoint/CHECKPOINTED 경로 구현. Agent 재시작·watcher는 자동 upload/staging을 시작하지 않는다. 업로드 중 source 변경은 `CHANGED_DURING_UPLOAD`으로 checkpoint 대상에서 제외한다.

최종 검증 선행 결과: `https://suhonas.ipdisk.co.kr:8443/projecthub/`는 인증서 검증 실패(`SEC_E_WRONG_PRINCIPAL`, SNI/certificate hostname 불일치)로 정상 TLS health check가 되지 않았다. 운영 Agent에 TLS 우회는 적용하지 않으며, NAS 인증서/hostname 정리 후 upload E2E를 재개한다.

2026-09-18 사용자 진입점 UX 보완: `ProjectHub_Setup.cmd`, `ProjectHub_Sync.cmd`, `ProjectHub_Restore.cmd`, `ProjectHub_GC.cmd`, `ProjectHub_update.cmd`, `ProjectHub_Agent_Test.cmd`가 성공·실패와 무관하게 종료 코드를 출력하고 `pause` 후 동일 종료 코드를 반환하도록 통일했다. `ProjectHub_update.cmd`는 콘솔을 `220x50`으로 설정해 진행 로그가 잘리지 않도록 했다. 누락된 Agent 스크립트 오류 경로도 동일한 종료 코드/일시정지 형식을 사용한다. `git diff --check` 통과; 실제 배포 PC 반영 및 Explorer 더블클릭 E2E가 후속 검증 대상이다.

2026-09-18 v0.2 정책 전환: 사용자가 Explorer에서 명시적으로 실행하는 `ProjectHub_Commit_Push.cmd`는 add/commit/fetch/pull --rebase/push 후 Sync 및 large-data checkpoint를 실행하고, `ProjectHub_Fetch_Pull.cmd`는 clean 상태 확인 후 fetch/pull --rebase와 Restore를 실행한다. detached HEAD, dirty pull 대상, merge/rebase 진행, 충돌, push reject는 자동 해결하지 않고 중단한다. 기존 Sync/Restore CMD는 호환성을 위해 유지한다.

2026-09-18 강제 복구 구현: `ProjectHub_Force_Restore.cmd`와 `ProjectHub_Force_Restore.ps1`를 추가했다. 실행 전 `FORCE` 명시 입력이 없으면 종료하며, 승인 후에만 `fetch origin` → `reset --hard origin/<branch>` → `clean -fd`를 수행한다. `.projecthub/project.json`, ProjectHub CMD, `bin\ProjectHub_*.ps1`는 임시 보관 후 복원하고, 이후 최신 checkpoint 기준 기존 Restore 엔진으로 NAS 대용량 파일과 REMOVED 파일을 처리한다. 파괴적 실행·NAS 복구 E2E가 후속 검증 대상이다.

2026-09-18 hw 순차 검증: 최신 ProjectHub 파일을 `hw`에 배포하고 일반 Fetch_Pull dirty 보호(exit 2)를 확인했다. 강제 복구는 `FORCE` 승인 후 Git `fetch/reset --hard/clean -fd`와 500MiB 로컬 삭제까지 성공했다. tombstone 재등록 후 500MiB 업로드는 원본 SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, `ALREADY_PRESENT`, `STAGED`, checkpoint `be3cff250b18ce651f1e50167a9ff8407d395197`로 완료했다. 이후 `.git`, `.projecthub`, ProjectHub 런처·엔진만 남기고 테스트 파일/솔루션/대용량 파일을 삭제했다. 최종 Force Restore의 Git 복구는 성공했지만 NAS Restore는 `RESTORE_SIZE_MISMATCH: forUpload.z01`로 실패해 원상복구 E2E는 미완료다. 업로드 중복 실행 시 임시 chunk 경합이 발생했으나 중복 프로세스 종료 후 단일 uploader 재시도로 성공했다.

2026-09-18 최신 피드백 구현: `download.php`가 공통 canonical object 경로를 사용하고 `object_not_found`, `object_size_mismatch`, `object_not_readable`, `object_read_failed`를 구분해 로그/응답하도록 보강했다. Restore는 전체 파일 PREPARE(임시 다운로드·size/SHA 검증) 후 APPLY하며, 완료 전에 `RESTORE_VERIFY expected/matched/mismatched/missing`을 출력하고 불일치 시 실패한다. Force Restore도 Restore 결과의 mismatch/missing 0을 확인한다. Setup은 새 프로젝트의 ProjectHub 전용 폴더 구조를 생성하고 루트 3개 진입점을 ProjectHub\bin 엔진으로 연결한다. PHP lint는 개발 PC에 PHP가 없어 실행하지 못했으며, 수정 download.php의 NAS 배포 후 단독 API와 Restore E2E가 남았다.

2026-09-18 download/restore 재검증 완료: NAS 실제 canonical object `S:\HDD1\ProjectHub\objects\sha256\e9\e93ac6ff...e8267`는 524,288,000 bytes였다. 수정 `download.php`를 `S:\HDD1\DocRoot\projecthub`에 배포한 뒤 assertion 단독 호출이 HTTP 200, Content-Length 524,288,000, fopen preflight `open-ok`로 응답했다. `hw\ProjectHub_Restore.cmd`는 500MiB를 다운로드하고 `RESTORE_VERIFY matched=1, mismatched=0, missing=0`으로 완료했으며, 로컬 파일 크기·SHA-256도 원본과 일치했다. 이전 HTTP 500은 NAS 웹 루트의 구버전 download.php와 readfile 처리 문제였다.
2026-09-18 Restore 다운로드 UX 보완: `ProjectHub_Restore.ps1`을 HttpClient 스트림 수신 방식으로 변경해 업로드와 같은 콘솔에서 `DOWNLOAD`, 퍼센트, 수신/전체 바이트, 완료 로그를 표시한다. PREPARE/APPLY 검증과 완료 후 `pause`는 유지한다.
2026-09-18 Force Restore UX/배포 구조 보완: `ProjectHub_Force_Restore.ps1`의 파괴적 실행 승인을 콘솔 `FORCE` 문자열 입력에서 Windows 확인 대화상자의 `계속`/`취소` 선택으로 변경했다. 새 `ProjectHub\bin` 구조를 우선 사용하고 구형 루트 `bin`은 호환 fallback으로 유지한다.

2026-09-18 문서 정정 및 `hw` 최신화: `hw`에서 구형 `.projecthub\`, 루트 `bin\`, 구형 Setup/Sync/Restore 진입점을 제거하고 `ProjectHub\bin`, `ProjectHub\config`, `ProjectHub\state`, `ProjectHub\log` 구조로 통합했다. 루트 사용자 진입점은 `ProjectHub_Commit_Push.cmd`, `ProjectHub_Fetch_Pull.cmd`, `ProjectHub_Force_Restore.cmd`만 유지했다. 500MiB `forUpload.z01`은 `.gitignore`로 커밋에서 제외하고 로컬 테스트 파일로 보존했다. `ProjectHub_Commit_Push.cmd` 실행 결과 커밋 `8dd5c80081929da8957d32938ea5aa58c064f252`를 `origin/main`에 push했고, 후속 Sync/uploader는 `checkpointed=true`로 완료했다.

문서 불일치 정정: 이전 기록의 `FORCE` 콘솔 입력, `hw\ProjectHub_Restore.cmd` 실행, 구형 `.projecthub/bin` 구조, Restore 미완료 표시는 변경 전 상태를 기록한 이력이다. 현재 Force Restore는 GUI `계속`/`취소` 승인 창을 사용하고 Restore는 `ProjectHub\bin\ProjectHub_Restore.ps1` 엔진을 사용한다. 실제 남은 검증은 Force Restore GUI 승인 창의 계속/취소 동작 확인 1건이다. Commit_Push 루트 CMD는 종료 전 `pause`를 수행하지만, 대용량 uploader는 별도 프로세스로 실행되므로 그 창의 Enter 대기 여부는 별도 개선·검증 항목으로 분리한다.

Gateway URL 변경 확인: `https://dfblackbox-nas.duckdns.org:8443/projecthub/` health `200` 및 JSON 응답 성공, `provision.php` GET은 `405`로 method 경계가 정상이다. 인증 없는 POST 응답 본문 확인은 PowerShell의 예외 응답 형식 차이로 별도 Gateway 클라이언트 검증에서 수행한다.

NAS upload 구현: `nas-gateway/upload-start.php`, `upload-chunk.php`, `upload-status.php`, `upload-finalize.php`를 추가했다. 로컬 PHP 파일은 실제 NAS 배포 후 운영 assertion으로 검증해야 하며, 현재 원격 upload 경로는 아직 배포되지 않아 HTML 응답을 반환한다.

NAS 배포 후 재검증: health `200`, 기존 `provision.php` GET `405`, upload-start GET `405`, upload-status GET 및 upload-start POST(Authorization 없음) `401 upload_session_required`를 확인했다. 운영 Server `https://projecthub.ornithopter.bid`는 같은 시각 `/api/status`와 assertion 발급 모두 Cloudflare `502`였으므로 운영 assertion 기반 upload E2E는 Server 복구 후 재개한다.

Server 재기동 후 재검증: `/api/status`는 `200`으로 복구됐으나 운영 assertion 발급은 `503 PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM is not configured`로 실패했다. NAS Gateway까지의 인증 upload E2E는 Server PC에 private key 환경 변수를 설정하고 재기동한 뒤 재개한다.

Server 실행 스크립트 `ProjectHub_Server_Test.ps1`를 추가했다. `C:\AI-Server\ProjectHub\src\ProjectHub.Server\projecthub-private.pem`을 `GetContent -Raw`와 동일한 `[IO.File]::ReadAllText()` 방식으로 읽어 PEM 개행을 보존하고, `PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM` process 환경 변수로만 주입한다.

NAS upload 최종 재검증: 운영 Server assertion 발급 성공 후 NAS `upload-start.php` → `upload-chunk.php` → `upload-status.php` → `upload-finalize.php`를 실제 실행했다. 11바이트 테스트 객체에서 chunk 수신 `11`, status `completed_chunks=[0]`, finalize `complete`, SHA-256 object 생성과 동일 hash 재업로드 `already_present=true` dedup을 확인했다.

새 Server 세션 재검증: `/api/status=ok`, 운영 assertion 발급 성공, 17바이트 객체에 대해 upload-start → chunk(`17`) → status(`bytes_received=17`) → finalize(`complete`)와 SHA-256/size 일치를 확인했다.

500MiB 실제 파일 `forUpload.z01` 검증: SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, 16MiB chunk 32개가 NAS staging에 정확히 수신되어 `bytes_received=524288000`을 확인했다. NAS finalize는 `size_mismatch`를 반환해 object 확정이 보류됐고, finalize 직후 `clearstatcache`를 추가해 보정했다. 보정 파일 재배포 후 동일 세션 finalize를 재시도한다.

500MiB 최종 재검증: 기존 session에 새 assertion으로 finalize를 재시도한 뒤 session은 정리됐고, 동일 hash `upload-start`에서 `already_present=true`, `size_bytes=524288000`을 확인했다. object 확정 및 실제 파일 크기 검증이 완료됐으며, hash는 finalize 대상 경로와 원본 SHA-256이 일치한다.

06 후속 연결 구현: Agent startup 대용량 inventory를 Server reconciliation API로 전송하고, Server에 STAGED metadata 및 명시적 commit SHA 기반 CHECKPOINTED dataset API를 추가했다. Git 자동 변경은 수행하지 않는다.

06 한계 기록(2026-09-17): `Sync`는 현재 존재하는 대용량 파일만 manifest로 수집하며 이전 manifest와 비교해 로컬에서 삭제된 파일을 NAS/Supabase 삭제 대상으로 추적하지 않는다. `ProjectHub_GC.ps1`는 TTL/lifecycle 기준의 upload session staging 정리만 담당하고 canonical NAS object 및 `large_objects`·`project_large_files`·`large_data_sets` metadata를 자동 삭제하지 않는다. NAS-only orphan은 Server API로 열거하지 못하므로 관리페이지/별도 관리 절차가 필요하다.

## 2026-09-19 Worker-A UI skeleton

최신 `GPT-Web-Feedback.md`의 Worker-A 지시에 따라 `src/ProjectHub.Worker` WPF 데스크톱 프로젝트를 추가했다. 제공된 Worker 대시보드 이미지를 기준으로 1024x768 메인 화면, Header/model/reasoning selector, Project/GPT Web/Server/System 상태 카드, 통합 Current Task/Task Flow, Codex/GPT Web 결과 탭, 자연어 Command 입력, Clear/Run Task 버튼, Windows tray 숨김/Open/Pause/Exit 기본 동작을 구현했다. Run Task와 결과 전환은 현재 mock 상태이며 Codex CLI, persistent state, Git, GPTWeb-Hub bridge는 후속 Worker-B~E 범위다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

잔여 작업: Worker-B — Codex CLI 자동 탐색·실행·결과 수집.

2026-09-19 Worker-A UI refinement: 메인 창을 세로 980px 기준으로 확장하고 Last Result/Command 영역의 사용 공간을 늘렸다. Header의 Settings/Minimize/Close/READY 배치를 분리해 겹침을 제거했고, Model/Reasoning 선택 컨트롤의 크기·색·여백을 정리했다. Current Task의 방향 문구, 상태 설명, Codex/Worker/GPT Web 단계 아이콘을 확대했다. 샘플 명령/최근 명령 버튼과 하단 설명은 제거했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 2: CURRENT TASK 행 높이를 줄이고 LAST RESULT 영역을 확장했다. Codex/GPT Web 토글 버튼과 결과 제목·시간·본문의 글자 크기를 키우고 결과 카드 여백을 확대했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0). XAML 변경 범위이므로 기존 전체 테스트 통과 상태를 유지한다.

2026-09-19 Worker-A UI refinement 3: 실제 실행 화면에서 Run/Clear 버튼이 보이도록 창 높이를 1120px, Command/Last Result 행을 각각 300px로 확장했다. Windows 기본 제목 표시줄과 중복되던 사용자 정의 최소화/닫기 버튼을 제거하고 Settings 아이콘과 READY 표시를 별도 열에 배치했다. Model/Reasoning 콤보박스의 화살표 토글을 수직·수평 중앙 정렬했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

잔여 작업: Worker-B — Codex CLI 자동 탐색·실행·결과 수집.

2026-09-19 Worker-A UI refinement 4: 실행 화면에서 Command 하단 Run/Clear 버튼이 완전히 보이도록 창 높이를 1260px, Command 행을 340px로 확장했다. 설정 아이콘 오른쪽의 헤더 READY 표시와 사용하지 않는 FooterStatus 상태 표시·갱신 로직을 제거했고, 연결되지 않은 상태/선택 핸들러도 정리했다. 트레이 Exit에 필요한 종료 상태만 유지했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

잔여 작업: Worker-B — Codex CLI 자동 탐색·실행·결과 수집.

2026-09-19 Worker-A UI refinement 5: 첨부 실행 화면 기준으로 상단 헤더 행을 76px, 연결 상태 카드 행을 122px로 축소해 카드 아래의 남는 공간을 줄였다. 하단 Command 공간은 유지했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 6: 사용자 의도에 맞춰 헤더 행은 이전 86px로 복구하고 연결 카드 행 축소 122px만 유지했다. 카드 내부 경로·상태 문구가 잘리지 않도록 창 폭을 1200px, 최소 폭을 1100px로 확장했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 7: Worker 헤더의 PC명을 `SUHO_DEV_PC`로 변경하고 실행 상태 설명 문구를 제거했다. 연결 카드에서 System 카드를 삭제하고 3열로 변경했다. 첫 카드는 Codex / `CODEX - MCP-with-MiniPC`, 두 번째는 GPT Web / `GPT Web - MCP 프로젝트 진척도 확인`, 세 번째 서버 카드는 `MINIPC_SERVER` 닉네임을 표시한다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 8: 연결 카드의 두 번째 줄에서 제목 중복을 제거하고 본문만 표시하도록 변경했다. Model/Reasoning 선택기를 상단에서 제거해 Command 하단 우측, Clear/Run 버튼 왼쪽에 여백을 두고 배치했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A UI refinement 9: Command 하단의 Model/Reasoning 선택기와 Clear 버튼 사이 간격만 추가 조정했다. Reasoning 열을 152px로 확보하고 오른쪽 여백을 16px로 설정해 버튼과 시각적으로 분리했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A icon refinement: 제공된 `worker아이콘.png`를 기반으로 128px 헤더용 PNG와 256px PNG-embedded ICO를 생성했다. WPF 헤더 이미지, Windows 실행 파일 ApplicationIcon, tray NotifyIcon에 연결했으며 원본은 프로젝트 밖에 보존했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A Current Task icon refinement: 사용자 지정 매핑에 따라 CODEX에는 OpenAI 아이콘, WORKER에는 콘솔 아이콘, GPT WEB에는 웹 아이콘을 96px 투명 PNG로 리사이징해 적용했다. 각 아이콘은 66px 원형 단계 표시 안에 배치했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-A Current Task flow refinement: 단계 사이 진행 표시를 `>>>` 화살표로 교체하고 우측 이동·점멸 애니메이션을 추가했다. CODEX/WORKER/GPT WEB은 컬러·그레이스케일 아이콘을 겹쳐 비활성 단계는 회색으로 표시하고, mock task 실행 시 Codex → Worker → GPT Web 순으로 활성 아이콘이 전환되도록 구현했다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개).

2026-09-19 Worker-B 1차: `CodexCliRunner`를 추가해 PATH 및 `%LOCALAPPDATA%\OpenAI\Codex\bin` 하위에서 `codex.exe`를 자동 탐색하고, 선택 모델·추론값을 적용한 `codex exec --json`을 비동기로 실행하도록 연결했다. stdout/stderr, exit code, 실행 시간, output-last-message 결과를 수집하며 Run 버튼은 실행 중 Cancel로 전환되고 취소 시 프로세스 트리를 종료한다. Codex 결과는 Last Result의 Codex 탭과 Current Task 상태에 반영한다. GPT Web bridge는 Worker-D 전까지 mock 유지.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-build --no-restore` 성공(Core 1개, Agent 3개, Server 1개). 로컬 Codex CLI `codex-cli 0.155.0-alpha.9.2` 및 `codex exec --help` 확인.

잔여 작업: Worker-B 2차 — JSON event/token usage/session id 파싱 강화 및 실제 CLI 실행 smoke test.

2026-09-19 Worker-A Current Task animation refinement: 진행 화살표를 개별 TextBlock 3개로 분리하고 150ms 타이머로 `>.. → >>. → >>> → .>> → ..>` 5단계 순차 점등을 구현했다. 활성 흐름만 컬러로 애니메이션하며 비활성 흐름은 회색 고정이고, Window 종료 시 타이머를 중지한다.

검증: `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore -p:OutDir=C:\Users\ornit\AppData\Local\Temp\projecthub-worker-build\ -p:UseAppHost=false` 성공(경고 0, 오류 0). 전체 솔루션 빌드는 실행 중인 `ProjectHub.Worker (PID 43396)`가 기존 DLL을 잠가 실패했으며, 코드 컴파일 오류는 확인되지 않았다.

2026-09-19 Worker 단일 인스턴스 개선: named mutex로 Worker 중복 실행을 차단하고, 두 번째 실행 요청이 들어오면 기존 프로세스에 named event를 보내 기존 창을 복원·활성화하도록 변경했다. StartupUri를 명시적 창 생성으로 전환해 시작 순서를 제어했다.

검증: `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore -p:OutDir=C:\Users\ornit\AppData\Local\Temp\projecthub-worker-build-single-instance\ -p:UseAppHost=false` 성공(경고 0, 오류 0).

2026-09-19 작업 범위 재분석 및 중지 정책 정정: 기존 잔여 작업에 적힌 `Force Restore GUI 계속/취소 검증`은 현재 GPTWeb-Hub Worker 작업과 무관하므로 이번 작업의 잔여 항목에서 제외한다. 잔여 작업 3번은 `현재 실행 중인 작업 중지` 기능이며, 이는 개발 전체를 중단한다는 뜻이 아니다. 실행 중인 작업을 중지할 때는 이미 진행된 변경을 억지로 되돌리지 않는다. 작업 시작 전 저장소가 최신 동기화 상태가 아니면 이전 작업이 삭제될 수 있으므로, 새 작업 시작 전 최신 pull/rebase와 `GPT-Web-Feedback.md` 확인을 우선한다.

현재 잔여 작업 재정의:
- Worker-B 2차는 최소 범위로 유지한다: Codex CLI 1회 실제 실행 smoke test와 성공/실패/취소 결과 확인만 수행한다. JSON event/token usage/session ID 확장은 후속 선택 사항으로 둔다.
- GPT Web bridge는 Worker-D 범위이며 현재는 mock 유지한다.
- 현재 실행 중인 작업을 중지해도 이미 진행된 변경은 유지하며 억지로 revert하지 않는다. 개발 작업 자체는 계속한다.
2026-09-19 Worker-B 최소 smoke test 및 현재 작업 중지 검증: 로컬 `codex-cli 0.155.0-alpha.9.2`를 프로젝트 루트에서 파일 변경 금지 프롬프트로 1회 실행했다. `EXIT_CODE=0`, `SMOKE_OK`를 확인했다. 현재 작업 중지는 Run 버튼의 Cancel 전환 → CancellationToken 취소 → Codex 프로세스 트리 종료 → `Codex CANCELED` 표시 흐름으로 동작하며, 완료된 변경을 되돌리는 로직은 없다. Worker 단독 컴파일도 경고 0/오류 0으로 통과했다.

현재 남은 작업: GPT Web bridge(Worker-D 후속 범위). 현재 실행 중인 작업 중지 기능은 구현·코드 검증 완료 상태이며 실제 UI 클릭 검증은 별도 확인 사항이다.

2026-09-19 Worker-D bridge 1차: Worker에 `127.0.0.1:43821` loopback HTTP bridge를 추가했다. status/project list/conversation binding/pending task/create/claim/result/heartbeat API를 제공하고 `%LOCALAPPDATA%\ProjectHub\Worker\bridge-state.json`에 binding·task 상태를 원자적으로 저장한다. Chrome 확장은 1.5초 polling으로 bridge 상태·프로젝트·task를 표시하며, bridge가 끊기면 Disconnected 상태를 표시한다. 외부 LAN bind와 ChatGPT DOM 자동입력은 구현하지 않았다.

검증: `node --check extension/gptweb-hub/content.js` 성공, Worker 단독 빌드 성공(경고 0, 오류 0). Worker DLL에서 bridge를 별도 호스트로 실행해 `/bridge/status`, `/bridge/projects`, `/bridge/bind`, `/bridge/bindings/{conversationId}`를 호출했고 `BRIDGE=ready`, `LOOPBACK=True`, `PROJECT=MCP-with-MiniPC`, `BOUND=True`를 확인했다.

현재 남은 작업: Chrome에서 확장을 실제 로드한 뒤 polling 화면을 확인하는 UI E2E 1건. GPT Web DOM 자동입력·응답 제출은 별도 후속 범위.

2026-09-19 GPTWeb-Hub 확장 상태 행 정리: Project/Worker/Web 상태 텍스트가 실제 연결 상태를 표시하므로 우측 `READY` 점·문구를 제거했다. `node --check extension/gptweb-hub/content.js` 통과.

2026-09-19 GPTWeb-Hub 상태 색상 개선: 확장 상태 텍스트에 정상(`status-ok`, 녹색), 연결 끊김(`status-offline`, 적색), 대기(`status-pending`, 주황색) 스타일을 추가하고 bridge polling 결과에 따라 자동 갱신하도록 변경했다. `node --check extension/gptweb-hub/content.js` 통과.

2026-09-19 확장 bridge 진단 보완: bridge 연결 전에도 Project/Worker는 주황색 대기, Web은 빨간색 연결 끊김으로 즉시 표시하도록 초기 상태 색상 적용을 추가했다. `node --check extension/gptweb-hub/content.js` 통과. 확장 reload만으로 기존 ChatGPT 탭의 content script가 교체되지 않으므로 탭 새로고침이 필요하다.

2026-09-19 상태 색상 미적용 원인 수정: `setStatusValue`는 정상 동작했지만 `statusRow` 생성부의 실제 span에 `status-value` class가 누락되어 CSS가 적용되지 않았다. 해당 class를 추가하고 `node --check extension/gptweb-hub/content.js`를 통과했다.

2026-09-19 Worker 실행 문제 수정: 사용자 세션에 남아 있던 구버전 PID 8812를 종료하고 최신 소스로 실제 `bin\Debug\net9.0-windows`를 재빌드했다. 최신 Worker PID 21844가 `ProjectHub Worker` 창으로 응답하며 `127.0.0.1:43821/bridge/status`에서 `bridge=ready`, `loopback=true`를 확인했다. bridge 시작 실패가 UI 전체 종료로 이어지지 않도록 App 시작 예외를 기록하고 Worker UI는 계속 표시하는 보호 로직을 추가했다.

검증: `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore` 성공(경고 0, 오류 0), localhost bridge status 성공.

2026-09-19 Worker 재실행 문제 수정: 첫 실행 후 창이 사라져도 activation 대기 Task가 무기한 `WaitOne()`에 남아 숨은 프로세스가 종료되지 않는 경로를 확인했다. activation event를 250ms timeout polling으로 바꿔 CancellationToken을 확인하고, 종료 시 대기 루프가 남지 않도록 수정했다. 기존 숨은 PID 21844를 종료한 뒤 최신 빌드로 1차 실행 및 즉시 2차 실행을 수행했으며 두 번 모두 동일 PID 단일 인스턴스와 `bridge=ready`, `loopback=true`를 확인했다.

검증: `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore` 성공(경고 0, 오류 0), 1차·2차 실행 bridge status 성공, 최신 Worker 창 제목 `ProjectHub Worker` 확인.

2026-09-19 Worker 트레이 재실행 흐름 정정: X 버튼은 실제 종료하지 않고 `Hide()`로 트레이 상태를 유지하도록 복원했다. 기존 백그라운드 대기 Task 대신 WPF `DispatcherTimer`가 250ms마다 named activation event를 확인해 숨은 창을 복원·활성화한다. 트레이 `Exit`에서만 `_allowClose`를 통해 bridge·tray icon·mutex를 정리한다.

검증: 최신 Worker 빌드 성공(경고 0/오류 0). 1차 실행에서 창 handle `790082`와 bridge ready를 확인하고 X 버튼으로 숨긴 뒤에도 동일 PID `45672`와 bridge ready를 확인했다. 2차 실행 후 동일 PID의 창 handle `790082`가 복원되고 bridge ready가 유지됐다.

2026-09-19 3초/10초 재실행 시나리오 및 트레이 종료 보강: 강제 종료 방식의 `3초 실행 → 종료 → 10초 대기 → 재실행`에서는 기존 문제를 재현하지 못했고 2차 bridge도 정상 확인했다. 실제 트레이 Exit 경로의 종료 보장을 위해 `Application.Current.Shutdown()`을 사용하고, `OnExit`에서 bridge/activation event 정리 실패가 mutex 해제를 건너뛰지 않도록 finally 정리를 추가했다. X 버튼의 트레이 숨김 동작과 재실행 복원은 유지한다.

검증: Worker 빌드 성공(경고 0/오류 0), 최신 Worker PID 25196 창 표시 및 bridge `ready` 확인.

2026-09-19 Worker 실제 shutdown 재검증 및 잔류 프로세스 수정: 강제 종료가 아닌 WPF `Application.Shutdown()` 경로에서 브리지는 닫히지만 PID가 남는 결함을 재현했다. 원인은 X 버튼의 트레이화 Closing 처리와 실제 종료 요청이 같은 경로에서 취소될 수 있었던 점과 Windows Forms 트레이 자원 종료 보장이 부족했던 점이다. App에 명시적 `RequestShutdown()` 상태를 두고 트레이 Exit가 이를 사용하도록 했으며, 종료 중 Closing은 숨김으로 취소하지 않도록 변경하고 ContextMenuStrip/NotifyIcon을 정리한 뒤 프로세스 종료를 보장한다. 테스트 전용 shutdown 옵션과 분리 출력 폴더는 제거했다.

검증: 분리 출력에서 최신 수정본으로 `3초 실행 → 실제 shutdown → 4초 후 확인` 수행 결과 `BRIDGE_EARLY=200`, `LATE=False`, `BRIDGE_LATE=UNREACHABLE`. 최종 일반 빌드 `dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore` 성공(경고 0, 오류 0).

2026-09-19 GPTWeb-Hub 상태 행 명칭 정정: 세 번째 행은 웹 연결 대상명이 아니라 현재 연결 상태를 표시하므로 라벨을 `Web`에서 `Status`로 변경했다. 상태값과 녹색/적색 색상 갱신 로직은 유지했다.

검증: `node --check extension/gptweb-hub/content.js` 성공.

2026-09-19 GPTWeb-Hub 상태 색상 의미 정정: Project/Worker는 bridge에서 실제 값이 지정된 경우에만 녹색으로 표시하고, 아직 값이 없을 때만 대기 상태를 표시하도록 변경했다. bridge 연결이 끊겨도 마지막으로 확인된 Project/Worker 값과 녹색 상태는 유지하며, Status 행만 빨간색 Disconnected로 전환한다.

검증: node --check extension/gptweb-hub/content.js 성공.

2026-09-19 최신 GPT-Web-Feedback Settings 기능 구현: 원격 피드백을 fetch/pull --rebase로 동기화한 뒤, 확장 톱니바퀴의 mock 상태 순환을 제거하고 실제 Settings modal을 추가했다. Host/Port/BasePath/선택적 Worker Path 입력, loopback 검증, chrome.storage.local 저장·복원, Test Connection, Save 즉시 polling URL 전환, Cancel을 구현했다. Project/Worker/Status 상태는 저장된 bridge 설정을 사용한다. Worker bridge task endpoint는 최신 task와 terminal 상태를 반환하고 확장 CURRENT REQUEST는 실제 PENDING/CLAIMED/COMPLETED/FAILED 상태에서 파생된다.

검증: node --check extension/gptweb-hub/content.js 성공, dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore 성공(경고 0, 오류 0).

2026-09-19 GPTWeb-Hub SPA navigation refresh 보완: 전체 페이지 새로고침이 아닌 ChatGPT 대화 이동은 content script가 재실행되지 않아 패널과 CURRENT REQUEST가 유지되는 문제를 확인했다. history.pushState/replaceState, popstate, hashchange를 감지해 URL 변경 시 task 상태를 IDLE로 초기화하고 Worker bridge를 즉시 재조회하도록 보완했다. 대화별 binding 식별·복원은 아직 후속 범위다.

검증: node --check extension/gptweb-hub/content.js 성공.

2026-09-19 Conversation binding 구현: ChatGPT URL의 conversation ID를 식별하고 Worker의 binding 조회/저장 API와 연결했다. Project가 연결되지 않은 대화는 Project 행에 연결 버튼을 표시하며, 현재 Worker project를 POST /bridge/bind로 저장한다. 페이지 이동·새로고침 후 binding을 다시 조회해 연결된 Project를 녹색으로 복원한다. task 조회에도 conversationId 필터를 적용해 다른 대화의 task가 섞이지 않도록 했다.

검증: node --check extension/gptweb-hub/content.js 성공, dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore 성공(경고 0, 오류 0), git diff --check 통과.


2026-09-19 GPTWeb-Hub 표현·연동 의미 정리: 확장 첫 상태 행을 Project에서 GPT Web으로 변경하고 현재 ChatGPT 대화 제목을 표시하도록 했다. 기존 대화-Worker 프로젝트 바인딩은 GPT Web 행의 연결 동작으로 유지했다. Worker 행은 bridge의 repository 값(저장소명)을 표시하고 Status 행만 bridge 연결 상태를 표시한다. Worker 헤더에는 메인 저장소명 MCP-with-MiniPC을 제목 옆에 표시하고, Codex 카드의 두 번째 줄은 실행 요청 첫 줄을 Codex 대화 제목으로 표시하도록 연결했다.

검증: node --check extension/gptweb-hub/content.js 성공, dotnet build src/ProjectHub.Worker/ProjectHub.Worker.csproj --no-restore 성공(경고 0, 오류 0), git diff --check 통과.


2026-09-19 GPTWeb-Hub 테스트 전송 버튼: 현재 ChatGPT 대화 ID가 식별된 경우 패널의 테스트 전송 버튼으로 Hello World! 테스트 문구니까 인사만 짧게 해줘를 실제 ChatGPT 입력창에 주입하고 전송하도록 구현했다. 대화방·입력창·전송 버튼 미검출 상태를 사용자에게 표시하며, 대화 이동 시 버튼 활성 상태를 갱신한다.

검증: node --check extension/gptweb-hub/content.js 성공.


2026-09-19 GPTWeb-Hub 대화 표시 개행: ChatGPT 페이지 제목이 프로젝트명 - 대화방명 형식이면 GPT Web 상태값을 두 줄로 표시하도록 수정했다.

검증: node --check extension/gptweb-hub/content.js 성공.


2026-09-19 GPTWeb-Hub 이미지 테스트 전송: 테스트 버튼 문구를 이미지가 무엇인지 짧게만 대답해줘로 변경하고, 확장 내부 Canvas에서 생성한 gptweb-hub-test.png를 File/DataTransfer로 ChatGPT file input 또는 paste/drop 경로에 첨부한 뒤 기존 전송 버튼으로 함께 전송하도록 구현했다. 별도 로컬 파일 권한은 사용하지 않는다.

검증: node --check extension/gptweb-hub/content.js 성공.


2026-09-19 GPTWeb-Hub 요청·수신 연동: 테스트 전송 버튼을 제거하고 CURRENT REQUEST가 IDLE일 때만 텍스트 입력, 전송, 파일 드롭을 활성화했다. 드롭 파일은 최종 파일명만 표시하며 전송 시 ChatGPT 입력창에 첨부한다. ChatGPT DOM의 최신 assistant 메시지를 MutationObserver로 감시해 하단 RESULT MESSAGE에 갱신한다. PENDING/CLAIMED/COMPLETED/FAILED 상태에서는 입력과 드롭을 비활성화한다.

검증: node --check extension/gptweb-hub/content.js 성공, git diff --check 통과.


2026-09-19 GPTWeb-Hub 요청·수신 UI 정리: 테스트 전송 버튼과 전용 이미지 생성 코드를 제거했다. CURRENT REQUEST가 IDLE이고 대화가 식별된 경우에만 텍스트 입력·전송·파일 드롭을 활성화하며, 드롭 파일은 파일명만 표시하고 일반 파일 첨부 경로로 ChatGPT에 전달한다. 전송 후 새 assistant 응답이 감지될 때까지 컨트롤을 잠그고, 최신 assistant 메시지를 RESULT MESSAGE에 갱신한다.

검증: node --check extension/gptweb-hub/content.js 성공, git diff --check 통과.
