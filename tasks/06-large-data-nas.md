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

남은 검증: 중단 후 resume, Supabase STAGED metadata, 실제 Git commit과 LargeDataSet/CHECKPOINTED item, Agent 재시작 reconciliation을 확인한다. 운영 assertion으로 upload-start → chunk → status → finalize, 최종 SHA-256 object 생성과 동일 hash dedup은 실제 NAS1DUAL에서 완료했다.

새 Server 세션에서도 운영 assertion 발급과 17바이트 upload 전체 경로를 재검증했다. upload-start, chunk 수신, status bytes/chunk 확인, finalize complete 및 SHA-256/size 일치가 성공했다.

500MiB `forUpload.z01`은 32개 16MiB chunk로 NAS staging에 정확히 수신됐으나 finalize에서 `size_mismatch`가 발생했다. `upload-finalize.php`에 finalize 직후 `clearstatcache(true, $assembled)`를 추가했으며, NAS 재배포 후 동일 세션 finalize 검증이 남았다.

실제 환경 반영: PHP-visible root는 `/mnt/HDD1/ProjectHub`이며 `/HDD1/ProjectHub`를 사용하지 않는다. Gateway는 운영자가 준비한 root 내부만 사용한다. Gateway URL은 `https://dfblackbox-nas.duckdns.org:8443/projecthub/`로 설정한다. Authorization fallback과 NAS PHP 런타임 호환성을 유지하고, 운영 코드에서 TLS 인증서 검증을 우회하지 않는다. Provision/RS256/NAS write E2E는 완료로 기록한다.

검증: `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(5개 통과). 로컬 PHP CLI는 없어 PHP lint는 미실행이다. 운영 Gateway에서는 기존 health `200`, 기존 `provision.php` GET `405`를 확인했으며, 새 upload PHP 파일은 아직 NAS에 배포되지 않아 원격 경로가 Blazor HTML을 반환했다.

## 변경 금지

- ProjectHub.Server가 대용량 바이너리를 relay하지 않는다.
- Agent는 Supabase·SMB·NAS filesystem에 직접 접근하지 않으며 NAS Gateway HTTPS만 사용한다.
- 자동 Git commit/push/pull/reset/merge/delete는 수행하지 않는다.

## 최종 검증

local/temp filesystem adapter 통합 테스트 후 실제 NAS1DUAL 환경에서만 NAS root, Gateway 실행 방식과 URL을 주입해 검증한다.
