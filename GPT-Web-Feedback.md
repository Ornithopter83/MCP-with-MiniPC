# GPT Web Feedback

Updated: 2026-09-17

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다. 단, 아래 Large Data/NAS 정책은 사용자가 2026-09-17 명시적으로 변경한 최신 정책이므로 관련 계획·task·구현은 이 기준에 맞춘다.

---

# 1. 최종 정책: 대용량 자동 전송 폐기

ProjectHub는 대용량 파일을 평상시 자동 전송하지 않는다.

금지:

```text
- FileSystemWatcher 이벤트를 upload trigger로 사용
- Agent startup 시 자동 hash/upload/resume
- 새 대용량 파일 발견 즉시 upload session 생성
- 실패 session을 백그라운드에서 자동 재개
- 사용자 명시 실행 없이 NAS staging 생성
- Server PC가 대용량 binary를 relay/cache/storage
```

허용:

```text
평상시 Agent
  -> heartbeat
  -> Git branch / HEAD / dirty / changed state
  -> last activity
  -> 관찰/상태 보고만 수행

사용자 명시 Batch Sync
  -> Git/file snapshot 확보
  -> manifest/queue 확정
  -> 빠른 control-plane metadata sync
  -> 별도 uploader process/console 시작
  -> uploader가 hash/assertion/chunk/status/resume/finalize 수행
  -> NAS 검증 완료 후 STAGED
  -> 적절한 Git checkpoint와 연결 후 CHECKPOINTED
```

대용량 data plane은 계속 다음을 유지한다.

```text
DEV PC uploader -> NAS Gateway HTTPS -> NAS1DUAL
```

control plane은:

```text
DEV PC Agent/Batch Sync -> ProjectHub.Server -> Supabase
```

Server는 binary relay를 하지 않는다.

---

# 2. 최신 구현 검토 기준

최신 확인 커밋:

```text
2e7f5e340e621ddc97a36dbf54d61e2617490052
자동 서버 동기화, 대용량 자동 업로드 정책 폐기
```

확인된 구현:

```text
[x] 평상시 Agent Program에서 large-data startup scan/upload/reconciliation 제거
[x] heartbeat + ProjectActivityMonitor/Git state 감시는 유지
[x] 명시적 ProjectHub_Sync.ps1 추가
[x] Batch 시작 시 branch/HEAD/dirty 및 대용량 path/size/mtime manifest 생성
[x] 별도 ProjectHub_LargeData_Uploader.ps1 프로세스 실행
[x] uploader에서 SHA-256, assertion, upload-start/status/chunk/finalize 구현
[x] uploader 실행 중 status 기반 resume 구현
[x] NAS object identity 검증
[x] STAGED metadata API 호출
[x] captured HEAD 기반 CHECKPOINTED API 호출
[x] 업로드 후 size/mtime이 변경된 파일을 CHANGED_DURING_UPLOAD로 제외
[x] 자동 Git add/commit/push 등은 추가하지 않음
[x] 500MiB 기존 파일의 NAS finalize/hash/size 및 dedup 기반 기능은 실제 NAS에서 검증됨
[x] build/test 및 PowerShell parser 검증 기록 있음
```

따라서 **정책 전환 자체와 기본 Batch/Uploader 구현은 완료**로 볼 수 있다.

그러나 아래 항목은 실제 완료 조건으로 추가 검증/보완해야 한다. 이 항목을 확인하기 전에는 06을 완전 종료로 간주하지 않는다.

---

# 3. 중요 보완 1: 다음 Batch에서 기존 실패 session을 실제로 resume해야 함

현재 `ProjectHub_Sync.ps1`은 실행할 때마다 새로운 `BatchId`를 만든다.

현재 uploader의 session id도 개념적으로:

```text
batch-<BatchId>-<relative-path-derived-name>
```

형태이므로, 다음 Batch를 다시 실행하면 이전 실패 session과 다른 upload session id가 만들어질 수 있다.

이 상태에서는:

```text
Batch A 업로드 중단
-> NAS staging/session A 남음

다음날 Batch B 실행
-> 새 session B 생성
-> 이전 session A는 resume되지 않음
-> orphan staging이 누적될 수 있음
```

이 문제는 이번 정책 변경의 핵심 목적 중 하나인 “staging이 반복 생성/잔존하는 문제”와 직접 연결된다.

필수 수정 방향:

```text
- 동일 project + relative_path + object identity(hash/size) 기준으로 기존 미완료 session 조회
- resume 가능한 기존 session이 있으면 같은 upload_session_id를 재사용
- assertion만 새로 발급
- upload-status로 완료 chunk 확인
- 남은 chunk부터 전송
```

새 Batch 실행 시 무조건 새 session을 만드는 구조는 피한다.

기존 session을 재사용할 수 없는 경우에만 새 session 생성.

Server/Supabase에 upload session metadata가 있다면 그 정보를 우선 사용하고, NAS staging 자체를 임의 탐색해 상태의 source of truth로 삼지 않는다.

---

# 4. 중요 보완 2: 실패/orphan staging 정리 정책

새 정책에서는 평상시 Agent가 staging을 재생성하지 않아야 한다. 이 부분은 현재 Agent 구조에서 충족되는 것으로 보인다.

추가로 다음을 보장해야 한다.

```text
finalize 성공
-> 해당 staging session 정리 확인

업로드 실패/중단
-> 명시적 다음 Batch에서 resume 가능한 session으로 보존

resume 불가능/폐기 확정 session
-> 안전한 metadata 대조 후 cleanup 후보
```

TTL cleanup을 도입할 수 있지만 다음 원칙을 지킨다.

```text
- 단순 age 기준 즉시 삭제 금지
- active/resumable session 여부 확인
- object 이미 finalize 되었는지 확인
- metadata와 NAS 상태 대조 후 삭제
```

사용자가 NAS staging을 수동 삭제한 경우에도 Agent가 평상시에 즉시 복구하지 않는다.

---

# 5. 중요 보완 3: Batch Main 단계에서 control-plane metadata를 먼저 동기화

현재 `ProjectHub_Sync.ps1`은 Git branch/HEAD/dirty와 file manifest를 로컬 JSON에 저장하고 uploader를 시작한다.

하지만 사용자가 요구한 흐름은:

```text
1. Git/file 정보를 먼저 빠르게 ProjectHub.Server에 반영
2. 그 직후 사용자는 다른 개발 작업 계속
3. 대용량은 별도 uploader가 오래 걸려도 무관
```

이다.

따라서 `Main sync is complete`라고 표시하려면 Batch main 단계에서 최소 다음 정보가 Server control plane에 먼저 반영되는 것이 바람직하다.

```text
project_id
workstation_id
captured_head_sha
captured_branch
captured_dirty
batch_id
large-file manifest summary
relative_path / size / mtime / queued state
```

기존 project-state API와 large-data metadata API를 최대한 재사용하고, 별도 복잡한 schema 추가는 최소화한다.

중요:

```text
Git 동기화 = Git 상태/metadata를 Server에 반영한다는 의미
```

이며 자동 commit/push/pull을 의미하지 않는다.

---

# 6. 중요 보완 4: 실제 새 Batch Sync 경로 E2E 필요

과거 500MiB 테스트는 NAS Gateway/upload/finalize 기본 기능 검증으로 유효하다.

하지만 새로 추가된:

```text
ProjectHub_Sync.ps1
  -> manifest
  -> 별도 ProjectHub_LargeData_Uploader.ps1
  -> Server assertion
  -> NAS Gateway
  -> STAGED
  -> CHECKPOINTED
```

전체 경로가 실제 대용량 파일로 끝까지 검증됐다는 기록은 아직 별도로 남겨야 한다.

필수 E2E:

```text
A. 실제 500MiB 이상 파일로 ProjectHub_Sync.ps1 실행
B. Main console이 uploader 완료를 기다리지 않고 반환되는지 확인
C. 별도 uploader console/process 확인
D. 실제 NAS object hash/size 확인
E. STAGED metadata 확인
F. CHECKPOINTED dataset/item 확인
G. Agent를 재시작해도 자동 staging/upload가 발생하지 않는지 확인
```

---

# 7. 중요 보완 5: 중단 -> 다음 Batch resume 실제 검증

06 완료 전 반드시 실제로 검증한다.

테스트 절차 권장:

```text
1. 새로운 대용량 테스트 파일 준비
2. ProjectHub_Sync 실행
3. 일부 chunk 전송 후 uploader 강제 종료
4. NAS staging/session과 Server session 상태 확인
5. 평상시 Agent를 켜 두어도 업로드가 재개되지 않는지 확인
6. ProjectHub_Sync를 다시 명시적으로 실행
7. 기존 upload_session_id가 재사용되는지 확인
8. upload-status의 completed_chunks를 기준으로 이어서 전송되는지 확인
9. 전체 재전송이 아닌 남은 chunk만 전송되는지 확인
10. finalize 성공
11. staging cleanup 확인
12. STAGED/CHECKPOINTED 확인
```

이 검증이 실패하면 새 Batch policy의 resume 구현이 완료된 것이 아니다.

---

# 8. 업로드 중 source 변경 처리

현재 uploader는 Batch manifest의 size/mtime과 업로드 종료 후 실제 파일을 비교해 변경된 항목을 checkpoint에서 제외한다.

이 정책을 유지한다.

```text
업로드 시작 snapshot
-> 사용자는 계속 작업 가능
-> 파일 변경 가능
-> NAS에 전송된 object 자체가 정상이어도
-> 현재 로컬 최신 파일과 다르면 CHECKPOINTED 금지
-> CHANGED_DURING_UPLOAD 또는 동등 상태
-> 다음 Batch에서 새 버전 처리
```

원본 파일을 장시간 lock하지 않는다.

---

# 9. lifecycle 의미

기존 enum/DB 호환성을 우선한다.

개념적으로:

```text
LOCAL_ONLY
-> QUEUED
-> UPLOADING
-> STAGED   # NAS 저장/검증 완료, 아직 Git checkpoint 미연결
-> CHECKPOINTED
```

예외:

```text
FAILED
CHANGED_DURING_UPLOAD
MISSING
MIGRATION_REQUIRED
```

핵심:

```text
NAS 저장 완료 != Git 버전 연결 완료
```

---

# 10. 현재 코드에서 유지해야 할 경계

```text
- FileSystemWatcher는 대용량 upload trigger가 아님
- Agent startup은 대용량 hash/upload/resume를 수행하지 않음
- private key/secret Git 기록 금지
- 운영 TLS certificate bypass 금지
- Agent에 SMB/NAS filesystem credential 배포 금지
- Server binary relay 금지
- 자동 git add/commit/push/pull/reset/merge/rebase/clean 금지
- 사용자 명시 Batch Sync 없이 대용량 upload 금지
```

실제 환경값:

```text
Gateway URL: https://dfblackbox-nas.duckdns.org:8443/projecthub/
PHP-visible root: /mnt/HDD1/ProjectHub
private key: ProjectHub.Server process 환경
public key: NAS Gateway
```

---

# 11. 문서 상태

최신 구현에서 다음은 정리된 것으로 확인됨.

```text
CurrentWork.md 활성 task -> tasks/06-large-data-nas.md
ProjectHub_IMPLEMENTATION_PLAN.md -> 06 완료 표기
CurrentWork/NewThreadHandoff -> 07 준비 표기
```

다만 위 보완 검증이 끝나기 전까지는 문서에서 06을 “구현 완료, 최종 Batch E2E/resume 검증 필요”처럼 구분해도 좋다.

07을 본격 시작하기 전에 최소한 다음 두 가지는 반드시 확인한다.

```text
1. 새 Batch 전체 실제 E2E
2. 중단 후 다음 명시적 Batch에서 기존 session resume
```

---

# 12. Codex에게 요청할 다음 작업

최신 commit `2e7f5e340e621ddc97a36dbf54d61e2617490052` 기준으로 아래를 수행한다.

```text
1. ProjectHub_Sync.ps1 / ProjectHub_LargeData_Uploader.ps1의 session identity 흐름 추적
2. 새 Batch마다 새 upload_session_id가 생겨 기존 실패 session이 orphan 되는 문제 수정
3. 동일 object identity의 resumable session을 Server metadata 기준으로 재사용
4. Batch main 단계에서 Git/file/manifest control metadata를 Server에 먼저 반영
5. 실제 500MiB 이상 파일로 새 Batch 전체 E2E
6. uploader 중간 강제 종료
7. Agent가 평상시 자동 resume하지 않는지 확인
8. 다음 명시적 Batch에서 동일 session resume 검증
9. finalize 후 staging cleanup 확인
10. STAGED/CHECKPOINTED metadata 검증
11. source 변경 시 checkpoint 제외 검증
12. build/test/PowerShell parser 검증
13. CurrentWork/tasks/06 문서에 실제 결과 기록
```

기존 검증된 Gateway/assertion/chunk/finalize/content-addressed object 로직은 재작성하지 말고 재사용한다.

---

# 13. 06 최종 완료 기준

```text
[x] 평상시 Agent 자동 upload 제거
[x] 명시적 Batch entrypoint 구현
[x] 별도 uploader process 구현
[x] snapshot manifest 구현
[x] 기존 NAS Gateway 기본 upload/finalize/dedup 기능 검증
[ ] Batch main metadata 선반영 확인
[ ] 새 Batch 전체 대용량 E2E 확인
[ ] uploader 중단 후 평상시 자동 resume가 없는지 확인
[ ] 다음 명시적 Batch에서 기존 session resume 확인
[ ] finalize 후 staging cleanup 확인
[ ] STAGED 확인
[ ] captured Git checkpoint 기반 CHECKPOINTED 확인
[ ] source 변경 시 CHANGED_DURING_UPLOAD 처리 확인
```

이 체크가 끝난 뒤 06을 완전히 닫고 07 Project 상태 API로 넘어간다.
