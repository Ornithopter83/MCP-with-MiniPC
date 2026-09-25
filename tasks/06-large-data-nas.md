# 대용량 데이터/NAS

## 목표

ProjectHub-native 검증 토큰과 NAS Gateway 계약으로 대용량 파일을 서버가 중계하지 않고 안전하게 저장·검증한다.

## 세부 작업

### A. 계약·보안 경계와 구현 구조 (완료: 2026-09-16)

ProjectHub.서버 검증 토큰 발급기, ProjectHub NAS Gateway 검증기/프로비저닝, 콘텐츠 주소 기반 storage, 재개 가능 upload의 계약과 환경 변수 경계를 확정한다.

`ProjectHub.Core/LargeDataContracts.cs`에 작업/생명주기, hash+size 식별 정보, 검증 토큰 범위, 업로드 세션, 프로비저닝 result와 발급기/storage/provisioner 인터페이스를 추가했다. 실제 서명·파일 경로 처리는 다음 세부 작업에서 구현한다.

### B. 서버 검증 토큰 발급기

RS256 계열 서명, 짧은 만료, 프로젝트/작업 PC/세션/작업/객체 범위 클레임을 발급한다.

### C. 게이트웨이 검증기와 프로비저닝

공개키 검증, 작업 allowlist, hash·size·범위 검증, 경로 이탈/재분석 지점 보호와 안전한 프로비저닝을 구현한다.

## 진행

기능 검증 완료. 후속 개선을 진행하며, 외부 NAS 계정·절대 경로·비밀키는 저장소에 기록하지 않는다.

후속 개선 구현: uploader는 파일/session 범위에서 검증 토큰을 캐시하고 JWT 만료 임박 또는 Gateway 401일 때만 새 검증 토큰을 발급해 해당 요청을 1회 재시도한다. 파일별 try/catch로 실패를 격리하고 성공/실패 결과를 매니페스트 옆 `.result.json`에 저장한다. 하나라도 실패하거나 변경되면 전체 CHECKPOINTED 기록을 생략한다. 정상 완료 출력은 `STAGING_CLEANED; OBJECT_RETAINED`, `STAGED`, `CHECKPOINTED`로 구분한다.

최신 재검증(2026-09-17): 운영 서버 `/api/status=200`, NAS Gateway `200`, GC 모의 실행 `safe=0, keep=0, review=0`을 확인했다. `forUpload.z01` 500MiB는 기존 재개 가능 session을 재사용해 NAS `already_present` 경로로 완료됐고, 원본 SHA-256 `e93ac6ff6751cd7f016305ba1f5eb97440108c59bfda42b364eb41927f9e8267` 및 `524288000` 바이트가 일치했다. `STAGED`와 `CHECKPOINTED`를 확인했으며 서버 session 생명주기은 `COMPLETED`다. 백그라운드 uploader의 PowerShell 환경 차이를 제거하기 위해 uploader 해시 계산을 .NET SHA-256/FileStream 방식으로 고정했다.

GC 추가 검증: PowerShell에서 JSON 배열 응답이 단일 객체처럼 처리되던 문제를 수정했다. 운영 모의 실행 결과 완료 session 2개를 개별 인식해 안전 2개(각 524,288,000 바이트), 유지 0, 검토 0, reclaimable 1,048,576,000 바이트로 집계했다. 이는 준비 영역 cleanup 후보 집계이며 object 삭제를 의미하지 않는다. `-Apply`는 실제 NAS 대상 확인 전 실행하지 않았다.

GC 적용 결과: 승인된 두 안전 session에 `-Apply`를 실행해 cleanup 요청이 완료됐고, 동일 명령 재실행 결과 두 session 모두 `ALREADY_CLEAN`으로 반환되어 idempotency를 확인했다. 테스트 `UPLOADING` session은 모의 실행에서 `KEEP`으로 보호됐고, fixture 삭제 후 session count 0 및 안전/유지/검토 0을 확인했다.

운영 정리 보완: `ProjectHub_GC.ps1`와 `cleanup-session.php`는 구현됐고, `ProjectHub_GC.cmd`는 ExecutionPolicy를 영구 변경하지 않고 GC를 실행하는 런처다. 기본 동작은 모의 실행이며 `-Apply`는 TTL과 생명주기로 안전 판정된 session에만 사용한다. `-GatewayUrl`로 운영 Gateway를 명시할 수 있다.

삭제 검증 기준: ipDISK Drive 화면만으로 실제 삭제를 판정하지 않는다. NAS1DUAL 관리페이지와 실제 파일 시스템 상태를 기준으로 확인하며, 네트워크 휴지통 폴더가 활성화된 경우 휴지통 잔존과 실제 용량 회수를 함께 확인한다. NAS 관리페이지 직접 삭제와 네트워크 휴지통 폴더 비우기로 테스트 데이터를 초기화한 것은 확인했지만, 이는 ProjectHub GC 성공 증거가 아니다.

현재까지 구현: 환경 변수 기반 RS256 검증 토큰 발급·검증, 작업/claim 검증, 안전 relative 범위 및 콘텐츠 주소 기반 프로비저닝, 청크 write/status/완료 처리와 최종 SHA-256·size 검증, Supabase 메타데이터 repository, 에이전트 대용량 파일 안정성·SHA-256 스캔을 추가했다. `supabase/large-data.sql`에 대용량 객체·업로드 세션·프로젝트 파일·dataset 메타데이터 스키마를 추가했다.

완료: 명시적 `ProjectHub_Sync.ps1`가 Batch 시작 시 브랜치/HEAD/변경 있음와 대용량 파일의 상대경로·size·mtime을 고정 매니페스트로 저장하고, Git/file 요약을 서버에 먼저 반영한다. 별도 `ProjectHub_LargeData_Uploader.ps1`는 project/workstation/object hash/size 기준으로 Supabase `UPLOADING` session을 조회해 기존 session ID를 재사용하고, 없을 때만 GUID session을 만든다. 이후 검증 토큰 갱신, upload-start, 청크, status 기반 재개, 완료 처리, NAS object 식별 정보 확인, Supabase STAGED 및 commit SHA 기반 CHECKPOINTED dataset 기록을 수행한다. 업로드 중 size/mtime이 바뀐 항목은 `CHANGED_DURING_UPLOAD`으로 제외한다. 정상 에이전트 시작/감시기는 대용량 hash/upload/준비 영역/reconciliation을 수행하지 않으며 Git 자동 변경도 없다. 운영 검증 토큰으로 upload-start → 청크 → status → 완료 처리, 최종 SHA-256 object 생성과 동일 hash 중복 제거은 실제 NAS1DUAL에서 완료했다.

새 서버 세션에서도 운영 검증 토큰 발급과 17바이트 upload 전체 경로를 재검증했다. upload-start, 청크 수신, status 바이트/청크 확인, 완료 처리 complete 및 SHA-256/size 일치가 성공했다.

500MiB `forUpload.z01`은 32개 16MiB 청크로 NAS 준비 영역에 정확히 수신됐으나 완료 처리에서 `size_mismatch`가 발생했다. `upload-finalize.php`에 완료 처리 직후 `clearstatcache(true, $assembled)`를 추가했으며, NAS 재배포 후 동일 세션 완료 처리 검증이 남았다.

500MiB 최종 확인: 기존 session에 새 검증 토큰으로 완료 처리를 재시도했고, 동일 hash upload-start에서 `already_present=true`, `size_bytes=524288000`을 확인했다. NAS object 확정과 원본 SHA-256/size 일치가 완료됐다.

최종 정책: 평상시 에이전트는 관찰 전용이다. 대용량 전송과 GC는 사용자가 명시적으로 Batch/GC를 시작했을 때만 수행한다. 실패 session 정리 또는 재시도도 다음 명시적 Batch/GC에서만 수행한다. 완료 처리 성공 시 서버 session을 `STAGED`로 갱신하며 NAS 준비 영역 정리는 Gateway 완료 처리 응답으로 확인한다. `already_present` 경로도 `.part`, `session.json`, `assembled.tmp`를 모두 정리하도록 보완했고, 원본 파일명/확장자는 canonical hash object를 중복 복사하지 않는 하드 링크 alias로 노출한다. `ProjectHub_GC.ps1`은 기본 모의 실행이며 `-Apply`도 DB 메타데이터와 TTL 기준의 안전 후보만 cleanup 검증 토큰으로 삭제한다.

실제 환경 반영: PHP-visible root는 `/mnt/HDD1/ProjectHub`이며 `/HDD1/ProjectHub`를 사용하지 않는다. Gateway는 운영자가 준비한 root 내부만 사용한다. Gateway URL은 `https://dfblackbox-nas.duckdns.org:8443/projecthub/`로 설정한다. Authorization fallback과 NAS PHP 런타임 호환성을 유지하고, 운영 코드에서 TLS 인증서 검증을 우회하지 않는다. 프로비저닝/RS256/NAS write E2E는 완료로 기록한다.

검증: 2026-09-17 `dotnet build ProjectHub.sln --no-restore` 성공(경고 0, 오류 0), `dotnet test ProjectHub.sln --no-restore` 성공(5개 통과), PowerShell parser로 Batch Sync/uploader/Test/GC 스크립트 문법 검증 PASS. 과거 운영 E2E에서 서버 `/api/status=200`, NAS health `200`과 500MiB object hash/size 일치를 확인했다. 같은 날 재검증 시 NAS Gateway는 health 및 `provision.php=405`, `upload-start.php=405`로 응답했으나 운영 서버 `/api/status`는 `502`를 반환해 이번에는 GC 모의 실행과 서버 검증 토큰 기반 재업로드를 실행하지 못했다. GC 모의 실행은 DB session 목록 기준 안전/유지/검토 후보를 표시하며, NAS-only orphan 준비 영역은 현재 API가 열거하지 않으므로 별도 NAS 관리페이지 확인 없이는 비어 있다고 단정하지 않는다. 실제 `-Apply`, 네트워크 휴지통 폴더, 하드 링크/아이노드 검증은 NAS 운영 PC에서 수행해야 한다. 로컬 PHP CLI는 없어 PHP 문법 검사는 미실행이다.

## 변경 금지

- ProjectHub.서버가 대용량 바이너리를 relay하지 않는다.
- 에이전트는 Supabase·SMB·NAS 파일 시스템에 직접 접근하지 않으며 NAS Gateway HTTPS만 사용한다.
- 에이전트는 자동 Git 커밋/푸시/풀/리셋/병합/삭제를 수행하지 않는다. v0.2의 사용자 명시적 Commit_Push CMD만 Git 체크포인트 흐름을 실행한다.

## 현재 한계: 로컬 대용량 파일 삭제 추적

현재 `ProjectHub_Sync.cmd`는 실행 시점에 로컬에 존재하는 대용량 파일만 새 매니페스트로 수집한다. 이전 매니페스트 또는 체크포인트와 현재 매니페스트를 비교해 사라진 파일을 삭제 후보로 만드는 기능은 아직 없다.

따라서 Git에 추가하지 않은 대용량 파일을 로컬에서 삭제한 뒤 Sync를 실행해도 다음 동작은 자동으로 수행되지 않는다.

- 삭제된 파일의 과거 SHA-256 object 식별
- NAS `objects/sha256` 정규 객체 삭제
- Supabase `large_objects`, `project_large_files`, `large_data_sets` 메타데이터 삭제
- 삭제 결과 및 NAS 휴지통/실제 디스크 공간 회수 확인

현재 `ProjectHub_GC.ps1`와 `cleanup-session.php`는 TTL과 생명주기이 안전하다고 판정된 업로드 session의 준비 영역 정리용이다. 이는 정규 객체나 체크포인트 메타데이터를 삭제하는 기능이 아니며, 기본 동작도 모의 실행이다. NAS에만 남은 orphan 준비 영역/object는 현재 서버 API로 열거하지 못하므로 NAS 관리페이지 또는 관리용 별도 절차의 확인이 필요하다.

`hw` 테스트에서 `forUpload.z01`을 로컬에서 삭제한 상태를 commit한 것은 Git 원격에서 파일을 제거하는 것일 뿐 NAS와 Supabase 자산 삭제를 의미하지 않는다. 이 한계를 해소하려면 이전·현재 매니페스트 비교, 명시적 삭제 승인, object/메타데이터 연쇄 삭제, idempotent 결과 검증을 별도 작업으로 설계해야 한다.

## 최종 검증

local/temp 파일 시스템 어댑터 통합 테스트 후 실제 NAS1DUAL 환경에서만 NAS root, Gateway 실행 방식과 URL을 주입해 검증한다.
