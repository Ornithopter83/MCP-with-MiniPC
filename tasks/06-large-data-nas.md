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

현재까지 구현: 환경 변수 기반 RS256 assertion 발급·검증, operation/claim 검증, safe relative scope 및 content-addressed provision, chunk write/status/finalize와 최종 SHA-256·size 검증, Supabase metadata repository, Agent 대용량 파일 안정성·SHA-256 스캔을 추가했다. `supabase/large-data.sql`에 대용량 객체·업로드 세션·프로젝트 파일·dataset 메타데이터 스키마를 추가했다.

완료: 명시적 `ProjectHub_Sync.ps1`가 Batch 시작 시 branch/HEAD/dirty와 대용량 파일의 상대경로·size·mtime을 고정 manifest로 저장하고, Git/file 요약을 Server에 먼저 반영한다. 별도 `ProjectHub_LargeData_Uploader.ps1`는 project/workstation/object hash/size 기준으로 Supabase `UPLOADING` session을 조회해 기존 session ID를 재사용하고, 없을 때만 GUID session을 만든다. 이후 assertion 갱신, upload-start, chunk, status 기반 resume, finalize, NAS object identity 확인, Supabase STAGED 및 commit SHA 기반 CHECKPOINTED dataset 기록을 수행한다. 업로드 중 size/mtime이 바뀐 항목은 `CHANGED_DURING_UPLOAD`으로 제외한다. 정상 Agent startup/watcher는 대용량 hash/upload/staging/reconciliation을 수행하지 않으며 Git 자동 변경도 없다. 운영 assertion으로 upload-start → chunk → status → finalize, 최종 SHA-256 object 생성과 동일 hash dedup은 실제 NAS1DUAL에서 완료했다.

새 Server 세션에서도 운영 assertion 발급과 17바이트 upload 전체 경로를 재검증했다. upload-start, chunk 수신, status bytes/chunk 확인, finalize complete 및 SHA-256/size 일치가 성공했다.

500MiB `forUpload.z01`은 32개 16MiB chunk로 NAS staging에 정확히 수신됐으나 finalize에서 `size_mismatch`가 발생했다. `upload-finalize.php`에 finalize 직후 `clearstatcache(true, $assembled)`를 추가했으며, NAS 재배포 후 동일 세션 finalize 검증이 남았다.

500MiB 최종 확인: 기존 session에 새 assertion으로 finalize를 재시도했고, 동일 hash upload-start에서 `already_present=true`, `size_bytes=524288000`을 확인했다. NAS object 확정과 원본 SHA-256/size 일치가 완료됐다.

최종 정책: 평상시 Agent는 관찰 전용이다. 대용량 전송과 GC는 사용자가 명시적으로 Batch/GC를 시작했을 때만 수행한다. 실패 session 정리 또는 재시도도 다음 명시적 Batch/GC에서만 수행한다. finalize 성공 시 Server session을 `STAGED`로 갱신하며 NAS staging 정리는 Gateway finalize 응답으로 확인한다. `already_present` 경로도 `.part`, `session.json`, `assembled.tmp`를 모두 정리하도록 보완했고, 원본 파일명/확장자는 canonical hash object를 중복 복사하지 않는 hard-link alias로 노출한다. `ProjectHub_GC.ps1`은 기본 dry-run이며 `-Apply`도 DB metadata와 TTL 기준의 안전 후보만 cleanup assertion으로 삭제한다.

실제 환경 반영: PHP-visible root는 `/mnt/HDD1/ProjectHub`이며 `/HDD1/ProjectHub`를 사용하지 않는다. Gateway는 운영자가 준비한 root 내부만 사용한다. Gateway URL은 `https://dfblackbox-nas.duckdns.org:8443/projecthub/`로 설정한다. Authorization fallback과 NAS PHP 런타임 호환성을 유지하고, 운영 코드에서 TLS 인증서 검증을 우회하지 않는다. Provision/RS256/NAS write E2E는 완료로 기록한다.

검증: 2026-09-17 `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(5개 통과), PowerShell parser로 Batch Sync/uploader/Test 스크립트 문법 검증 PASS. 운영 Server `/api/status=200`, NAS health `200`을 확인했다. 실제 `ProjectHub_Sync.ps1` 500MiB Batch에서 main metadata 선반영과 별도 uploader를 실행했고, uploader 전면 재실행 결과 `ALREADY_PRESENT`(hash/size 일치), `STAGED`, `CHECKPOINTED`(captured HEAD `6481d1439a75233dc0f8504bbfeb74e7856f5f15`)를 확인했다. canonical object는 기존 hash 경로이므로 원본 파일명 alias와 staging cleanup 수정은 NAS Gateway PHP 재배포 후 확인해야 하며, 기존 orphan staging은 NAS에서 일회성 명시 cleanup이 필요하다. 로컬 PHP CLI는 없어 PHP lint는 미실행이다.

## 변경 금지

- ProjectHub.Server가 대용량 바이너리를 relay하지 않는다.
- Agent는 Supabase·SMB·NAS filesystem에 직접 접근하지 않으며 NAS Gateway HTTPS만 사용한다.
- 자동 Git commit/push/pull/reset/merge/delete는 수행하지 않는다.

## 최종 검증

local/temp filesystem adapter 통합 테스트 후 실제 NAS1DUAL 환경에서만 NAS root, Gateway 실행 방식과 URL을 주입해 검증한다.
