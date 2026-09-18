# ProjectHub 현재 작업 상태

Updated: 2026-09-17

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

잔여 작업: 새 Server 삭제/tombstone API 운영 배포 후 `hw` 탐색기 더블클릭 Sync·Restore E2E, 이후 08 Server 설치·이전

## 작업 정책

- 한 번에 하나의 세부 작업만 수행한다.
- 자동 Git 변경·원격 명령·파일 삭제는 금지한다.
- 비밀값은 환경 변수로만 읽고 저장소에 기록하지 않는다.

## 표준 검증

```powershell
dotnet build ProjectHub.sln --no-restore
dotnet test ProjectHub.sln --no-restore
```

## 현재 작업

06 Large Data/NAS 기능 검증 완료 / 07 프로젝트 배포 패키지 검증 중

07 구현: Setup/Sync/Restore 배포 패키지에 이전·현재 manifest diff, REMOVED 승인 GUI, Server tombstone API, current-folder Restore의 LOCAL_ONLY 보호와 REMOVED 삭제 승인을 추가했다. `hw`에는 최신 `bin` 실행본을 반영했다. Server 새 바이너리 운영 배포 후 탐색기 더블클릭 기준 삭제 Sync와 Restore E2E가 남아 있다.

2026-09-17 재검증: Server `/api/status=200`, NAS Gateway `200`, GC dry-run `safe=0, keep=0, review=0`을 확인했다. 500MiB `forUpload.z01`은 .NET SHA-256 fallback으로 해시 계산 후 기존 session 재사용, NAS `already_present`, 원본과 동일한 size/hash, `STAGED`, `CHECKPOINTED`까지 성공했다. Server session은 `COMPLETED`로 확인됐다.

2026-09-18 최신 피드백 반영: `Invoke-WebRequest -InFile` 대용량 chunk 전송 회귀를 수정해 binary PUT에 `curl.exe --data-binary`를 사용하도록 변경했다. assertion cache와 401 1회 refresh는 유지하고, chunk 실패 진단에 파일·session·index·size·URI·HTTP status·예외 정보를 추가했다. `hw`에서 Explorer Sync와 동일한 경로로 500MiB 업로드를 재검증했으며 실패 0건, SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267`, Server `STAGED`/`COMPLETED` session/`CHECKPOINTED`를 확인했다. checkpoint commit은 `be3cff250b18ce651f1e50167a9ff8407d395197`이다.

2026-09-18 operation logging 구현 및 검증: `ProjectHub.Server` category로 주요 operation만 Information 로그를 남기고, Microsoft/ASP.NET Core/HttpClient 반복 로그는 Warning으로 제한했다. Console single-line/timestamp를 적용했다. `dotnet build ProjectHub.sln --no-restore`, `dotnet test ProjectHub.sln --no-restore` 통과 후 로컬 Server `http://127.0.0.1:5280`에서 `SERVER_STARTED` 로그와 `/api/status=200`을 확인했다.

2026-09-18 삭제 반복 표시 원인 수정: `Removed` enum의 실제 JSON 값 `7`을 인식하도록 `Sync`를 수정하고, checkpoint 응답에서 단일 commit SHA를 명시적으로 선택하도록 보완했다. `hw` 삭제 API가 `forUpload.z01`을 tombstone 처리했고, 최신 `Sync`를 `hw\bin`에 복사해 실행한 결과 `0 large files`, `Removed=0`, `Failed=0`으로 확인했다.

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
