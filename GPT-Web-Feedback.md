# GPT Web Feedback

Updated: 2026-09-17

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다. 단, 아래 Large Data/NAS 정책은 사용자가 2026-09-17 명시적으로 요청한 최신 운영 요구이므로 관련 문서와 구현을 이 기준에 맞게 갱신한다.

---

# 1. 최신 검토 기준

최신 확인 커밋:

```text
755ce0de0cfeddb0c3dcb31d407e2a98cb57b742
Harden NAS staging cleanup and upload progress
```

현재 확인된 방향:

```text
[x] 평상시 Agent 자동 대용량 업로드 폐기
[x] 명시적 ProjectHub_Sync.ps1 기반 Batch Sync
[x] 별도 ProjectHub_LargeData_Uploader.ps1 프로세스
[x] 파일별 [i/n], HASHING, FINALIZING, DONE 표시
[x] chunk/byte/percent 진행 표시
[x] resumable session 재사용
[x] NAS finalize 후 staging cleanup 결과를 강제 확인
[x] files/<project>/<relative-path> named path 생성
[x] objects/sha256/<hash> content-addressed object 유지
```

그러나 실제 NAS 운영에서 사용자는 `files/<project>/<relative-path>`에 최종 파일이 정상 생성된 뒤에도 과거 chunk/staging 데이터가 남아 동일한 수준의 용량을 차지하는 현상을 확인했다.

따라서 **정상 finalize 시 즉시 cleanup**만으로는 충분하지 않다. 이미 남은 실패/중단/구버전 session을 안전하게 정리하는 **Garbage Collection(GC)** 기능을 정식으로 추가한다.

---

# 2. 먼저 구분해야 할 NAS 데이터 영역

NAS ProjectHub root의 역할을 명확히 구분한다.

```text
/mnt/HDD1/ProjectHub/
├─ files/                 사용자 관점의 프로젝트/파일 경로
│  └─ <project>/<relative-path>
├─ objects/sha256/        content-addressed object 저장소
│  └─ <prefix>/<hash>
└─ staging/               업로드 중 임시 chunk/session 영역
   └─ <session-id>/
```

중요:

`files/...`와 `objects/sha256/...`는 현재 hard link 방식일 수 있다. 이 경우 두 경로에서 같은 파일 크기로 보이더라도 실제 디스크 블록을 두 번 사용하는 것이 아닐 수 있다.

따라서 GC 구현 전에 실제 NAS에서 다음을 확인해 기록한다.

```text
- files 경로와 objects 경로가 동일 inode인지
- link count가 2 이상인지
- NAS 실제 사용량이 두 경로 때문에 이중 증가하는지
```

`files`와 `objects`가 hard link라면 **둘 중 하나를 중복 데이터라고 보고 임의 삭제하면 안 된다.**

반면 `staging/<session-id>/*.part`는 실제 임시 chunk이므로 finalize 후 남아 있으면 실제 추가 용량을 점유한다. 이번 GC의 1차 대상은 staging이다.

---

# 3. GC를 두 단계로 분리

Garbage Collection은 반드시 다음 두 종류로 분리한다.

## A. Staging GC — 이번에 우선 구현

대상:

```text
staging/<session-id>/
*.part
assembled.tmp
session.json
```

목표:

```text
- 정상 완료됐는데 남은 session 정리
- 취소된 session 정리
- 오래 전에 실패하고 더 이상 resume 대상이 아닌 session 정리
- DB에 대응 session이 없는 orphan staging 정리
```

## B. Object GC — 별도 후속 단계

대상:

```text
objects/sha256/<hash>
files/<project>/<relative-path>
```

Object GC는 훨씬 보수적으로 처리한다.

다음 reference를 모두 확인하기 전에는 object를 삭제하지 않는다.

```text
- project_large_files
- large_data_sets
- large_data_set_items
- 현재 named files alias
- 다른 project에서 동일 SHA-256을 참조하는지
```

**reference count가 0인 object만 삭제 가능**하다.

이번 작업에서는 Object GC를 자동 실행하지 않아도 된다. 우선 Staging GC를 완성한다.

---

# 4. Staging GC의 안전 기준

GC는 단순히 오래된 디렉터리를 `rm -rf` 하는 기능이어서는 안 된다.

각 `staging/<session-id>`에 대해 최소 다음을 대조한다.

```text
session_id
project_id
workstation_id
object_hash
size_bytes
session lifecycle
last_activity_at
NAS session.json 내용
```

삭제 가능한 상태 예:

```text
COMPLETED  -> 즉시 cleanup 가능
CANCELLED  -> cleanup 가능
ABANDONED  -> TTL 경과 후 cleanup 가능
DB에 session 없음 + 충분한 TTL 경과 -> ORPHAN 후보
```

삭제 금지:

```text
UPLOADING
최근 activity가 있는 session
현재 실행 중인 uploader가 보유한 session
metadata가 서로 불일치하는 session
```

metadata 불일치 시 자동 삭제하지 말고 `REVIEW_REQUIRED` 또는 동등한 경고 후보로 남긴다.

---

# 5. upload session lifecycle 보강

현재 `UPLOADING`/`COMPLETED`만으로는 운영 정리에 부족하다.

가능하면 다음 상태를 명확히 지원한다.

```text
UPLOADING
COMPLETED
CANCELLED
ABANDONED
```

최소한 `CANCELLED`를 추가한다.

사용자가 해당 업로드를 버리기로 결정하면:

```text
UPLOADING
-> CANCELLED
-> 해당 session은 다음 Batch에서 resume하지 않음
-> Staging GC 정리 대상이 됨
```

이 기능이 없으면 오래된 `UPLOADING` session을 다음 Batch가 계속 resume 대상으로 볼 수 있다.

---

# 6. last_activity_at 또는 동등 정보 필요

단순 생성 시각만으로 stale 여부를 판단하지 않는다.

chunk 수신, status/resume, assertion 갱신 또는 uploader 진행 시점 중 적절한 경로에서 session의 `last_activity_at`을 갱신한다.

예:

```text
created_at       = 09:00
last_activity_at = 10:21
lifecycle        = UPLOADING
```

이 session은 생성된 지 오래됐더라도 활성 session이므로 GC 금지다.

권장 기본 TTL은 코드 상수로 박지 말고 설정 가능하게 한다.

예:

```text
PROJECTHUB_STAGING_GC_TTL_HOURS=24
```

실제 기본값은 구현자가 현재 운영 패턴을 보고 보수적으로 선택한다.

---

# 7. GC는 평상시 Agent가 자동 수행하지 않는다

기존 최신 정책을 유지한다.

```text
Agent startup
FileSystemWatcher
heartbeat
Git 상태 변경
```

만으로 GC를 자동 수행하지 않는다.

GC 실행은 다음 중 하나로 제한한다.

```text
1. 사용자가 명시적으로 ProjectHub_GC.ps1 실행
2. Batch Sync가 정상 끝난 뒤 안전 후보만 정리하는 명시적 cleanup 단계
3. 향후 Server 운영 스케줄에 넣더라도 dry-run/보수적 조건을 만족하는 경우
```

지금 단계의 기본 UX는 **명시적 GC 실행 + dry-run 기본값**으로 한다.

---

# 8. 권장 GC 사용자 경험

새 entrypoint 예:

```text
ProjectHub_GC.ps1
```

기본 실행은 실제 삭제가 아닌 dry-run이다.

예:

```text
ProjectHub Large Data GC

Scanning staging sessions...

[SAFE]   a1b2...  COMPLETED   500.0 MB   age 2h
[SAFE]   c3d4...  CANCELLED   1.2 GB     age 4h
[KEEP]   e5f6...  UPLOADING   3.8 GB     last activity 3m ago
[ORPHAN] 7788...  no DB row    700 MB     age 9d
[REVIEW] 99aa...  metadata mismatch

Reclaimable staging space: 2.4 GB
No files deleted. Use -Apply to execute safe cleanup.
```

실제 삭제:

```powershell
.\ProjectHub_GC.ps1 -Apply
```

실행 후:

```text
Deleted sessions : 3
Freed space      : 2.4 GB
Skipped active   : 1
Review required  : 1
```

`-Force`로 위험 후보까지 지우는 기능은 이번 단계에서는 만들지 않는 것을 권장한다.

---

# 9. GC control plane / data plane 경계

기존 아키텍처를 유지한다.

```text
DEV PC / Admin Script
    -> ProjectHub.Server
    -> metadata 확인

ProjectHub.Server
    -> scoped cleanup assertion 발급

Admin Script 또는 Server-orchestrated client
    -> NAS Gateway HTTPS cleanup endpoint
    -> NAS staging session만 삭제
```

금지:

```text
- DEV PC가 SMB/NAS filesystem credential로 직접 staging 삭제
- Agent가 Supabase에 직접 접근해 GC 판단
- Server가 대용량 binary를 relay
```

현재 assertion operation이 upload만 있다면 GC용으로 명시적 scoped operation을 추가한다.

예:

```text
operation = cleanup
scope = project/workstation/session
```

cleanup assertion은 지정된 `upload_session_id` 외 다른 session이나 objects/files에 접근할 수 없어야 한다.

---

# 10. Gateway cleanup endpoint 요구

예:

```text
POST /cleanup-session.php
```

또는 동등 API.

입력 권한은 short-lived RS256 assertion으로 제한한다.

Gateway에서 반드시 확인:

```text
- operation == cleanup
- upload_session_id 일치
- project_id/workstation_id 일치
- session.json metadata 일치
- path escape 금지
- symlink 금지
```

삭제 대상은 해당 staging session 내부로 제한한다.

```text
*.part
assembled.tmp
session.json
session directory
```

`objects/`와 `files/`는 이 endpoint에서 절대 삭제하지 않는다.

cleanup 결과 예:

```json
{
  "state": "cleaned",
  "upload_session_id": "...",
  "deleted_files": 33,
  "freed_bytes": 524288000
}
```

이미 directory가 없는 경우도 idempotent하게 성공 처리 가능하다.

```json
{
  "state": "already_clean",
  "freed_bytes": 0
}
```

---

# 11. 정상 finalize cleanup + GC는 둘 다 필요

GC를 추가한다고 해서 finalize cleanup을 느슨하게 하면 안 된다.

정상 흐름:

```text
upload chunks
-> finalize
-> hash/size 확인
-> object commit
-> files named alias 확인
-> staging 즉시 cleanup
-> staging_cleaned=true
-> Server session COMPLETED
```

이 경로가 기본이다.

GC는 예외 복구용이다.

```text
프로세스 강제종료
네트워크 단절
구버전 코드가 남긴 session
cleanup 실패
사용자 취소
```

같은 경우를 처리한다.

즉:

```text
정상 cleanup = 1차 방어
Staging GC    = 2차 방어
```

이다.

---

# 12. 현재 남아 있는 staging을 실제로 정리하는 절차

새 GC 기능이 완성되면 실제 NAS의 기존 잔여 staging을 바로 삭제하지 말고 먼저 dry-run 보고서를 만든다.

반드시 다음을 사용자에게 보고한다.

```text
- session 수
- 각 session 상태
- 각 session 용량
- DB/Supabase 대응 row 존재 여부
- 마지막 activity
- 삭제 가능/유지/검토 필요 판정
- 총 회수 가능 용량
```

사용자가 결과를 확인한 뒤 `-Apply`로 정리한다.

과거 테스트 session이라 하더라도 active metadata와 충돌하면 자동 삭제하지 않는다.

---

# 13. hard link / 실제 디스크 사용량 검증

현재 `files/<project>/<relative-path>`와 `objects/sha256/<hash>`가 hard link라면 파일 관리 UI에서 각각 전체 크기로 보여도 실제 NAS 사용량은 한 파일 분량일 수 있다.

Codex는 이 점을 실제 NAS에서 검증할 수 있는 운영 명령 또는 작은 PHP 진단을 준비한다.

확인 대상:

```text
inode 동일 여부
link count
실제 filesystem 사용량
```

이 확인 없이 `objects`를 중복 파일로 판단해 GC 대상으로 삼지 않는다.

---

# 14. Object GC는 별도 승인 후 구현

향후 Object GC를 구현할 때 원칙:

```text
1. DB reference graph 생성
2. files alias reference 확인
3. dataset/checkpoint reference 확인
4. 다른 project의 동일 hash reference 확인
5. reference count == 0만 후보
6. dry-run
7. 사용자 승인 또는 안전한 운영 정책 후 삭제
```

content-addressed object는 프로젝트 버전 복원에 필요한 핵심 데이터이므로 staging과 같은 TTL 삭제 정책을 적용하면 안 된다.

---

# 15. 현재 업로드 진행 UX 요구 유지

GC 작업과 별도로 uploader 진행 표시 요구는 유지한다.

최소:

```text
[i/n] 파일명
HASHING / RESUMING / UPLOADING / FINALIZING / DONE / FAILED
percent
bytes / total bytes
completed chunks / total chunks
resume 여부
```

파일 완료 시 콘솔에 완료 로그를 남긴다.

가능하면 batch 전체 진행률, 속도, ETA도 추가한다.

---

# 16. 오류 하나가 전체 batch를 중단하지 않도록 검토

현재 구조가 한 파일 실패 시 전체 `foreach`를 빠져나온다면 파일 단위 실패 격리를 검토한다.

권장:

```text
file-A DONE
file-B FAILED
file-C 계속 진행
```

다만 captured manifest 전체가 하나의 checkpoint 단위라면 하나라도 실패했을 때 전체 CHECKPOINTED 처리는 하지 않는다. 성공 파일은 STAGED로 유지하고 다음 Batch에서 실패 파일을 재처리한다.

---

# 17. 06 완료 상태를 보수적으로 유지

다음 검증이 끝날 때까지 문서는:

```text
06 Large Data/NAS 기능 구현 완료 / 최종 운영 검증 중
```

정도로 표현한다.

완료 전 필수 검증:

```text
[ ] multi-file uploader progress 실제 확인
[ ] 정상 finalize 후 staging session 완전 제거
[ ] interrupted resume 후 finalize/cleanup 성공
[ ] already_present 경로 stale staging cleanup
[ ] Staging GC dry-run 후보 판정
[ ] COMPLETED/CANCELLED/orphan session 실제 cleanup
[ ] UPLOADING active session 보호 확인
[ ] GC 후 회수 용량 확인
[ ] files/object hard-link 및 실제 사용량 확인
[ ] build/test PASS
```

---

# 18. Codex 수행 순서

```text
1. 최신 저장소와 CurrentWork/tasks/06-large-data-nas.md 확인
2. 최신 finalize cleanup 구현을 유지하고 실제 NAS E2E 재검증
3. large_upload_sessions에 CANCELLED/ABANDONED 및 last_activity_at 필요성 검토·최소 구현
4. Staging GC 후보 조회 API/서비스 구현
5. cleanup 전용 scoped assertion/operation 구현
6. NAS Gateway cleanup-session endpoint 구현
7. ProjectHub_GC.ps1 dry-run 구현
8. -Apply 안전 후보 삭제 구현
9. active UPLOADING session 보호 테스트
10. orphan/COMPLETED/CANCELLED cleanup 테스트
11. freed_bytes 및 결과 summary 출력
12. hard-link inode/link-count/실제 disk 사용량 검증
13. 기존 staging 잔여 데이터를 dry-run으로 분석하고 사용자에게 보고
14. 사용자 확인 후 실제 GC E2E
15. 관련 관리 문서 갱신
16. build/test/PowerShell parser 검증
```

---

# 19. 변경 금지

```text
- 평상시 Agent 자동 upload/GC 금지
- FileSystemWatcher를 upload 또는 GC trigger로 사용 금지
- Agent restart만으로 staging 생성/resume/GC 금지
- DEV PC에 SMB/NAS filesystem credential 배포 금지
- Agent의 Supabase 직접 접근 금지
- Server의 대용량 binary relay 금지
- 운영 TLS 검증 우회 금지
- private key 저장소 기록 금지
- 자동 git add/commit/push/pull/reset/merge/rebase/clean 금지
- active UPLOADING session 강제 삭제 금지
- Object GC를 Staging GC와 함께 무조건 실행 금지
```

---

# 20. 다음 보고에 반드시 포함

```text
- latest commit SHA
- GC 설계/구현 파일 목록
- session lifecycle 변경점
- last_activity 갱신 방식
- dry-run 실제 출력
- SAFE / KEEP / ORPHAN / REVIEW 판정 예시
- 삭제 전 staging 총 용량
- 삭제 후 staging 총 용량
- 회수된 bytes
- active session이 보존된 증거
- cleanup assertion scope 검증
- files/object inode 또는 hard-link 검증 결과
- uploader progress 실제 출력
- multi-file upload 및 cleanup 결과
- build/test 결과
```

이번 보완의 핵심 성공 기준은 다음과 같다.

> **정상 업로드가 끝난 session은 즉시 staging이 제거되고, 비정상 종료로 남은 staging은 metadata와 activity를 기준으로 안전하게 식별·정리할 수 있어야 한다.**

> **`files`와 `objects`는 hard-link/reference 구조를 먼저 확인하고, staging과 같은 단순 TTL GC 대상으로 취급하지 않는다.**
