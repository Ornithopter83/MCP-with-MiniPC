# GPT Web Feedback

Updated: 2026-09-17

## 우선순위

1. `AGENTS.md`
2. `ProjectHub_IMPLEMENTATION_PLAN.md`
3. `CurrentWork.md`
4. 현재 활성 `tasks/*.md`
5. `GPT-Web-Feedback.md`

충돌 시 앞선 관리 문서와 활성 task를 우선한다. 단, 아래 내용은 사용자가 2026-09-17 명시적으로 요청한 **Large Data/NAS 정책 변경**이므로 관련 계획과 task를 이 정책에 맞게 정식 갱신한다.

---

# 1. 중요 정책 변경: 대용량 파일 자동 전송 폐기

사용자는 기존에 고려하던 다음 동작을 폐기하기로 했다.

```text
- FileSystemWatcher 이벤트를 계기로 대용량 파일을 자동 NAS 업로드
- Agent가 새 대용량 파일을 발견하면 자동 upload session 생성
- Agent 시작 시 large-file reconciliation 과정에서 자동 upload/resume
- 실패/중단된 upload session을 백그라운드에서 자동으로 되살림
- Git에 들어가지 않는 신규/대용량 파일을 실시간 또는 상시 자동 전송
- Server PC를 대용량 파일 relay/cache 대상으로 사용하는 동작
```

새 원칙은 다음 한 문장으로 요약한다.

> **ProjectHub는 대용량 데이터를 실시간 자동 전송하지 않는다. Agent는 평상시 상태만 관찰한다. 대용량 파일의 hash·전송·resume·checkpoint 처리는 사용자가 명시적으로 실행한 Batch Sync에서만 시작하며, 실제 전송은 별도 프로세스/콘솔에서 비동기 수행하여 Git 상태 처리와 일반 개발 작업을 차단하지 않는다.**

이 정책은 현재 NAS `ProjectHub/staging`에 사용자가 삭제한 테스트 세션/파일이 반복적으로 다시 생성되는 문제를 방지하는 목적도 있다. 사용자가 NAS staging을 수동 삭제했다고 해서 Agent가 평상시 자동으로 세션을 재생성해서는 안 된다.

---

# 2. 현재 구현/검증 상태

최신 확인 커밋:

```text
8e8488bfa1fee04e292c7576b476513884ad795d
Fix NAS finalize size validation after chunk assembly
```

현재까지 실제로 검증된 사항:

```text
[x] 05 Agent heartbeat/Git 상태 수집/실제 DEV PC E2E 완료
[x] ProjectHub.Server RS256 assertion 발급
[x] NAS Gateway public-key 검증
[x] 실제 NAS root /mnt/HDD1/ProjectHub 접근
[x] provision/write/read/delete E2E
[x] upload-start / upload-chunk / upload-status / upload-finalize 실제 실행
[x] 작은 파일 finalize + SHA-256/size 검증
[x] 동일 hash dedup already_present=true 검증
[x] 500MiB 실제 파일을 16MiB x 32 chunk로 staging에 전송
[x] bytes_received=524288000 확인
[x] finalize size cache 문제를 보정하기 위해 clearstatcache 추가
```

500MiB 실파일 테스트에서는 chunk 전송 자체는 완료됐고, finalize에서 `size_mismatch`가 발생해 `upload-finalize.php`에 `clearstatcache(true, $assembled)` 보정이 추가됐다. 이 finalize 재검증은 계속 수행할 수 있지만, 이후 자동 upload/reconciliation 설계는 아래 Batch Sync 정책으로 변경한다.

---

# 3. 새 사용자 경험: 명시적 Batch Sync

사용자는 평상시 Agent가 파일을 자동 전송하는 방식 대신, 필요할 때 하나의 일괄 스크립트를 명시적으로 실행하는 방식을 원한다.

권장 사용자 흐름:

```text
사용자
  -> ProjectHub_Sync.cmd (또는 동등한 명시적 launcher) 실행

첫 번째/메인 처리
  -> 프로젝트 Git 상태 수집
  -> branch / HEAD / dirty / 변경 파일 정보 수집
  -> 대용량 파일 inventory/manifest snapshot 생성
  -> Server에 빠른 metadata/state 동기화
  -> 대용량 전송 대상 queue 확정
  -> 별도 uploader process/console 시작
  -> 메인 처리는 빠르게 종료 또는 사용자가 다른 작업을 계속할 수 있는 상태로 전환

별도 Upload Console
  -> queue에 확정된 파일만 hash/전송
  -> upload session/assertion 발급
  -> Agent/Upload worker -> NAS Gateway HTTPS
  -> chunk/status/resume/finalize
  -> NAS SHA-256 + size 검증
  -> Server/Supabase 상태 갱신
```

대용량 전송이 수십 분 걸려도 Git/파일 상태 동기화가 끝난 뒤 사용자는 프로젝트 작업을 계속할 수 있어야 한다.

---

# 4. 평상시 Agent의 역할 축소

평상시 Agent는 다음을 계속 수행할 수 있다.

```text
- heartbeat
- branch / HEAD / dirty
- changed/untracked/deleted file 상태
- last file activity
- 대용량 파일 존재 여부 또는 LOCAL_ONLY/미동기화 여부 표시
```

그러나 평상시 Agent는 다음을 수행하지 않는다.

```text
- 대용량 SHA-256 계산을 이유로 장시간 작업 점유
- upload session 생성
- NAS chunk upload
- 자동 resume
- staging 재생성
- background retry를 통한 대용량 자동 전송
```

`LargeDataScanner`가 유지된다면 기본 역할은 **inventory/detection only**로 제한한다. FileSystemWatcher/ProjectActivityMonitor와 LargeData upload 시작 경로를 분리한다.

---

# 5. Batch Sync는 snapshot 기반으로 동작

Batch Sync를 실행한 시점의 작업 대상을 고정해야 한다.

Batch 시작 시 최소 다음 정보를 기록한다.

```text
batch_id
project_id
workstation_id
captured_head_sha
captured_branch
captured_dirty
relative_path
size_bytes
last_write_time
필요 시 file identity/hash 상태
```

이 snapshot이 uploader의 입력 queue가 된다.

Uploader는 watcher가 발견하는 최신 파일 목록을 실시간으로 따라가지 않는다. Batch가 시작된 뒤 새 파일이 생기거나 기존 파일이 수정되더라도 현재 batch에 임의로 끼워 넣지 않는다. 새 변경은 다음 Batch Sync에서 처리한다.

---

# 6. 업로드 중에도 사용자는 파일/프로젝트를 수정할 수 있어야 함

대용량 전송을 위해 원본 파일이나 프로젝트 전체를 장시간 lock하지 않는다.

대신 업로드 시작 시와 완료 시 file identity를 비교한다.

최소 비교 후보:

```text
size_bytes
last_write_time
가능하면 시작 시 계산된 SHA-256 또는 안정성 fingerprint
```

전송 중 원본이 변경된 경우:

```text
NAS에 전송된 object 자체가 hash/size 검증에 성공했더라도
현재 로컬 파일의 최신 상태와 동일하다고 간주하지 않는다.
```

상태 예:

```text
CHANGED_DURING_UPLOAD
```

이 경우 방금 저장된 NAS object를 현재 작업트리의 최신 파일로 잘못 `CHECKPOINTED` 처리하지 않는다. 다음 Batch Sync에서 새 버전을 다시 queue한다.

---

# 7. 권장 lifecycle 단순화

기존 enum/DB 호환성을 크게 깨지 않는 선에서 의미를 명확히 한다.

권장 개념 상태:

```text
LOCAL_ONLY
  -> Batch Sync 대상 확정
QUEUED
  -> uploader 시작
UPLOADING
  -> Gateway finalize + SHA-256/size 검증 완료
STORED (기존 STAGED를 유지해도 됨)
  -> 특정 Git commit/LargeDataSet에 연결 완료
CHECKPOINTED
```

예외 상태:

```text
FAILED
CHANGED_DURING_UPLOAD
MISSING
MIGRATION_REQUIRED
```

기존 DB/API에서 `STAGED`가 이미 널리 사용됐다면 새 `STORED` enum을 억지로 추가하지 말고 `STAGED = NAS 저장 및 검증 완료, 아직 Git checkpoint 미연결` 의미를 유지해도 된다.

핵심은 다음이다.

```text
NAS 저장 완료 != Git 버전 연결 완료
```

---

# 8. Git과 대용량 업로드는 서로 기다리지 않는다

Batch Sync의 빠른 메인 처리는 Git 상태와 파일 manifest를 먼저 Server에 반영한다.

대용량 전송이 끝날 때까지 Git 관련 일반 작업을 차단하지 않는다.

예:

```text
Batch 시작 시 HEAD = AAA
8GB object = QUEUED/UPLOADING

메인 상태 동기화 완료
-> 사용자는 계속 개발 가능

나중에 NAS finalize 성공
-> STAGED
-> 적절한 LargeDataSet/checkpoint 연결 조건이 만족되면 CHECKPOINTED
```

중요: 현재 ProjectHub 정책상 **자동 git add/commit/push/pull/reset/merge/rebase/clean은 여전히 금지**한다. 여기서 “Git 동기화”는 우선 branch/HEAD/dirty/file state 및 manifest를 ProjectHub.Server에 동기화하는 의미다. 실제 commit/push 자동화는 별도의 사용자 정책 변경 없이는 추가하지 않는다.

Git commit과 LargeDataSet 연결 시에는 반드시 어떤 HEAD/snapshot에 대한 object인지 혼동하지 않도록 `captured_head_sha` 또는 명시적 checkpoint 기준을 사용한다.

---

# 9. 별도 Upload Console / 프로세스

사용자가 원하는 UX는 메인 작업과 대용량 업로드를 분리하는 것이다.

권장 형태:

```text
ProjectHub_Sync.cmd
  -> 빠른 scan/metadata sync
  -> upload queue 생성
  -> 별도 process/console로 uploader 실행
```

예시 UX:

```text
[Main]
Git 상태 검사 ........ OK
HEAD ................ abc123
변경 파일 ............ 17
대용량 파일 .......... 3
NAS 미동기화 ......... 2
ProjectHub 상태 전송 .. OK
Large Data uploader 시작됨
메인 작업 완료
```

별도 콘솔:

```text
[ProjectHub Large Data Upload]
[1/2] GameAssets.zip
  4.1 / 9.3 GB   44%
[2/2] Video.raw
  Waiting

업로드 중에도 프로젝트 작업을 계속할 수 있습니다.
```

Codex는 구현상 Agent subcommand, 별도 console project, worker executable 중 현재 구조에 가장 작은 변경으로 들어가는 방식을 선택할 수 있다. 중요한 것은 **실제 uploader가 별도 프로세스로 실행되어 메인 Sync를 장시간 blocking하지 않는 것**이다.

---

# 10. Assertion/session 갱신은 uploader가 자동 처리

사용자가 수동 JWT를 복사하는 방식은 테스트용으로만 남긴다.

운영 uploader는:

```text
Server에서 upload session + short-lived assertion 요청
-> NAS Gateway 호출
-> assertion 만료 시 Server에서 새 assertion 발급
-> 동일 upload session으로 status 확인 후 resume
```

해야 한다.

즉:

```text
upload_session_id = 장기 작업의 연속성
assertion/JWT      = 짧게 갱신되는 권한
```

으로 분리한다.

단, 이러한 resume는 **명시적 Batch Sync/uploader 실행 중에만 자동으로 수행**한다. Agent가 평상시 백그라운드에서 자동 재개하지 않는다.

---

# 11. NAS staging 정책 수정

현재 사용자가 NAS `ProjectHub/staging`의 테스트 대용량 데이터를 수동 삭제해도 다시 생성되는 현상을 경험했다.

새 정책에서는 다음을 보장한다.

```text
평상시 Agent 실행
-> staging 생성 금지

FileSystemWatcher 이벤트
-> staging 생성 금지

Agent 재시작
-> staging 자동 복구/재생성 금지

명시적 Batch Sync/Uploader 실행
-> 필요 session만 staging 생성 허용
```

세션 정리 정책:

```text
finalize 성공
-> 해당 staging session 자동 정리

업로드 실패/사용자 중단
-> resume를 위해 session을 보존할 수 있음
-> 그러나 평상시 자동 재생성/자동 resume 금지

다음 Batch Sync 실행
-> 기존 미완료 session을 발견
-> resume 가능한 항목으로 표시/선택 또는 정책에 따라 명시적으로 resume
```

추가로 orphan/오래된 staging에 대해 TTL cleanup 정책을 둘 수 있지만, 자동 삭제는 안전한 기준과 metadata 대조 후에만 한다.

사용자가 NAS에서 수동으로 session을 삭제한 경우, 평상시 Agent가 이를 즉시 복구하지 않는다. 다음 명시적 Batch Sync에서 Server/NAS 상태를 재평가한다.

---

# 12. Startup Reconciliation 정책 변경

기존 계획에 있던 startup reconciliation은 자동 복구/자동 업로드 개념을 제거한다.

Agent 시작 시 허용:

```text
- Git 상태 재수집
- heartbeat/state 갱신
- large file 존재/metadata 상태 확인
- 미완료 upload session이 있다는 사실 표시
```

Agent 시작 시 금지:

```text
- 자동 hashing 대량 수행
- 자동 upload session 생성
- 자동 chunk upload
- 자동 resume
- NAS staging 재생성
```

미완료 대용량 작업은 다음 Batch Sync에서 처리한다.

---

# 13. Server PC에 대용량 파일 전송/relay 금지 유지

대용량 binary data plane은 기존 원칙을 유지한다.

```text
DEV PC Batch Uploader
  -> NAS Gateway HTTPS
  -> NAS1DUAL
```

control plane:

```text
DEV PC Agent/Batch Sync
  -> ProjectHub.Server
  -> Supabase metadata
```

ProjectHub.Server는 대용량 파일 relay/cache/storage가 아니다.

---

# 14. 현재 코드에서 수정해야 할 구체 항목

Codex는 먼저 최신 코드를 확인하고 실제 호출 관계를 추적한 뒤 다음을 수정한다.

## A. 자동 upload trigger 제거/비활성화

확인 대상:

```text
ProjectActivityMonitor/FileSystemWatcher
Agent startup path
LargeDataScanner 호출부
startup reconciliation 관련 코드
upload retry/resume runner
```

어떤 경로에서도 평상시 파일 이벤트나 Agent 시작만으로 `upload-start`, chunk upload, staging 생성이 일어나지 않게 한다.

## B. LargeDataScanner를 inventory-only로 전환

대용량 파일 탐지는 허용하지만 자동 hash/upload trigger로 연결하지 않는다.

필요하면 빠른 탐지 단계에서는 size/path/mtime만 기록하고, SHA-256은 Batch Sync queue가 확정된 이후 uploader 또는 별도 hashing 단계에서 수행한다.

## C. 명시적 Batch Sync entrypoint 추가

사용자가 직접 실행할 수 있는 명확한 entrypoint를 만든다.

후보:

```text
ProjectHub_Sync.cmd
ProjectHub_Sync.ps1
또는 Agent/별도 console의 explicit sync command
```

메인 단계는 빠르게 끝나야 한다.

## D. 별도 uploader process/console 추가

Batch manifest/queue를 입력받아 실제 대용량 전송을 담당한다.

요구:

```text
- 별도 process
- 진행률 표시
- chunk/resume
- assertion 자동 갱신
- finalize/hash/size 검증
- 완료 후 Server metadata 상태 갱신
- 실패 상태 기록
```

## E. Batch snapshot/batch_id 도입

Uploader가 실시간 watcher 상태를 따라가지 않도록 batch 시작 시 queue를 고정한다.

Supabase schema를 무조건 크게 바꾸기 전에 기존 metadata table로 표현 가능한지 먼저 검토한다. 필요하면 최소한의 `batch_id`/captured HEAD/snapshot metadata를 추가한다.

## F. 원본 변경 감지

업로드가 진행되는 동안 사용자가 계속 작업할 수 있으므로, 시작 snapshot과 완료 시 로컬 상태를 비교한다.

변경된 경우 현재 파일을 잘못 checkpoint하지 않는다.

## G. staging lifecycle 정리

`upload-finalize.php` 성공 시 session staging이 실제로 정리되는지 확인하고, 그렇지 않으면 명시적으로 cleanup한다.

실패 session은 명시적 Batch Sync 재개를 위해 보존 가능하지만 평상시 자동 재생성 금지.

## H. 자동 retry/resume 범위 제한

retry/resume는 uploader process가 살아 있거나 사용자가 다음 Batch Sync를 명시적으로 실행했을 때만 허용한다.

## I. 문서/상태 정합성

다음 문서를 새 정책으로 갱신한다.

```text
ProjectHub_IMPLEMENTATION_PLAN.md
CurrentWork.md
tasks/06-large-data-nas.md
NewThreadHandoff.md
필요 시 AGENTS.md의 관련 운영 정책
```

`CurrentWork.md` 상단의 오래된 `활성 작업: tasks/05_agent-state.md` 표기도 실제 현재 작업인 `tasks/06-large-data-nas.md`로 수정한다.

---

# 15. 06 완료 기준 재정의

기존 06 완료 기준에서 “Agent가 상시 자동 대용량 upload/reconciliation을 수행한다”는 요구는 제거한다.

새 완료 기준:

```text
[ ] 최신 upload-finalize.php로 실파일 finalize/hash/size 검증 성공
[ ] 명시적 Batch Sync entrypoint 동작
[ ] 메인 metadata/Git-state sync가 대용량 전송을 기다리지 않고 완료
[ ] 별도 uploader console/process에서 대용량 전송 수행
[ ] Server에서 assertion 자동 발급/갱신
[ ] chunk/status/resume/finalize 성공
[ ] finalize 성공 후 staging cleanup
[ ] 동일 hash dedup
[ ] STAGED(또는 동등한 NAS 저장 완료 상태) metadata 반영
[ ] 업로드 중 원본 변경 시 CHANGED_DURING_UPLOAD 또는 동등 상태 처리
[ ] Git commit/LargeDataSet/CHECKPOINTED 연결 검증
[ ] Agent restart만으로 자동 upload/staging 재생성이 일어나지 않음
[ ] 다음 명시적 Batch Sync에서 미완료 session을 안전하게 resume 가능
```

---

# 16. Codex 수행 순서

이 정책 변경은 기존 06 구현을 폐기하는 것이 아니라 **자동 실행 정책을 제거하고 명시적 Batch Sync로 재배치하는 작업**이다. 이미 검증된 Gateway, assertion, chunk, resume, finalize, content-addressed object 로직은 최대한 재사용한다.

권장 수행 순서:

```text
1. 최신 저장소/관리 문서/06 task 확인
2. 자동 upload/staging 생성 trigger의 실제 호출 경로 추적
3. 자동 trigger 비활성화 또는 제거
4. Batch Sync snapshot/queue 설계 최소화
5. 명시적 launcher 구현
6. 별도 uploader process/console로 기존 upload 로직 이동/연결
7. staging cleanup 및 실패-session 정책 정리
8. 업로드 중 원본 변경 감지
9. small/medium 파일로 Batch Sync E2E
10. 실제 500MiB 이상 파일로 E2E
11. STAGED -> Git checkpoint -> CHECKPOINTED 검증
12. Agent 종료/재시작 후 자동 업로드가 일어나지 않는지 검증
13. 다음 Batch Sync에서 resume 검증
14. 관리 문서와 task 결과 갱신
15. build/test 수행
```

---

# 17. 변경 금지 / 주의

```text
- 자동 git add/commit/push/pull/reset/merge/rebase/clean 추가 금지
- private key/secret Git 기록 금지
- 운영 TLS certificate bypass 금지
- DEV PC에 SMB/NAS filesystem credential 배포 금지
- Server가 대용량 binary relay 금지
- 사용자 명시 실행 없이 대용량 upload 시작 금지
- FileSystemWatcher를 upload trigger로 재사용 금지
```

이미 검증된 실제 NAS 환경값은 유지한다.

```text
Gateway URL: https://dfblackbox-nas.duckdns.org:8443/projecthub/
PHP-visible root: /mnt/HDD1/ProjectHub
public key: NAS Gateway 측에서만 사용
private key: ProjectHub.Server process 환경으로 주입
```

---

# 18. Codex에게 기대하는 다음 보고

다음 작업 완료 후 보고에는 최소 다음을 포함한다.

```text
- 어떤 자동 upload trigger를 제거/비활성화했는지
- Agent 시작/FileSystemWatcher가 NAS staging을 만들지 않는 증거
- Batch Sync entrypoint 파일/명령
- 메인 sync와 uploader가 별도 process로 분리된 증거
- batch snapshot/queue 구조
- uploader progress/resume/assertion refresh 동작
- finalize 후 staging cleanup 결과
- 업로드 중 source 변경 처리 결과
- STAGED/CHECKPOINTED metadata 결과
- 실제 대용량 파일 E2E 결과
- build/test 결과
- 문서 갱신 내역
```

이번 정책 변경 이후 핵심 성공 기준은 **“대용량 파일이 자동으로 움직이지 않고, 사용자가 명시적으로 Sync를 실행했을 때만 별도 uploader가 전송하며, 사용자는 전송 중에도 개발을 계속할 수 있다”**이다.
