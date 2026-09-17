# Large Data/NAS

## 목표

ProjectHub-native assertion과 NAS Gateway 계약으로 대용량 파일을 Server가 중계하지 않고 안전하게 저장·검증한다.

## 세부 작업

### A. 계약·보안 경계와 구현 구조 (완료: 2026-09-16)

ProjectHub.Server assertion issuer, ProjectHub NAS Gateway verifier/provision, content-addressed storage, resumable upload의 계약과 환경 변수 경계를 확정한다.

`ProjectHub.Core/LargeDataContracts.cs`에 operation/lifecycle, hash+size identity, assertion scope, upload session, provision result와 issuer/storage/provisioner 인터페이스를 추가했다. 실제 서명·파일 경로 처리는 다음 세부 작업에서 구현한다.

### B. Server assertion issuer

RS256 계열 서명, 짧은 만료, project/workstation/session/operation/object scope claim을 발급한다.

### C. Gateway verifier와 provision

공개키 검증, operation allowlist, hash·size·scope 검증, path escape/reparse 보호와 안전한 provision을 구현한다.

## 진행

현재 최종 통합 검증 진행. 외부 NAS 계정·절대 경로·비밀키는 저장소에 기록하지 않는다.

최신 재검증(2026-09-17): 운영 Server `/api/status=200`, NAS Gateway `200`, GC dry-run `safe=0, keep=0, review=0`을 확인했다. `forUpload.z01` 500MiB는 기존 resumable session을 재사용해 NAS `already_present` 경로로 완료됐고, 원본 SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267` 및 `524288000` bytes가 일치했다. `STAGED`와 `CHECKPOINTED`를 확인했으며 Server session lifecycle은 `COMPLETED`다. 백그라운드 uploader의 PowerShell 환경 차이를 제거하기 위해 uploader 해시 계산을 .NET SHA-256/FileStream 방식으로 고정했다.

GC 추가 검증: PowerShell에서 JSON 배열 응답이 단일 객체처럼 처리되던 문제를 수정했다. 운영 dry-run 결과 완료 session 2개를 개별 인식해 SAFE 2개(각 524,288,000 bytes), KEEP 0, REVIEW 0, reclaimable 1,048,576,000 bytes로 집계했다. 이는 staging cleanup 후보 집계이며 object 삭제를 의미하지 않는다. `-Apply`는 실제 NAS 대상 확인 전 실행하지 않았다.

GC 적용 결과: 승인된 두 SAFE session에 `-Apply`를 실행해 cleanup 요청이 완료됐고, 동일 명령 재실행 결과 두 session 모두 `ALREADY_CLEAN`으로 반환되어 idempotency를 확인했다. 활성 UPLOADING 보호는 코드상 TTL 이내이면 KEEP 처리되지만, 운영 Server에는 테스트 session 생성 공개 API가 없어 실제 보호 E2E는 별도 fixture 또는 관리 DB 준비 후 수행해야 한다.

운영 정리 보완: `ProjectHub_GC.ps1`와 `cleanup-session.php`는 구현됐고, `ProjectHub_GC.cmd`는 ExecutionPolicy를 영구 변경하지 않고 GC를 실행하는 런처다. 기본 동작은 dry-run이며 `-Apply`는 TTL과 lifecycle로 SAFE 판정된 session에만 사용한다. `-GatewayUrl`로 운영 Gateway를 명시할 수 있다.

삭제 검증 기준: ipDISK Drive 화면만으로 실제 삭제를 판정하지 않는다. NAS1DUAL 관리페이지와 실제 filesystem 상태를 기준으로 확인하며, Network Trashes Folder가 활성화된 경우 휴지통 잔존과 실제 용량 회수를 함께 확인한다. NAS 관리페이지 직접 삭제와 Network Trashes Folder 비우기로 테스트 데이터를 초기화한 것은 확인했지만, 이는 ProjectHub GC 성공 증거가 아니다.

현재까지 구현: 환경 변수 기반 RS256 assertion 발급·검증, operation/claim 검증, safe relative scope 및 content-addressed provision, chunk write/status/finalize와 최종 SHA-256·size 검증, Supabase metadata repository, Agent 대용량 파일 안정성·SHA-256 스캔을 추가했다. `supabase/large-data.sql`에 대용량 객체·업로드 세션·프로젝트 파일·dataset 메타데이터 스키마를 추가했다.

완료: 명시적 `ProjectHub_Sync.ps1`가 Batch 시작 시 branch/HEAD/dirty와 대용량 파일의 상대경로·size·mtime을 고정 manifest로 저장하고, Git/file 요약을 Server에 먼저 반영한다. 별도 `ProjectHub_LargeData_Uploader.ps1`는 project/workstation/object hash/size 기준으로 Supabase `UPLOADING` session을 조회해 기존 session ID를 재사용하고, 없을 때만 GUID session을 만든다. 이후 assertion 갱신, upload-start, chunk, status 기반 resume, finalize, NAS object identity 확인, Supabase STAGED 및 commit SHA 기반 CHECKPOINTED dataset 기록을 수행한다. 업로드 중 size/mtime이 바뀐 항목은 `CHANGED_DURING_UPLOAD`으로 제외한다. 정상 Agent startup/watcher는 대용량 hash/upload/staging/reconciliation을 수행하지 않으며 Git 자동 변경도 없다. 운영 assertion으로 upload-start → chunk → status → finalize, 최종 SHA-256 object 생성과 동일 hash dedup은 실제 NAS1DUAL에서 완료했다.

새 Server 세션에서도 운영 assertion 발급과 17바이트 upload 전체 경로를 재검증했다. upload-start, chunk 수신, status bytes/chunk 확인, finalize complete 및 SHA-256/size 일치가 성공했다.

500MiB `forUpload.z01`은 32개 16MiB chunk로 NAS staging에 정확히 수신됐으나 finalize에서 `size_mismatch`가 발생했다. `upload-finalize.php`에 finalize 직후 `clearstatcache(true, $assembled)`를 추가했으며, NAS 재배포 후 동일 세션 finalize 검증이 남았다.

500MiB 최종 확인: 기존 session에 새 assertion으로 finalize를 재시도했고, 동일 hash upload-start에서 `already_present=true`, `size_bytes=524288000`을 확인했다. NAS object 확정과 원본 SHA-256/size 일치가 완료됐다.

최종 정책: 평상시 Agent는 관찰 전용이다. 대용량 전송과 GC는 사용자가 명시적으로 Batch/GC를 시작했을 때만 수행한다. 실패 session 정리 또는 재시도도 다음 명시적 Batch/GC에서만 수행한다. finalize 성공 시 Server session을 `STAGED`로 갱신하며 NAS staging 정리는 Gateway finalize 응답으로 확인한다. `already_present` 경로도 `.part`, `session.json`, `assembled.tmp`를 모두 정리하도록 보완했고, 원본 파일명/확장자는 canonical hash object를 중복 복사하지 않는 hard-link alias로 노출한다. `ProjectHub_GC.ps1`은 기본 dry-run이며 `-Apply`도 DB metadata와 TTL 기준의 안전 후보만 cleanup assertion으로 삭제한다.

실제 환경 반영: PHP-visible root는 `/mnt/HDD1/ProjectHub`이며 `/HDD1/ProjectHub`를 사용하지 않는다. Gateway는 운영자가 준비한 root 내부만 사용한다. Gateway URL은 `https://dfblackbox-nas.duckdns.org:8443/projecthub/`로 설정한다. Authorization fallback과 NAS PHP 런타임 호환성을 유지하고, 운영 코드에서 TLS 인증서 검증을 우회하지 않는다. Provision/RS256/NAS write E2E는 완료로 기록한다.

검증: 2026-09-17 `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(5개 통과), PowerShell parser로 Batch Sync/uploader/Test/GC 스크립트 문법 검증 PASS. 과거 운영 E2E에서 Server `/api/status=200`, NAS health `200`과 500MiB object hash/size 일치를 확인했다. 같은 날 재검증 시 NAS Gateway는 health 및 `provision.php=405`, `upload-start.php=405`로 응답했으나 운영 Server `/api/status`는 `502`를 반환해 이번에는 GC dry-run과 Server assertion 기반 재업로드를 실행하지 못했다. GC dry-run은 DB session 목록 기준 SAFE/KEEP/REVIEW 후보를 표시하며, NAS-only orphan staging은 현재 API가 열거하지 않으므로 별도 NAS 관리페이지 확인 없이는 비어 있다고 단정하지 않는다. 실제 `-Apply`, Network Trashes Folder, hard-link/inode 검증은 NAS 운영 PC에서 수행해야 한다. 로컬 PHP CLI는 없어 PHP lint는 미실행이다.

## 변경 금지

- ProjectHub.Server가 대용량 바이너리를 relay하지 않는다.
- Agent는 Supabase·SMB·NAS filesystem에 직접 접근하지 않으며 NAS Gateway HTTPS만 사용한다.
- 자동 Git commit/push/pull/reset/merge/delete는 수행하지 않는다.

## 최종 검증

local/temp filesystem adapter 통합 테스트 후 실제 NAS1DUAL 환경에서만 NAS root, Gateway 실행 방식과 URL을 주입해 검증한다.
