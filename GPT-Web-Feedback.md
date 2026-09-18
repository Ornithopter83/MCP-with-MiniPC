# GPT Web Feedback

Updated: 2026-09-18

## 최신 확인

```text
18aacb6fee012af27dcfb0c00926878804ad2618
Document hw force restore validation results
```

현재 방향은 유지하되, 재검증 전에 아래 3가지를 함께 반영한다.

## 1. 관리 프로젝트 UX 구조 정리

사용자가 관리 프로젝트 루트에서 보게 될 ProjectHub 진입점은 아래 3개만 둔다.

```text
ProjectHub_Commit_Push.cmd
ProjectHub_Fetch_Pull.cmd
ProjectHub_Force_Restore.cmd
```

나머지 ProjectHub 내부 파일은 모두 프로젝트 내부의 전용 폴더로 이동한다.

```text
<ProjectRoot>\
├─ ProjectHub_Commit_Push.cmd
├─ ProjectHub_Fetch_Pull.cmd
├─ ProjectHub_Force_Restore.cmd
└─ ProjectHub\
   ├─ bin\
   │  ├─ ProjectHub_Commit_Push.ps1
   │  ├─ ProjectHub_Fetch_Pull.ps1
   │  ├─ ProjectHub_Force_Restore.ps1
   │  ├─ ProjectHub_Sync.ps1
   │  ├─ ProjectHub_Restore.ps1
   │  └─ ProjectHub_LargeData_Uploader.ps1
   ├─ config\
   │  └─ project.json
   ├─ state\
   └─ log\
```

기존 `.projecthub\`, 루트 `bin\`, `ProjectHub_Sync.cmd`, `ProjectHub_Restore.cmd`를 새 UX의 최종 사용자 진입점으로 남기지 않는다.
Setup은 관리 프로젝트의 위치를 바꾸거나 별도 경로를 요구하지 않는다. 현재 프로젝트 루트를 그대로 사용한다.

강제 복구는 ProjectHub 자체를 절대 손상시키지 않아야 한다.

```text
보호 대상:
- ProjectHub\ 전체
- 루트의 위 3개 CMD
```

`git clean -fd`를 그대로 사용하지 말고 위 경로를 명시적으로 exclude한다. 가능하면 임시 백업/복원에 의존하기보다 처음부터 보호한다.
Force Restore 완료 후에도 위 보호 대상의 존재 및 최소 실행 가능 여부를 검증한다.

## 2. Large Data Restore 불일치/부분 적용 방지

현재 size/SHA-256 검증 방향은 유지한다. 추가로 Restore와 Force Restore를 명확한 2단계 적용으로 만든다.

```text
A. PREPARE
1. 최신 checkpoint/manifest 확정
2. 필요한 대용량 파일을 temp에 전부 다운로드
3. 모든 파일 size + SHA-256 검증
4. 하나라도 실패하면 실제 프로젝트 파일을 변경하지 않고 실패

B. APPLY
5. PREPARE가 전부 성공한 경우에만 실제 프로젝트 폴더에 적용
6. REMOVED 처리 적용
7. 적용 완료 후 전체 managed large file을 다시 size + SHA-256 검증
8. missing/mismatch가 1개라도 있으면 성공 처리 금지
```

최종 출력 예:

```text
RESTORE_VERIFY
expected   : N
matched    : N
mismatched : 0
missing    : 0
```

`ProjectHub_Force_Restore`는 이 최종 검증이 0 mismatch / 0 missing일 때만 성공으로 끝낸다.
Commit-Push도 대용량 변경 파일 중 하나라도 upload/finalize 검증이 실패하면 해당 상태를 정상 checkpoint로 확정하지 않는다.

## 3. NAS download.php 500 긴급 수정/진단

현재 첨부 및 저장소의 `download.php`는 assertion의 `object_hash`로 canonical object를 직접 찾는다.

```text
/mnt/HDD1/ProjectHub/objects/sha256/<앞2글자>/<sha256>
```

현재 관찰된 실패:

```text
assertion 발급 성공
download.php 호출
HTTP 500
Content-Type: application/octet-stream
응답 body 0 bytes
```

현재 코드상 application/octet-stream header는 object 존재/size 검사 이후에 설정된다. 따라서 단순 object_not_found보다는 `readfile()` 단계 또는 NAS 권한/ACL/I/O 문제를 우선 의심한다.

`download.php`를 다음 원칙으로 보강한다.

```text
1. is_readable($path) 사전 확인
2. filesize 결과 실패/불일치 명확히 분리
3. readfile() 반환값 확인
4. 실패 시 error_log에 operation, object hash, resolved path, size 정도만 기록
5. secret/JWT/Authorization은 절대 로그 금지
6. PHP warning이 빈 500으로 끝나지 않게 명확한 오류 코드 또는 서버 로그를 남김
```

가능하면 진단 오류를 아래처럼 구분한다.

```text
object_not_found
object_size_mismatch
object_not_readable
object_read_failed
```

운영 NAS에서 실패 object에 대해 반드시 확인:

```text
ls -l <canonical object>
stat <canonical object>
PHP/web 실행 계정 기준 read 가능 여부
parent directories execute/read 권한 및 ACL
```

NAS/DB를 비운 뒤 다시 생성한 환경에서 발생했으므로 새 object/디렉터리의 owner/group/permission이 기존 운영 상태와 달라졌는지도 확인한다.

## 재검증 순서

수정 후에는 아래 순서로 다시 검증한다.

```text
1. 작은 large-data 파일 Commit-Push
2. NAS canonical object 실제 존재/size/hash 확인
3. download.php 단독 download 성공 확인
4. Fetch-Pull Restore 성공
5. 로컬 large file을 임의 변경 후 Fetch-Pull → server version으로 복구 확인
6. local large file 삭제 후 Fetch-Pull → 재다운로드 확인
7. Force Restore 실행
8. Git 최신 상태 + Large Data 0 mismatch / 0 missing 확인
9. ProjectHub\ 및 루트 3개 CMD가 그대로 보호됐는지 확인
```

이번 수정은 별도 기능 확장보다 위 세 항목의 안정화와 E2E 재검증을 우선한다.
